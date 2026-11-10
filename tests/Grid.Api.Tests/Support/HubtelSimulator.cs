using System.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grid.Api.Tests.Support;

/// <summary>
/// A real loopback HTTP server shaped after Hubtel's published samples for Direct Receive Money,
/// Commission Services, Transaction Status, Refund and Balance &amp; Transfers. It proves the
/// caller's behaviour; it does not prove anything about a live Hubtel account.
/// </summary>
public sealed class HubtelSimulator : IAsyncDisposable
{
    public const string ApiId = "test-api-id", ApiKey = "test-api-key", Collection = "11684", Disbursement = "11691";
    public const string EcgService = "e6d6bac062b5499cb1ece1ac3d742a84", WaterService = "6c1e8a82d2e84feeb8bfd6be2790d71d",
        DstvService = "297a96656b5846ad8b00d5d41b256ea7", MtnDataService = "b230733cd56b4a0fad820e39f66bc27c",
        MtnAirtimeService = "fdd76c884e614b1c8f669a3207b09a98", BroadbandService = "b9a1aa246ba748f9ba01ca4cdbb3d1d3";

    private WebApplication? _app;
    private readonly State _state = new();
    public HttpClient Http { get; private set; } = null!;
    public string Url { get; private set; } = "";

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.Map("/{**path}", _state.HandleAsync);
        await _app.StartAsync();
        Url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Http = new HttpClient { BaseAddress = new Uri(Url + "/") };
    }

    /// <summary>
    /// collection: accept | reject | http-error | lost | slow | forbidden.
    /// purchase: accept | reject | ambiguous | http-error | lost | slow | forbidden.
    /// refund: accept | ineligible.
    /// </summary>
    public void Configure(string? collection = null, string? purchase = null, string? refund = null)
    {
        if (collection is not null) _state.CollectionMode = collection;
        if (purchase is not null) _state.PurchaseMode = purchase;
        if (refund is not null) _state.RefundMode = refund;
    }

    public void SetBalances(decimal? collection = null, decimal? floatBalance = null)
    {
        if (collection is { } c) _state.CollectionBalance = c;
        if (floatBalance is { } f) _state.FloatBalance = f;
    }

    /// <summary>The meters an ECG phone lookup returns: (display name, meter, outstanding).</summary>
    public void SetMeters(params (string Name, string Meter, decimal Amount)[] meters) => _state.Meters = meters;

    /// <summary>Receive money / commission: Paid | Unpaid | Refunded. Transfers: success | failed.</summary>
    public void SetStatus(string reference, string status, decimal? amount = null)
    {
        var item = _state.Transactions[reference];
        item.Status = status;
        if (amount is { } a) item.Amount = a;
        if (item.Kind == "transfer" && status == "success") { _state.CollectionBalance -= item.Amount; _state.FloatBalance += item.Amount; }
    }

    public Sim Snapshot => _state.Snapshot();

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
    }

    public sealed record Sim(int CollectionCalls, int PurchaseCalls, int StatusCalls, int RefundCalls, int TransferCalls, int LookupCalls,
        IReadOnlyList<SimTransaction> Transactions, IReadOnlyList<(string OrderId, string CallbackUrl)> Refunds)
    {
        public SimTransaction Collection => Transactions.Single(x => x.Kind == "collection");
        public SimTransaction Purchase => Transactions.Single(x => x.Kind == "purchase");
        public SimTransaction Find(string reference) => Transactions.Single(x => x.Reference == reference);
    }

    public sealed class SimTransaction
    {
        public required string Kind { get; init; }
        public required string Reference { get; init; }
        public required string CallbackUrl { get; init; }
        public required decimal Amount { get; set; }
        public required string Status { get; set; }
        public required JsonElement Body { get; init; }
        public string TransactionId { get; } = Guid.NewGuid().ToString("N");
        public string? ServiceId { get; init; }
    }

    private sealed class State
    {
        public readonly ConcurrentDictionary<string, SimTransaction> Transactions = new();
        public readonly ConcurrentQueue<(string, string)> RefundRequests = new();
        public string CollectionMode = "accept", PurchaseMode = "accept", RefundMode = "accept";
        public decimal CollectionBalance = 5000m, FloatBalance = 10000m;
        public (string Name, string Meter, decimal Amount)[] Meters = [("TEST CUSTOMER", "P191177631", -1.1432m)];
        private int _collections, _purchases, _status, _refunds, _transfers, _lookups;

        public Sim Snapshot() => new(_collections, _purchases, _status, _refunds, _transfers, _lookups,
            Transactions.Values.ToList(), RefundRequests.ToList());

        public async Task HandleAsync(HttpContext ctx)
        {
            var path = ctx.Request.Path.Value!;
            var query = ctx.Request.Query;
            JsonElement body = default;
            if (HttpMethods.IsPost(ctx.Request.Method)) body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
            string? Text(string key) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty(key, out var x) ? x.ToString() : null;
            decimal Number(string key) => decimal.Parse(Text(key)!, CultureInfo.InvariantCulture);
            async Task Reply(object value, int status = 200) { ctx.Response.StatusCode = status; await ctx.Response.WriteAsJsonAsync(value); }

            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ApiId}:{ApiKey}"));
            if (ctx.Request.Headers.Authorization != expected) { await Reply(new { ResponseCode = "4101", Message = "Authorization denied" }, 401); return; }

            var parts = path.Trim('/').Split('/');
            switch (parts)
            {
                case ["merchantaccount", "merchants", Collection, "receive", "mobilemoney"]:
                {
                    Interlocked.Increment(ref _collections);
                    if (await Mode(CollectionMode, collection: true)) return;
                    var amount = Number("Amount");
                    var charges = decimal.Round(amount * 0.01m, 2);
                    var item = Record("collection", Text("ClientReference")!, Text("PrimaryCallbackUrl")!, amount, "Unpaid", null);
                    if (item is null) { await Reply(new { ResponseCode = "4000", Message = "Duplicate client reference" }, 400); return; }
                    if (CollectionMode == "lost") { ctx.Abort(); return; }
                    if (CollectionMode == "slow") await Task.Delay(750);
                    await Reply(new { Message = "Transaction pending. Expect callback request for final state", ResponseCode = "0001",
                        Data = new { item.TransactionId, Description = Text("Description"), ClientReference = item.Reference, Amount = amount,
                            Charges = charges, AmountAfterCharges = amount, AmountCharged = amount + charges, DeliveryFee = 0.0 } });
                    return;
                }
                case ["commissionservices", Disbursement, var service] when HttpMethods.IsGet(ctx.Request.Method):
                    Interlocked.Increment(ref _lookups);
                    await Reply(Lookup(service, query["destination"].ToString(), query["mobile"].ToString())); return;
                case ["commissionservices", Disbursement, var service]:
                {
                    Interlocked.Increment(ref _purchases);
                    if (await Mode(PurchaseMode, collection: false)) return;
                    var amount = Number("Amount");
                    if (FloatBalance < amount) { await Reply(new { ResponseCode = "4075", Message = "Insufficient prepaid balance." }); return; }
                    var item = Record("purchase", Text("ClientReference")!, Text("CallbackUrl")!, amount, "Unpaid", service);
                    if (item is null) { await Reply(new { ResponseCode = "4000", Message = "Duplicate client reference" }, 400); return; }
                    FloatBalance -= amount;
                    if (PurchaseMode == "lost") { ctx.Abort(); return; }
                    if (PurchaseMode == "slow") await Task.Delay(750);
                    await Reply(new { ResponseCode = "0001", Message = "Transaction pending. Expect callback request for final state",
                        Data = new { ClientReference = item.Reference, Amount = amount, item.TransactionId, Meta = new { Commission = "0.0198" } } });
                    return;
                }
                case ["transactions", Collection, "status"]:
                {
                    Interlocked.Increment(ref _status);
                    if (!Transactions.TryGetValue(query["clientReference"].ToString(), out var item))
                    { await Reply(new { message = "Transaction not found", responseCode = "3000", data = (object?)null }, 404); return; }
                    await Reply(new { message = "Successful", responseCode = "0000", data = new { date = DateTimeOffset.UtcNow, status = item.Status,
                        transactionId = item.TransactionId, externalTransactionId = "EXT-" + item.Reference, paymentMethod = item.Kind == "collection" ? "mobilemoney" : "cash",
                        clientReference = item.Reference, currencyCode = (string?)null, amount = item.Amount, charges = 0.0, amountAfterCharges = item.Amount,
                        isFulfilled = item.Kind == "purchase" ? item.Status == "Paid" : (bool?)null } });
                    return;
                }
                case ["refund", Collection, "order", var orderId]:
                    Interlocked.Increment(ref _refunds);
                    if (RefundMode == "ineligible") { await Reply(new { message = $"Order with ID {orderId} is not eligible for a refund.", responseCode = "4509", data = (object?)null }, 404); return; }
                    RefundRequests.Enqueue((orderId, Text("callbackUrl")!));
                    await Reply(new { message = "Transaction pending. Expect callback request for final state.", responseCode = "0001", data = new { orderId } }, 202);
                    return;
                case ["api", "inter-transfers", Collection] when HttpMethods.IsGet(ctx.Request.Method):
                    await Reply(new { responseCode = "0000", message = "Success", data = new { amount = CollectionBalance } }); return;
                case ["api", "inter-transfers", "prepaid", Disbursement]:
                    await Reply(new { responseCode = "0000", message = "Success", data = new { amount = FloatBalance } }); return;
                case ["api", "inter-transfers", Collection]:
                {
                    Interlocked.Increment(ref _transfers);
                    if (Text("DestinationAccountNumber") != Disbursement) { await Reply(new { responseCode = "4000", message = "Disbursement Account Number is invalid" }, 400); return; }
                    var item = Record("transfer", Text("ClientReference")!, Text("PrimaryCallbackUrl")!, Number("Amount"), "pending", null);
                    await Reply(new { responseCode = "0001", message = "Pending", data = new { clientReference = item!.Reference, amount = item.Amount, charges = 0, meta = (object?)null, recipientName = "grid" } });
                    return;
                }
                case ["api", "inter-transfers", "status", Collection]:
                {
                    if (!Transactions.TryGetValue(query["clientReference"].ToString(), out var item)) { await Reply(new { message = "not found", code = "404" }, 404); return; }
                    await Reply(new { message = "success", code = "200", data = new { id = item.TransactionId, clientReference = item.Reference,
                        transferAmount = item.Amount, status = item.Status, failureReason = (string?)null } });
                    return;
                }
                default:
                    await Reply(new { ResponseCode = "4101", Message = "Could not find prepaid account or endpoint" }, 404); return;
            }

            SimTransaction? Record(string kind, string reference, string callback, decimal amount, string status, string? service)
            {
                var item = new SimTransaction { Kind = kind, Reference = reference, CallbackUrl = callback, Amount = amount, Status = status, Body = body.Clone(), ServiceId = service };
                return Transactions.TryAdd(reference, item) ? item : null;
            }

            async Task<bool> Mode(string mode, bool collection)
            {
                switch (mode)
                {
                    case "http-error": await Reply(new { }, 503); return true;
                    case "forbidden": ctx.Response.StatusCode = 403; return true;
                    case "reject": await Reply(collection ? new { ResponseCode = "4000", Message = "Validation errors" } : new { ResponseCode = "2001", Message = "The receiving user is not registered" }); return true;
                    case "ambiguous": await Reply(new { ResponseCode = "0005", Message = "There was an HTTP failure/exception." }); return true;
                    default: return false;
                }
            }
        }

        private object Lookup(string service, string destination, string mobile)
        {
            if (destination.StartsWith("9999")) return new { ResponseCode = "2001", Message = "Invalid account number", Label = "", Data = Array.Empty<object>() };
            object Row(string display, string value, decimal amount = 0) => new { Display = display, Value = value, Amount = amount };
            object[] rows = service switch
            {
                EcgService => Meters.Select(m => Row($" {m.Name} ({m.Meter})", " " + m.Meter, m.Amount)).ToArray(),
                WaterService => [Row("name", "AMAKYE FREMPONG"), Row("amountDue", "371.89", 371.89m), Row("sessionId", "session-for-" + destination + "-" + mobile)],
                MtnDataService => [Row("17.79MB", "data_bundle_1", 0.5m), Row("349.24MB", "data_bundle_3", 3m), Row("1.19GB", "flexi_data_bundle", 20m), Row("2.38GB", "flexi_data_bundle", 40m)],
                BroadbandService => [Row("name", "BOATENG PHILIP"), Row("amountDue", "0.00"), Row("account", "P0444101")],
                _ => [Row("name", "John Barnes"), Row("amountDue", "-201.00"), Row("account", destination)],
            };
            return new { ResponseCode = "0000", Message = "Successful", Label = "Successful", Data = rows };
        }
    }
}

