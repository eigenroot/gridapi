using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.UtilityAccounts;
using Grid.Api.Integrations.Hubtel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.BillPayments;

/// <summary>
/// The customer side: look a bill up, place an order (which prompts the wallet), and read orders.
/// </summary>
public sealed class OrderService(GridDbContext db, CurrentConsumer current, HubtelPayments hubtel, OrderTransitions transitions,
    CallbackUrls callbacks, IOptions<BillPaymentOptions> options, TimeProvider time)
{
    private BillPaymentOptions Config => options.Value;
    private DateTimeOffset Now => time.GetUtcNow();

    public IReadOnlyList<ProductResponse> Products() =>
        BillProducts.All.Select(p => new ProductResponse(p.Code, p.Type, p.Name, p.Lookup, p.Network, p.PhoneDestination, Rule(p),
            Config.Enabled && Config.IsEnabled(p))).ToList();

    /// <summary>Hubtel only lets a customer be prompted on their own verified number, so that is the payer.</summary>
    public async Task<ChannelsResponse> ChannelsAsync(CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        return new(HubtelPayments.Channels, HubtelPayments.SuggestChannel(consumer.PhoneNumber), consumer.PhoneNumber);
    }

    public async Task<LookupResponse> LookupAsync(LookupRequest request, CancellationToken ct)
    {
        Config.RequireEnabled();
        var product = RequireProduct(request.ProductCode);
        var consumer = await current.LoadAsync(ct);
        var found = await ResolveAsync(product, consumer, request.AccountNumber, request.PhoneNumber, request.MeterNumber, ct);
        return new(product.Code, found.AccountNumber, found.CustomerName, found.NameConfirmed, found.BeneficiaryPhone, MoneyDto.From(found.AmountDue),
            found.Bundles.Select(b => new BundleResponse(b.Value, b.Label, MoneyDto.From(b.Amount))).ToList(), Rule(product), Config.ChargePercent);
    }

    /// <summary>
    /// Places the order and prompts the customer's wallet. The lookup is repeated here so the
    /// price, bundle and Ghana Water session are fresh. The order is committed as Submitting before
    /// Hubtel is called, so a crash mid-call leaves it for status checks, never for a second prompt.
    /// </summary>
    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, string idempotencyKey, CancellationToken ct)
    {
        Config.RequireEnabled();
        var consumer = await current.LoadAsync(ct);
        var keyHash = KeyHash(idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(request));
        if (await FindReplayAsync(consumer.Id, keyHash, requestHash, ct) is { } replay) return await DescribeAsync(replay, ct);

        var product = RequireProduct(request.ProductCode);
        var channel = request.Channel ?? HubtelPayments.SuggestChannel(consumer.PhoneNumber);
        if (HubtelPayments.Channels.All(c => c.Code != channel))
            throw Errors.Validation([new("channel", "Choose your mobile money network.")]);
        var amount = (request.Amount ?? throw Errors.Validation([new("amount", "Enter an amount.")])).Parse();

        var found = await ResolveAsync(product, consumer, request.AccountNumber, request.PhoneNumber, request.MeterNumber, ct);
        string? bundle = found.Bundle;
        if (product.FixedPrice)
        {
            bundle = found.Bundles.FirstOrDefault(b => b.Value == request.Bundle && b.Amount == amount)?.Value
                ?? throw Errors.Validation([new("bundle", "Choose one of the bundles offered, at its price.")]);
        }
        else if (amount < Config.MinAmount || amount > Max(product))
            throw Errors.Validation([new("amount", $"Pay between {Config.MinAmount:0.00} and {Max(product):0.00} GHS.")]);

        await RequireOwnedAsync(consumer.Id, product, found, request.SavedAccountId, request.PropertyId, ct);
        var email = product.Lookup == ProductLookup.WaterMeter ? ReceiptEmail(consumer) : null;
        var fee = Config.FeeFlat + decimal.Round(amount * Config.FeePercent / 100m, 2, MidpointRounding.AwayFromZero);

        var order = new Order
        {
            ConsumerId = consumer.Id, SavedAccountId = request.SavedAccountId, PropertyId = request.PropertyId,
            ProductCode = product.Code, AccountNumber = found.AccountNumber, Destination = found.Destination, Bundle = bundle,
            SessionId = found.SessionId, ReceiptEmail = email, CustomerName = found.CustomerName, NameConfirmed = found.NameConfirmed,
            Principal = amount, Fee = fee, Total = amount + fee,
            ClientReference = "", Channel = channel!, PayerPhone = consumer.PhoneNumber,
            PaymentState = TxnState.Submitting, Status = OrderStatus.AwaitingPayment,
            IdempotencyKeyHash = keyHash, RequestHash = requestHash,
            NextCheckAt = Now + Config.StatusCheckDelay,
        };
        order.ClientReference = "C" + order.Id.ToString("N");

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await OrderLocks.AdvisoryAsync(db, $"order:{consumer.Id}:{keyHash}", ct);
            if (await FindReplayAsync(consumer.Id, keyHash, requestHash, ct) is { } raced)
            {
                await tx.CommitAsync(ct);
                return await DescribeAsync(raced, ct);
            }
            db.Orders.Add(order);
            transitions.Event(order.Id, "OrderPlaced", $"{product.Code} {order.Total:0.00}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // From here on the customer leaving must not stop us recording what Hubtel said.
        var result = await hubtel.ReceiveMoneyAsync(order.ClientReference, order.PayerPhone, order.Channel, order.Total, $"GRID {product.Name}",
            NullIfBlank(consumer.FullName), NullIfBlank(consumer.Email), callbacks.For(CallbackKind.Payment, order.Id), CancellationToken.None);
        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync(CancellationToken.None))
        {
            order = (await OrderLocks.LockAsync(db, order.Id, CancellationToken.None))!;
            transitions.PaymentSubmitted(order, result);
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }
        return await DescribeAsync(order, CancellationToken.None);
    }

    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken ct) => await DescribeAsync(await OwnedAsync(id, ct), ct);

    public async Task<CursorPage<OrderResponse>> HistoryAsync(Guid? before, int limit, CancellationToken ct)
    {
        await current.LoadAsync(ct);
        if (limit is < 1 or > 100) throw Errors.Validation([new("limit", "Use 1–100.")]);
        var query = db.Orders.AsNoTracking().Where(o => o.ConsumerId == current.Id);
        if (before is { } cursor)
        {
            var prior = await OwnedAsync(cursor, ct);
            query = query.Where(o => o.CreatedAt < prior.CreatedAt || (o.CreatedAt == prior.CreatedAt && o.Id.CompareTo(prior.Id) < 0));
        }
        var rows = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var ids = page.Select(o => o.Id).ToList();
        var bills = await db.Bills.AsNoTracking().Where(b => ids.Contains(b.OrderId)).ToDictionaryAsync(b => b.OrderId, ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => ids.Contains(r.OrderId)).ToDictionaryAsync(r => r.OrderId, ct);
        return new(page.Select(o => OrderViews.Describe(o, bills.GetValueOrDefault(o.Id), refunds.GetValueOrDefault(o.Id))).ToList(),
            rows.Count > limit ? page[^1].Id : null);
    }

    public async Task<ReceiptResponse> ReceiptAsync(Guid id, CancellationToken ct)
    {
        var order = await OwnedAsync(id, ct);
        if (order.PaymentState != TxnState.Succeeded) throw Errors.Conflict("payments.receipt_not_ready", "This order hasn't been paid yet.");
        var bill = await db.Bills.AsNoTracking().SingleOrDefaultAsync(b => b.OrderId == id && b.State == TxnState.Succeeded, ct);
        return new(order.Id, OrderViews.Reference(order), order.ProductCode, order.AccountNumber, order.CustomerName,
            MoneyDto.From(order.Principal), MoneyDto.From(order.Fee), MoneyDto.From(order.Total), MoneyDto.From(order.Charges),
            MoneyDto.From(order.AmountCharged), order.HubtelTransactionId, order.PaidAt,
            bill is null ? null : new ReceiptBill(BillProducts.Require(order.ProductCode).ResultKind, bill.ExternalReference, bill.DeliveredAt));
    }

    // ---- Beneficiary lookup ----

    private sealed record Beneficiary(string AccountNumber, string Destination, string? Bundle, string? SessionId, string CustomerName,
        string BeneficiaryPhone, decimal? AmountDue, IReadOnlyList<BundleOption> Bundles)
    {
        public bool NameConfirmed => !string.IsNullOrWhiteSpace(CustomerName);
    }

    private async Task<Beneficiary> ResolveAsync(BillProduct product, Consumer consumer, string? accountNumber, string? phoneNumber,
        string? meterNumber, CancellationToken ct)
    {
        // Airtime and data are bought for a phone; ECG and water accounts are looked up by the phone they're linked to.
        var phoneInput = phoneNumber ?? (product.PhoneDestination ? accountNumber : null) ?? consumer.PhoneNumber;
        var phone = PhoneNumbers.Normalize(phoneInput, product.PhoneDestination && phoneNumber is null ? "accountNumber" : "phoneNumber");

        switch (product.Lookup)
        {
            case ProductLookup.None:
                return new(phone, phone, null, null, "", phone, null, []);

            case ProductLookup.Bundles:
            {
                var bundles = (await hubtel.LookupAsync(product, phone, null, ct)).Bundles ?? [];
                if (bundles.Count == 0) throw Errors.NotFound("bills.no_bundles", "No bundles are available for this number right now.");
                return new(phone, phone, null, null, "", phone, null, bundles);
            }

            case ProductLookup.PhoneMeters:
            {
                var wanted = meterNumber ?? accountNumber;
                if (wanted is not null) wanted = Normalize(product, wanted, meterNumber is null ? "accountNumber" : "meterNumber");
                var meters = (await hubtel.LookupAsync(product, phone, null, ct)).Candidates;
                BeneficiaryCandidate chosen;
                // Paying a meter that isn't linked to this phone links it, so its owner can't be confirmed first.
                if (wanted is not null) chosen = meters.FirstOrDefault(m => m.AccountNumber == wanted) ?? new(wanted, "", null);
                else if (meters.Count == 1) chosen = meters[0];
                else if (meters.Count == 0) throw Errors.NotFound("bills.no_meters", "No ECG meters are linked to this phone. Enter the meter number.");
                else throw new ApiException(409, "bills.meter_selection_required", "Choose the meter to pay for.",
                    meters.Select(m => new { meterNumber = m.AccountNumber, customerName = m.CustomerName, amountDue = MoneyDto.From(m.AmountDue) }));
                return new(chosen.AccountNumber, phone, chosen.AccountNumber, null, chosen.CustomerName, phone, chosen.AmountDue, []);
            }

            case ProductLookup.WaterMeter:
            {
                var meter = Normalize(product, accountNumber, "accountNumber");
                var lookup = await hubtel.LookupAsync(product, meter, phone, ct);
                var found = lookup.Candidates.Single();
                return new(meter, phone, meter, lookup.SessionId, found.CustomerName, phone, found.AmountDue, []);
            }

            default:
            {
                var number = Normalize(product, accountNumber, "accountNumber");
                var lookup = await hubtel.LookupAsync(product, number, null, ct);
                var found = lookup.Candidates.Single();
                // Telecel Broadband takes the account from its lookup in Extradata.bundle; TV takes nothing extra.
                var bundle = product.Type == UtilityType.Internet ? lookup.Bundle ?? number : null;
                return new(number, number, bundle, null, found.CustomerName, phone, found.AmountDue, []);
            }
        }
    }

    // ---- Helpers ----

    private async Task RequireOwnedAsync(Guid consumerId, BillProduct product, Beneficiary found, Guid? savedAccountId, Guid? propertyId, CancellationToken ct)
    {
        if (propertyId is { } property && !await db.Properties.AnyAsync(p => p.Id == property && p.OwnerConsumerId == consumerId && p.ArchivedAt == null, ct))
            throw Errors.NotFound("properties.not_found", "That home was not found.");
        if (savedAccountId is { } saved)
        {
            var account = await db.UtilityAccounts.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == saved && a.OwnerConsumerId == consumerId && a.ArchivedAt == null, ct)
                ?? throw Errors.NotFound("utility_accounts.not_found", "That saved account was not found.");
            if (account.Type != product.Type || (account.AccountNumber != found.AccountNumber && account.AccountNumber != found.Destination))
                throw Errors.Conflict("bills.saved_account_mismatch", "That saved account doesn't match this bill.");
        }
    }

    private string ReceiptEmail(Consumer consumer)
    {
        var email = string.IsNullOrWhiteSpace(consumer.Email) ? Config.Hubtel.FallbackReceiptEmail : consumer.Email;
        return string.IsNullOrWhiteSpace(email)
            ? throw new ApiException(400, "bills.email_required", "Add an email address to your profile to pay Ghana Water.")
            : email;
    }

    private async Task<Order?> FindReplayAsync(Guid consumerId, string keyHash, string requestHash, CancellationToken ct)
    {
        var existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.ConsumerId == consumerId && o.IdempotencyKeyHash == keyHash, ct);
        if (existing is not null && existing.RequestHash != requestHash)
            throw Errors.Conflict("payments.idempotency_conflict", "This Idempotency-Key was already used for a different order.");
        return existing;
    }

    private async Task<OrderResponse> DescribeAsync(Order order, CancellationToken ct)
    {
        var bill = await db.Bills.AsNoTracking().SingleOrDefaultAsync(b => b.OrderId == order.Id, ct);
        var refund = await db.Refunds.AsNoTracking().SingleOrDefaultAsync(r => r.OrderId == order.Id, ct);
        return OrderViews.Describe(order, bill, refund);
    }

    private async Task<Order> OwnedAsync(Guid id, CancellationToken ct)
    {
        await current.LoadAsync(ct);
        return await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.ConsumerId == current.Id, ct)
            ?? throw Errors.NotFound("orders.not_found", "That order was not found.");
    }

    private BillProduct RequireProduct(string? code)
    {
        var product = BillProducts.Require(code);
        if (!Config.IsEnabled(product)) throw new ApiException(503, "bills.product_paused", $"{product.Name} payments are paused right now.");
        return product;
    }

    private AmountRule Rule(BillProduct product) =>
        product.FixedPrice ? new(null, null, true) : new(MoneyDto.From(Config.MinAmount), MoneyDto.From(Max(product)), false);

    private decimal Max(BillProduct product) => Math.Min(Config.MaxAmount, product.MaxAmount ?? decimal.MaxValue);

    private static string Normalize(BillProduct product, string? input, string field) =>
        UtilityTypeRules.Normalize(product.Type, input, out var error)
        ?? throw new ApiException(400, "utility_accounts.invalid_number", error!, fields: [new FieldError(field, error!)]);

    private static string KeyHash(string key) =>
        string.IsNullOrWhiteSpace(key) || key.Length > 128
            ? throw new ApiException(400, "payments.idempotency_required", "Send an Idempotency-Key header of 1–128 characters.")
            : Hash(key);

    internal static string Hash(string input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