public sealed class PaymentApiFactory(HubtelSimulator simulator, bool docs = false, List<Activity>? spans = null, List<LogRecord>? logs = null) : ApiFactory
{
    public const string FinancePhone = "233209990000";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Docs:Enabled", docs.ToString());
        builder.UseSetting("Payments:Enabled", "true");
        if (spans is not null && logs is not null)
        {
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:4318");
            builder.ConfigureServices(services =>
            {
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(spans));
                services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(logs));
            });
        }
        builder.UseSetting("Payments:RunWorker", "false");
        builder.UseSetting("Payments:CallbackBaseUrl", "https://grid-callback.test");
        // Tests drive outcomes through status checks, so don't wait for an overdue callback.
        builder.UseSetting("Payments:StatusCheckDelaySeconds", "0");
        builder.UseSetting("Payments:Float:LowWatermark", "100");
        builder.UseSetting("Payments:Float:Target", "1000");
        builder.UseSetting("Payments:Float:CollectionReserve", "0");
        builder.UseSetting("Payments:FinanceOperators:0", FinancePhone);
        builder.UseSetting("Payments:Hubtel:ApiId", HubtelSimulator.ApiId);
        builder.UseSetting("Payments:Hubtel:ApiKey", HubtelSimulator.ApiKey);
        builder.UseSetting("Payments:Hubtel:CollectionAccountNumber", HubtelSimulator.Collection);
        builder.UseSetting("Payments:Hubtel:DisbursementAccountNumber", HubtelSimulator.Disbursement);
        builder.UseSetting("Payments:Hubtel:CallbackSecret", "test-callback-secret-at-least-32-characters");
        builder.UseSetting("Payments:Hubtel:FallbackReceiptEmail", "receipts@grid.test");
        foreach (var key in new[] { "ReceiveMoneyBaseUrl", "CommissionBaseUrl", "StatusBaseUrl", "RefundBaseUrl", "TransferBaseUrl" })
            builder.UseSetting("Payments:Hubtel:" + key, simulator.Url);
    }
}
