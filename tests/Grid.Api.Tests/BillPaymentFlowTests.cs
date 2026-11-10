using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grid.Api.Features.BillPayments;
using Grid.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Grid.Api.Tests;

public sealed class BillPaymentFlowTests
{
    private const string Payer = "233551234567";

    // ---- The happy path ----

    [Fact]
    public async Task ECG_order_prompts_the_wallet_then_pays_the_bill_and_gives_a_receipt()
    {
        await using var rig = await Rig.CreateAsync();
        var lookup = await Json.ExpectSuccessAsync(await rig.LookupAsync(new { productCode = "EcgPrepaid" }));
        Assert.Equal("P191177631", lookup.GetProperty("accountNumber").GetString());
        Assert.Equal("TEST CUSTOMER", lookup.GetProperty("customerName").GetString());

        var placed = await Json.ExpectSuccessAsync(await rig.PlaceAsync());
        var id = placed.GetProperty("id").GetGuid();
        Assert.Equal("AwaitingPayment", placed.GetProperty("status").GetString());
        Assert.Equal("ApproveOnPhone", placed.GetProperty("nextAction").GetProperty("kind").GetString());
        Assert.Equal("101.00", placed.GetProperty("payment").GetProperty("amountCharged").GetProperty("amount").GetString());

        var order = await rig.OrderRowAsync(id);
        var prompt = rig.Simulator.Snapshot.Collection.Body;
        Assert.Equal(Payer, prompt.GetProperty("CustomerMsisdn").GetString());
        Assert.Equal("mtn-gh", prompt.GetProperty("Channel").GetString());
        Assert.Equal(100m, prompt.GetProperty("Amount").GetDecimal());
        Assert.Equal(order.ClientReference, prompt.GetProperty("ClientReference").GetString());

        rig.Simulator.SetStatus(order.ClientReference, "Paid");
        await rig.PumpAsync(); // Confirms the payment, then sends the bill in the same pass.
        Assert.Equal(TxnState.Succeeded, (await rig.OrderRowAsync(id)).PaymentState);
        Assert.Equal("Processing", (await rig.OrderAsync(id)).GetProperty("status").GetString());
        var bill = await rig.BillRowAsync(id);
        Assert.Equal(TxnState.Pending, bill.State);
        Assert.Equal(0.0198m, bill.Commission);
        var paid = rig.Simulator.Snapshot.Purchase;
        Assert.Equal(HubtelSimulator.EcgService, paid.ServiceId);
        Assert.Equal(Payer, paid.Body.GetProperty("Destination").GetString());
        Assert.Equal("P191177631", paid.Body.GetProperty("Extradata").GetProperty("bundle").GetString());

        rig.Simulator.SetStatus(bill.ClientReference, "Paid");
        await rig.PumpAsync();
        var done = await rig.OrderAsync(id);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal("Succeeded", done.GetProperty("bill").GetProperty("state").GetString());
        var receipt = await Json.ExpectSuccessAsync(await rig.Client.GetAsync($"/api/v1/orders/{id}/receipt"));
        Assert.Equal("RemoteMeterCredit", receipt.GetProperty("bill").GetProperty("resultKind").GetString());
        Assert.Equal("EXT-" + bill.ClientReference, receipt.GetProperty("bill").GetProperty("externalReference").GetString());
        Assert.Equal((1, 1), (rig.Simulator.Snapshot.CollectionCalls, rig.Simulator.Snapshot.PurchaseCalls));
    }

    [Fact]
    public async Task Callbacks_alone_settle_the_order_and_repeats_are_harmless()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Config.StatusCheckDelaySeconds = 300;
        var id = await rig.PlaceIdAsync();
        var reference = (await rig.OrderRowAsync(id)).ClientReference;
        var path = rig.CallbackPath(reference);

        await Json.ExpectErrorAsync(await rig.Client.PostAsJsonAsync(path[..^64] + new string('0', 64), Rig.Callback(reference, 100m)), 403, "payments.callback_forbidden");
        await Json.ExpectErrorAsync(await rig.Client.PostAsJsonAsync(path, Rig.Callback("C-someone-else", 100m)), 400, "payments.callback_mismatch");
        for (var i = 0; i < 3; i++) await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(path, Rig.Callback(reference, 100m)));
        Assert.Equal("Processing", (await rig.OrderAsync(id)).GetProperty("status").GetString());

        await rig.RunDueAsync(); // Sends the bill; its status check isn't due for five minutes.
        var bill = await rig.BillRowAsync(id);
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(rig.CallbackPath(bill.ClientReference), Rig.Callback(bill.ClientReference, 100m)));
        Assert.Equal("Completed", (await rig.OrderAsync(id)).GetProperty("status").GetString());
        Assert.Equal(0, rig.Simulator.Snapshot.StatusCalls);
        Assert.Equal(1, rig.Simulator.Snapshot.PurchaseCalls);
    }

    [Fact]
    public async Task Callbacks_from_unlisted_addresses_are_refused_when_enforced()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PlaceIdAsync();
        var reference = (await rig.OrderRowAsync(id)).ClientReference;
        rig.Config.Hubtel.EnforceCallbackIp = true;
        await Json.ExpectErrorAsync(await rig.Client.PostAsJsonAsync(rig.CallbackPath(reference), Rig.Callback(reference, 100m)), 403, "payments.callback_forbidden");
        Assert.Equal(TxnState.Pending, (await rig.OrderRowAsync(id)).PaymentState);
    }

    // ---- Telemetry ----

    [Fact]
    public async Task Telemetry_keeps_signatures_and_phone_numbers_out_and_skips_health_checks()
    {
        var spans = new List<Activity>();
        var logs = new List<LogRecord>();
        await using var rig = await Rig.CreateAsync(spans: spans, logs: logs);
        await rig.LookupAsync(new { productCode = "EcgPrepaid", accountNumber = "P20314039" });
        var id = await rig.PlaceIdAsync();
        var reference = (await rig.OrderRowAsync(id)).ClientReference;
        var path = rig.CallbackPath(reference);
        var signature = path[(path.LastIndexOf('/') + 1)..];
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(path, Rig.Callback(reference, 100m, "2001")));
        await rig.Client.GetAsync("/health");

        Activity? Callback() { lock (spans) return spans.Find(s => s.Kind == ActivityKind.Server && s.GetTagItem("http.route")?.ToString()?.Contains("callbacks") == true); }
        for (var i = 0; i < 50 && Callback() is null; i++) await Task.Delay(100);
        Assert.Equal("/api/v1/hubtel/callbacks", Callback()!.GetTagItem("url.path"));

        List<Activity> exported;
        lock (spans) exported = [.. spans];
        Assert.Contains(exported, s => s.Kind == ActivityKind.Client); // Hubtel calls are traced.
        var everyTag = string.Join(' ', exported.SelectMany(s => s.Tags).Select(t => t.Value));
        Assert.DoesNotContain(signature, everyTag);
        Assert.DoesNotContain("233551234567", everyTag);
        Assert.DoesNotContain(exported, s => s.GetTagItem("url.path")?.ToString() == "/health");
        Assert.DoesNotContain(exported, s => s.Source.Name.Contains("Npgsql") || s.Source.Name.Contains("EntityFramework"));

        List<LogRecord> records;
        lock (logs) records = [.. logs];
        var failure = Assert.Single(records, r => r.FormattedMessage?.Contains("PaymentFailed") == true && r.FormattedMessage.Contains("hubtel.2001"));
        Assert.Equal(LogLevel.Warning, failure.LogLevel);
        Assert.DoesNotContain(records, r => r.FormattedMessage?.Contains(signature) == true);
        Assert.DoesNotContain(records, r => r.CategoryName == "Microsoft.EntityFrameworkCore.Database.Command" && r.LogLevel < LogLevel.Warning);
    }

    // ---- Payments that don't go through ----

    [Fact]
    public async Task Declined_payment_ends_the_order_and_a_new_order_can_be_placed()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PlaceIdAsync();
        var reference = (await rig.OrderRowAsync(id)).ClientReference;
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(rig.CallbackPath(reference), Rig.Callback(reference, 100m, "2001")));
        var failed = await rig.OrderAsync(id);
        Assert.Equal("PaymentFailed", failed.GetProperty("status").GetString());
        Assert.Equal("hubtel.2001", failed.GetProperty("payment").GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("nextAction").ValueKind);
        await rig.PumpAsync();
        Assert.Null(await rig.Factory.WithDbAsync(db => db.Bills.SingleOrDefaultAsync()));
        await rig.PlaceIdAsync(key: "second-try");
        Assert.Equal(2, rig.Simulator.Snapshot.CollectionCalls);
    }

    [Fact]
    public async Task Payment_still_unpaid_after_15_minutes_is_declined_and_a_late_approval_is_refunded()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PlaceIdAsync();
        await rig.PumpAsync();
        Assert.Equal(TxnState.Pending, (await rig.OrderRowAsync(id)).PaymentState);
        rig.Factory.Time.Advance(TimeSpan.FromMinutes(16)); await rig.PumpAsync();
        var order = await rig.OrderRowAsync(id);
        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
        Assert.Equal("hubtel.unpaid", order.PaymentError);

        // The customer approves after all, perhaps after paying again on a new order: that money goes back.
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(rig.CallbackPath(order.ClientReference), Rig.Callback(order.ClientReference, 100m)));
        await rig.PumpAsync();
        Assert.Equal("LatePayment", (await rig.RefundRowAsync(id)).Reason);
        Assert.Single(rig.Simulator.Snapshot.Refunds);
        Assert.Null(await rig.Factory.WithDbAsync(db => db.Bills.SingleOrDefaultAsync()));
    }

    [Theory]
    [InlineData("reject", TxnState.Failed, "PaymentFailed")]
    [InlineData("forbidden", TxnState.Failed, "PaymentFailed")]
    [InlineData("http-error", TxnState.Unknown, "AwaitingPayment")]
    public async Task Rejections_end_the_order_but_transport_errors_stay_unknown(string mode, TxnState state, string status)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Simulator.Configure(collection: mode);
        var placed = await Json.ExpectSuccessAsync(await rig.PlaceAsync());
        Assert.Equal(status, placed.GetProperty("status").GetString());
        Assert.Equal(state, (await rig.OrderRowAsync(placed.GetProperty("id").GetGuid())).PaymentState);
    }

    [Fact]
    public async Task Payer_is_the_signed_in_number_and_its_network_is_the_default_channel()
    {
        await using var rig = await Rig.CreateAsync();
        var channels = await Json.ExpectSuccessAsync(await rig.Client.GetAsync("/api/v1/payment-channels"));
        Assert.Equal("mtn-gh", channels.GetProperty("suggested").GetString());
        Assert.Equal(Payer, channels.GetProperty("payerPhone").GetString());
        await Json.ExpectErrorAsync(await rig.PlaceAsync(new { productCode = "EcgPrepaid", amount = Rig.Ghs("100.00"), channel = "card" }), 400, "validation.failed");
        var placed = await Json.ExpectSuccessAsync(await rig.PlaceAsync(new { productCode = "EcgPrepaid", amount = Rig.Ghs("100.00"), channel = "vodafone-gh" }, "telecel"));
        Assert.Equal("vodafone-gh", placed.GetProperty("payment").GetProperty("channel").GetString());
        Assert.Equal(Payer, rig.Simulator.Snapshot.Collection.Body.GetProperty("CustomerMsisdn").GetString());
    }

    [Fact]
    public async Task Payments_switched_off_refuse_new_orders()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Config.Enabled = false;
        await Json.ExpectErrorAsync(await rig.PlaceAsync(), 503, "payments.disabled");
        Assert.Equal(0, rig.Simulator.Snapshot.CollectionCalls);
    }

    // ---- Unclear answers are never sent twice ----

    [Fact]
    public async Task Lost_prompt_response_is_resolved_by_status_without_a_second_prompt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Simulator.Configure(collection: "lost");
        var id = await rig.PlaceIdAsync();
        var order = await rig.OrderRowAsync(id);
        Assert.Equal(TxnState.Unknown, order.PaymentState);
        // Even a row left in Submitting (as after a crash) is only ever status-checked.
        await rig.Factory.WithDbAsync(db => db.Orders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentState, TxnState.Submitting)));
        await rig.PumpAsync();
        rig.Simulator.SetStatus(order.ClientReference, "Paid");
        await rig.PumpAsync();
        Assert.Equal(TxnState.Succeeded, (await rig.OrderRowAsync(id)).PaymentState);
        Assert.Equal(1, rig.Simulator.Snapshot.CollectionCalls);
    }

    [Fact]
    public async Task Lost_bill_response_is_never_resent_or_refunded_while_unknown()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Simulator.Configure(purchase: "lost");
        var id = await rig.PaidOrderAsync();
        var bill = await rig.BillRowAsync(id);
        Assert.Equal(TxnState.Unknown, bill.State);
        await rig.PumpAsync(); await rig.PumpAsync();
        Assert.Null(await rig.Factory.WithDbAsync(db => db.Refunds.SingleOrDefaultAsync()));
        rig.Simulator.SetStatus(bill.ClientReference, "Paid"); await rig.PumpAsync();
        Assert.Equal(OrderStatus.Completed, (await rig.OrderRowAsync(id)).Status);
        Assert.Equal(1, rig.Simulator.Snapshot.PurchaseCalls);
    }

    [Fact]
    public async Task Hubtel_0005_stays_unknown_and_goes_to_review_after_a_day()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Simulator.Configure(purchase: "ambiguous");
        var id = await rig.PaidOrderAsync();
        Assert.Equal(TxnState.Unknown, (await rig.BillRowAsync(id)).State);
        await rig.PumpAsync(); await rig.PumpAsync();
        rig.Factory.Time.Advance(TimeSpan.FromHours(25)); await rig.PumpAsync();
        var order = await rig.OrderRowAsync(id);
        Assert.True(order.NeedsReview);
        Assert.Equal("unresolved_after_a_day", order.ReviewReason);
        Assert.Equal(1, rig.Simulator.Snapshot.PurchaseCalls);
        Assert.Null(await rig.Factory.WithDbAsync(db => db.Refunds.SingleOrDefaultAsync()));
    }

    [Fact]
    public async Task Concurrent_workers_send_a_bill_only_once()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PlaceIdAsync();
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(rig.CallbackPath((await rig.OrderRowAsync(id)).ClientReference),
            Rig.Callback((await rig.OrderRowAsync(id)).ClientReference, 100m)));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = rig.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BillPaymentProcessor>().RunDueAsync();
        }));
        Assert.Equal(1, rig.Simulator.Snapshot.PurchaseCalls);
    }

    [Fact]
    public async Task Same_key_makes_one_order_and_one_prompt_and_a_different_body_is_refused()
    {
        await using var rig = await Rig.CreateAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => rig.PlaceAsync(key: "same")));
        var ids = new List<Guid>();
        foreach (var response in responses) ids.Add((await Json.ExpectSuccessAsync(response)).GetProperty("id").GetGuid());
        Assert.Single(ids.Distinct());
        Assert.Equal(1, rig.Simulator.Snapshot.CollectionCalls);
        await Json.ExpectErrorAsync(await rig.PlaceAsync(new { productCode = "EcgPrepaid", amount = Rig.Ghs("50.00") }, "same"), 409, "payments.idempotency_conflict");
        await Json.ExpectErrorAsync(await rig.PlaceAsync(key: ""), 400, "payments.idempotency_required");
    }

    // ---- Refunds ----

    [Fact]
    public async Task Failed_bill_is_refunded_through_Hubtel_automatically()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PaidOrderAsync();
        var bill = await rig.BillRowAsync(id);
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(rig.CallbackPath(bill.ClientReference), Rig.Callback(bill.ClientReference, 100m, "2001")));
        Assert.Equal("Refunding", (await rig.OrderAsync(id)).GetProperty("status").GetString());
        await rig.PumpAsync();
        var order = await rig.OrderRowAsync(id);
        var request = Assert.Single(rig.Simulator.Snapshot.Refunds);
        Assert.Equal(order.HubtelOrderId, request.OrderId);
        Assert.Equal(TxnState.Pending, (await rig.RefundRowAsync(id)).State);

        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(new Uri(request.CallbackUrl).AbsolutePath, new
        {
            responseCode = "0000", message = "Successful",
            data = new { amount = 100m, charges = 1m, orderId = order.HubtelOrderId, externalTransactionId = "0000002673867446" },
        }));
        var view = await rig.OrderAsync(id);
        Assert.Equal("Refunded", view.GetProperty("status").GetString());
        Assert.Equal("Completed", view.GetProperty("refund").GetProperty("status").GetString());
        Assert.Equal("hubtel:0000002673867446", (await rig.RefundRowAsync(id)).ExternalReference);

        var finance = await rig.FinanceAsync();
        var summary = await Json.ExpectSuccessAsync(await finance.GetAsync("/api/v1/ops/finance/summary"));
        Assert.Equal("100.00", summary.GetProperty("refunded").GetProperty("amount").GetString());
        Assert.Equal("0.00", summary.GetProperty("delivered").GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Refund_without_a_callback_completes_when_the_payment_reads_Refunded()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Simulator.Configure(purchase: "reject");
        var id = await rig.PaidOrderAsync();
        await rig.PumpAsync(); // Sends the refund.
        await rig.PumpAsync(); // Still "Paid" at Hubtel.
        Assert.Equal(TxnState.Pending, (await rig.RefundRowAsync(id)).State);
        rig.Simulator.SetStatus((await rig.OrderRowAsync(id)).ClientReference, "Refunded");
        await rig.PumpAsync();
        var refund = await rig.RefundRowAsync(id);
        Assert.Equal(TxnState.Succeeded, refund.State);
        Assert.StartsWith("hubtel-order:", refund.ExternalReference);
        Assert.Equal(OrderStatus.Refunded, (await rig.OrderRowAsync(id)).Status);
    }

    [Fact]
    public async Task Refund_Hubtel_refuses_goes_to_finance_who_can_retry_it()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Simulator.Configure(purchase: "reject", refund: "ineligible");
        var id = await rig.PaidOrderAsync();
        await rig.PumpAsync();
        var refund = await rig.RefundRowAsync(id);
        Assert.Equal(TxnState.Failed, refund.State);
        Assert.Equal("hubtel.4509", refund.ErrorCode);
        Assert.Equal("NeedsFinance", (await rig.OrderAsync(id)).GetProperty("refund").GetProperty("status").GetString());

        await Json.ExpectErrorAsync(await rig.Client.PostAsJsonAsync($"/api/v1/ops/finance/refunds/{refund.Id}/retry", new { reason = "Try" }), 403, "finance.forbidden");
        var finance = await rig.FinanceAsync();
        rig.Simulator.Configure(refund: "accept");
        await Json.ExpectSuccessAsync(await finance.PostAsJsonAsync($"/api/v1/ops/finance/refunds/{refund.Id}/retry", new { reason = "Hubtel enabled refunds" }));
        await rig.PumpAsync();
        Assert.Equal(2, rig.Simulator.Snapshot.RefundCalls);
        Assert.Equal(TxnState.Pending, (await rig.RefundRowAsync(id)).State);
    }

    [Fact]
    public async Task Refund_below_Hubtel_minimum_is_completed_by_hand()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Config.MinAmount = 0.5m;
        rig.Simulator.Configure(purchase: "reject");
        var id = await rig.PaidOrderAsync("0.50");
        await rig.PumpAsync();
        var refund = await rig.RefundRowAsync(id);
        Assert.Equal("refund.below_minimum", refund.ErrorCode);
        Assert.Equal(0, rig.Simulator.Snapshot.RefundCalls);

        var finance = await rig.FinanceAsync();
        await Json.ExpectErrorAsync(await finance.PostAsJsonAsync($"/api/v1/ops/finance/refunds/{refund.Id}/retry", new { reason = "Try" }), 409, "finance.refund_not_eligible");
        var body = new { reason = "Sent by MoMo", externalReference = "momo-1", evidence = "Telecel Cash receipt 123" };
        await Json.ExpectSuccessAsync(await finance.PostAsJsonAsync($"/api/v1/ops/finance/refunds/{refund.Id}/complete", body));
        await Json.ExpectSuccessAsync(await finance.PostAsJsonAsync($"/api/v1/ops/finance/refunds/{refund.Id}/complete", body)); // Repeat is fine.
        await Json.ExpectErrorAsync(await finance.PostAsJsonAsync($"/api/v1/ops/finance/refunds/{refund.Id}/complete", body with { externalReference = "other" }),
            409, "finance.refund_already_completed");
        Assert.Equal("Refunded", (await rig.OrderAsync(id)).GetProperty("status").GetString());
        Assert.Equal(RefundMethod.Manual, (await rig.RefundRowAsync(id)).Method);
    }

    [Fact]
    public async Task Bill_success_after_its_failure_cancels_the_unsent_refund()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PaidOrderAsync();
        var bill = await rig.BillRowAsync(id);
        var path = rig.CallbackPath(bill.ClientReference);
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(path, Rig.Callback(bill.ClientReference, 100m, "2001")));
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(path, Rig.Callback(bill.ClientReference, 100m)));
        await rig.PumpAsync();
        Assert.Equal(0, rig.Simulator.Snapshot.RefundCalls);
        var order = await rig.OrderRowAsync(id);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal("bill_delivered_after_failure", order.ReviewReason);
        var refund = await rig.RefundRowAsync(id);
        Assert.Equal("refund.bill_delivered", refund.ErrorCode);

        var finance = await rig.FinanceAsync();
        await Json.ExpectErrorAsync(await finance.PostAsJsonAsync($"/api/v1/ops/finance/refunds/{refund.Id}/complete",
            new { reason = "x", externalReference = "y", evidence = "z" }), 409, "finance.refund_unresolved");
        await Json.ExpectSuccessAsync(await finance.PostAsJsonAsync($"/api/v1/ops/finance/orders/{id}/resolve-review", new { reason = "Delivered; ECG confirmed" }));
        Assert.False((await rig.OrderRowAsync(id)).NeedsReview);
    }

    [Fact]
    public async Task Paid_amount_that_differs_is_refunded_and_never_paid_out()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PlaceIdAsync();
        rig.Simulator.SetStatus((await rig.OrderRowAsync(id)).ClientReference, "Paid", 99m);
        await rig.PumpAsync();
        var order = await rig.OrderRowAsync(id);
        Assert.Equal("payment_amount_mismatch", order.ReviewReason);
        Assert.Equal(99m, (await rig.RefundRowAsync(id)).Amount);
        Assert.Null(await rig.Factory.WithDbAsync(db => db.Bills.SingleOrDefaultAsync()));
    }

    [Fact]
    public async Task Paid_amount_that_includes_hubtels_charge_still_pays_the_bill()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PlaceIdAsync();
        var reference = (await rig.OrderRowAsync(id)).ClientReference;
        // Customer bears the charge: Hubtel reports 100 + its 1.00 charge as the amount.
        await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync(rig.CallbackPath(reference), Rig.Callback(reference, 101m)));
        await rig.PumpAsync();
        var order = await rig.OrderRowAsync(id);
        Assert.False(order.NeedsReview);
        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.Equal(100m, (await rig.Factory.WithDbAsync(db => db.Bills.SingleAsync())).Amount);
        Assert.Null(await rig.Factory.WithDbAsync(db => db.Refunds.SingleOrDefaultAsync()));
    }

    // ---- Float ----

    [Fact]
    public async Task Low_float_sweeps_from_collections_and_the_same_bill_retries_with_a_new_reference()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Simulator.SetBalances(collection: 5000m, floatBalance: 20m);
        var id = await rig.PaidOrderAsync();
        var bill = await rig.BillRowAsync(id);
        Assert.Equal(TxnState.Created, bill.State);
        Assert.Equal("hubtel.4075", bill.ErrorCode);
        Assert.Equal("Processing", (await rig.OrderAsync(id)).GetProperty("status").GetString());

        var transfer = await rig.Factory.WithDbAsync(db => db.FloatTransfers.AsNoTracking().SingleAsync());
        Assert.Equal(980m, transfer.Amount);
        Assert.Equal(HubtelSimulator.Disbursement, rig.Simulator.Snapshot.Find(transfer.ClientReference).Body.GetProperty("DestinationAccountNumber").GetString());
        rig.Simulator.SetStatus(transfer.ClientReference, "success");
        await rig.PumpAsync();
        Assert.Equal(TxnState.Succeeded, (await rig.Factory.WithDbAsync(db => db.FloatTransfers.SingleAsync())).State);
        var retried = await rig.BillRowAsync(id);
        Assert.Equal(TxnState.Pending, retried.State);
        Assert.Equal(2, retried.Attempts);
        Assert.NotEqual(bill.ClientReference, retried.ClientReference);

        rig.Simulator.SetStatus(retried.ClientReference, "Paid"); await rig.PumpAsync();
        Assert.Equal(OrderStatus.Completed, (await rig.OrderRowAsync(id)).Status);
        var finance = await rig.FinanceAsync();
        var view = await Json.ExpectSuccessAsync(await finance.GetAsync("/api/v1/ops/finance/float"));
        Assert.Equal("900.00", view.GetProperty("floatBalance").GetProperty("amount").GetString());
        Assert.Equal("980.00", view.GetProperty("last").GetProperty("amount").GetProperty("amount").GetString());
    }

    // ---- Products ----

    [Fact]
    public async Task ECG_phone_with_several_meters_asks_which_and_unlinked_meters_are_unconfirmed()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Simulator.SetMeters(("THOMAS ANANE", "G131099826", -1.14m), ("ADEMAU LYDIA", "24911947992", 0m));
        var error = await Json.ExpectErrorAsync(await rig.LookupAsync(new { productCode = "EcgPrepaid" }), 409, "bills.meter_selection_required");
        Assert.Equal(2, error.GetProperty("detail").GetArrayLength());
        var chosen = await Json.ExpectSuccessAsync(await rig.LookupAsync(new { productCode = "EcgPrepaid", meterNumber = "g131099826" }));
        Assert.Equal("THOMAS ANANE", chosen.GetProperty("customerName").GetString());
        Assert.Equal("-1.14", chosen.GetProperty("amountDue").GetProperty("amount").GetString());
        var unlinked = await Json.ExpectSuccessAsync(await rig.LookupAsync(new { productCode = "EcgPostpaid", accountNumber = "733342761" }));
        Assert.False(unlinked.GetProperty("nameConfirmed").GetBoolean());
    }

    [Fact]
    public async Task Ghana_Water_sends_a_fresh_session_and_a_receipt_email()
    {
        await using var rig = await Rig.CreateAsync();
        var lookup = await Json.ExpectSuccessAsync(await rig.LookupAsync(new { productCode = "GhanaWater", accountNumber = "091019010006" }));
        Assert.Equal("371.89", lookup.GetProperty("amountDue").GetProperty("amount").GetString());
        Assert.DoesNotContain("session", lookup.ToString(), StringComparison.OrdinalIgnoreCase);
        var paid = await rig.BillSentForAsync(new { productCode = "GhanaWater", accountNumber = "091019010006", amount = Rig.Ghs("50.00") });
        Assert.Equal(HubtelSimulator.WaterService, paid.ServiceId);
        Assert.Equal(Payer, paid.Body.GetProperty("Destination").GetString());
        var extra = paid.Body.GetProperty("Extradata");
        Assert.Equal("091019010006", extra.GetProperty("bundle").GetString());
        Assert.Equal("session-for-091019010006-" + Payer, extra.GetProperty("SessionId").GetString());
        Assert.Equal("receipts@grid.test", extra.GetProperty("Email").GetString());
    }

    [Fact]
    public async Task Data_bundles_come_from_the_telco_and_are_bought_at_their_exact_price()
    {
        await using var rig = await Rig.CreateAsync();
        var lookup = await Json.ExpectSuccessAsync(await rig.LookupAsync(new { productCode = "MtnData", accountNumber = "0244000111" }));
        Assert.Equal(4, lookup.GetProperty("bundles").GetArrayLength());
        Assert.True(lookup.GetProperty("amountRule").GetProperty("fixed").GetBoolean());
        await Json.ExpectErrorAsync(await rig.PlaceAsync(new { productCode = "MtnData", accountNumber = "0244000111", amount = Rig.Ghs("40.00") }, "no-bundle"), 400, "validation.failed");
        await Json.ExpectErrorAsync(await rig.PlaceAsync(new { productCode = "MtnData", accountNumber = "0244000111", amount = Rig.Ghs("39.00"), bundle = "flexi_data_bundle" }, "wrong"),
            400, "validation.failed");
        var paid = await rig.BillSentForAsync(new { productCode = "MtnData", accountNumber = "0244000111", amount = Rig.Ghs("40.00"), bundle = "flexi_data_bundle" });
        Assert.Equal("233244000111", paid.Body.GetProperty("Destination").GetString());
        Assert.Equal(40m, paid.Body.GetProperty("Amount").GetDecimal());
        Assert.Equal("flexi_data_bundle", paid.Body.GetProperty("Extradata").GetProperty("bundle").GetString());
    }

    [Fact]
    public async Task Airtime_is_capped_at_GHS_100_and_sent_to_the_number()
    {
        await using var rig = await Rig.CreateAsync();
        var lookup = await Json.ExpectSuccessAsync(await rig.LookupAsync(new { productCode = "MtnAirtime", accountNumber = "0244000111" }));
        Assert.Equal("100.00", lookup.GetProperty("amountRule").GetProperty("max").GetProperty("amount").GetString());
        await Json.ExpectErrorAsync(await rig.PlaceAsync(new { productCode = "MtnAirtime", accountNumber = "0244000111", amount = Rig.Ghs("100.01") }), 400, "validation.failed");
        var paid = await rig.BillSentForAsync(new { productCode = "MtnAirtime", accountNumber = "0244000111", amount = Rig.Ghs("5.00") });
        Assert.Equal(HubtelSimulator.MtnAirtimeService, paid.ServiceId);
        Assert.Equal("233244000111", paid.Body.GetProperty("Destination").GetString());
        Assert.False(paid.Body.TryGetProperty("Extradata", out _));
    }

    [Theory]
    [InlineData("Dstv", "7029864396", HubtelSimulator.DstvService, null)]
    [InlineData("TelecelBroadband", "0302450262", HubtelSimulator.BroadbandService, "P0444101")]
    public async Task Account_products_look_up_the_holder_and_pay_the_account(string product, string account, string service, string? bundle)
    {
        await using var rig = await Rig.CreateAsync();
        Assert.True((await Json.ExpectSuccessAsync(await rig.LookupAsync(new { productCode = product, accountNumber = account }))).GetProperty("nameConfirmed").GetBoolean());
        var paid = await rig.BillSentForAsync(new { productCode = product, accountNumber = account, amount = Rig.Ghs("50.00") });
        Assert.Equal(service, paid.ServiceId);
        Assert.Equal(account, paid.Body.GetProperty("Destination").GetString());
        if (bundle is null) Assert.False(paid.Body.TryGetProperty("Extradata", out _));
        else Assert.Equal(bundle, paid.Body.GetProperty("Extradata").GetProperty("bundle").GetString());
    }

    [Fact]
    public async Task Unknown_accounts_products_and_paused_products_are_refused_before_any_money_moves()
    {
        await using var rig = await Rig.CreateAsync();
        await Json.ExpectErrorAsync(await rig.LookupAsync(new { productCode = "Dstv", accountNumber = "9999999999" }), 404, "bills.account_not_found");
        await Json.ExpectErrorAsync(await rig.LookupAsync(new { productCode = "Netflix" }), 400, "validation.failed");
        rig.Config.DisabledProducts.Add("Gotv");
        await Json.ExpectErrorAsync(await rig.PlaceAsync(new { productCode = "Gotv", accountNumber = "7032371505", amount = Rig.Ghs("10.00") }), 503, "bills.product_paused");
        var products = await Json.ExpectSuccessAsync(await rig.Client.GetAsync("/api/v1/bill-products"));
        Assert.Equal(13, products.GetArrayLength());
        Assert.False(products.EnumerateArray().Single(p => p.GetProperty("code").GetString() == "Gotv").GetProperty("enabled").GetBoolean());
        Assert.Equal(0, rig.Simulator.Snapshot.CollectionCalls);
    }

    // ---- Saved accounts, finance, boundaries ----

    [Fact]
    public async Task Saved_accounts_must_match_the_bill_and_show_their_last_payment()
    {
        await using var rig = await Rig.CreateAsync();
        var saved = await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync("/api/v1/utility-accounts", new { type = "Ecg", accountNumber = "p191177631", nickname = "Home" }));
        Assert.Equal("Prepaid", saved.GetProperty("ecgKind").GetString());
        var other = await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync("/api/v1/utility-accounts", new { type = "Dstv", accountNumber = "7029864396", nickname = "TV" }));
        await Json.ExpectErrorAsync(await rig.PlaceAsync(new { productCode = "EcgPrepaid", amount = Rig.Ghs("100.00"), savedAccountId = other.GetProperty("id").GetGuid() }, "mismatch"),
            409, "bills.saved_account_mismatch");

        var id = (await Json.ExpectSuccessAsync(await rig.PlaceAsync(new { productCode = "EcgPrepaid", amount = Rig.Ghs("100.00"), savedAccountId = saved.GetProperty("id").GetGuid() }, "match")))
            .GetProperty("id").GetGuid();
        rig.Simulator.SetStatus((await rig.OrderRowAsync(id)).ClientReference, "Paid"); await rig.PumpAsync();
        rig.Simulator.SetStatus((await rig.BillRowAsync(id)).ClientReference, "Paid"); await rig.PumpAsync();
        var after = await Json.ExpectSuccessAsync(await rig.Client.GetAsync($"/api/v1/utility-accounts/{saved.GetProperty("id").GetGuid()}"));
        Assert.Equal(100m, after.GetProperty("lastAmount").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("lastPaidAt").ValueKind);

        var airtime = await Json.ExpectSuccessAsync(await rig.Client.PostAsJsonAsync("/api/v1/utility-accounts", new { type = "Airtime", accountNumber = "0201234567", nickname = "Mum" }));
        Assert.Equal("Telecel", airtime.GetProperty("network").GetString());
        await Json.ExpectErrorAsync(await rig.Client.PostAsJsonAsync("/api/v1/utility-accounts", new { type = "Dstv", accountNumber = "7029864396", nickname = "TV2", network = "Mtn" }),
            400, "validation.failed");
    }

    [Fact]
    public async Task Finance_sees_the_whole_story_of_an_order()
    {
        await using var rig = await Rig.CreateAsync();
        var id = await rig.PaidOrderAsync();
        await Json.ExpectErrorAsync(await rig.Client.GetAsync($"/api/v1/ops/finance/orders/{id}"), 403, "finance.forbidden");
        var finance = await rig.FinanceAsync();
        var detail = await Json.ExpectSuccessAsync(await finance.GetAsync($"/api/v1/ops/finance/orders/{id}"));
        Assert.Equal(Payer, detail.GetProperty("payerPhone").GetString());
        Assert.Equal(1, detail.GetProperty("bill").GetProperty("attempts").GetInt32());
        var kinds = detail.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Equal(["OrderPlaced", "PaymentSubmitted", "PaymentConfirmed", "BillSubmitting", "BillSubmitted"], kinds);
        var list = await Json.ExpectSuccessAsync(await finance.GetAsync("/api/v1/ops/finance/orders?status=Processing"));
        Assert.Single(list.EnumerateArray());
        await Json.ExpectErrorAsync(await finance.GetAsync($"/api/v1/orders/{id}/receipt"), 404, "orders.not_found");
    }

    [Fact]
    public async Task Orders_are_private_amounts_are_exact_and_history_pages()
    {
        await using var rig = await Rig.CreateAsync();
        var first = await rig.PlaceIdAsync(key: "a");
        var second = await rig.PlaceIdAsync(key: "b");
        var other = await rig.Factory.SignInAsync("0201234567", fullName: "Other Person");
        await Json.ExpectErrorAsync(await other.Client.GetAsync($"/api/v1/orders/{first}"), 404, "orders.not_found");
        Assert.Empty((await Json.ExpectSuccessAsync(await other.Client.GetAsync("/api/v1/orders"))).GetProperty("items").EnumerateArray());
        await Json.ExpectErrorAsync(await rig.PlaceAsync(new { productCode = "EcgPrepaid", amount = Rig.Ghs("1.001") }, "precise"), 400, "validation.failed");
        await Json.ExpectErrorAsync(await rig.Client.GetAsync($"/api/v1/orders/{first}/receipt"), 409, "payments.receipt_not_ready");
        var page = await Json.ExpectSuccessAsync(await rig.Client.GetAsync("/api/v1/orders?limit=1"));
        Assert.Equal(second, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        var next = await Json.ExpectSuccessAsync(await rig.Client.GetAsync($"/api/v1/orders?limit=1&before={page.GetProperty("nextCursor").GetGuid()}"));
        Assert.Equal(first, next.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Order_events_cannot_be_rewritten()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.PlaceIdAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await rig.Factory.WithDbAsync(async db =>
        { var e = await db.OrderEvents.FirstAsync(); e.Kind = "Rewritten"; return await db.SaveChangesAsync(); }));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(async () => await rig.Factory.WithDbAsync(db => db.OrderEvents.ExecuteDeleteAsync()));
    }

    [Fact]
    public async Task OpenAPI_describes_the_order_contract()
    {
        await using var rig = await Rig.CreateAsync(docs: true);
        var response = await rig.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await Json.ReadAsync(response);
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("OrderResponse", out _));
        Assert.True(document.GetProperty("paths").GetProperty("/api/v1/orders").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
    }

    private sealed class Rig : IAsyncDisposable
    {
        public HubtelSimulator Simulator { get; } = new();
        public PaymentApiFactory Factory { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        private HttpClient? _finance;
        public BillPaymentOptions Config => Factory.Services.GetRequiredService<IOptions<BillPaymentOptions>>().Value;

        public static async Task<Rig> CreateAsync(bool docs = false, List<Activity>? spans = null, List<LogRecord>? logs = null)
        {
            var rig = new Rig();
            await rig.Simulator.StartAsync();
            rig.Factory = new PaymentApiFactory(rig.Simulator, docs, spans, logs);
            await rig.Factory.InitializeAsync();
            rig.Client = (await rig.Factory.SignInAsync("0551234567", fullName: "Payment Tester")).Client;
            return rig;
        }

        public static object Ghs(string amount) => new { amount, currency = "GHS" };

        public Task<HttpResponseMessage> LookupAsync(object body) => Client.PostAsJsonAsync("/api/v1/bill-lookups", body);

        public async Task<HttpResponseMessage> PlaceAsync(object? body = null, string key = "order-1")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
            {
                Content = JsonContent.Create(body ?? new { productCode = "EcgPrepaid", amount = Ghs("100.00") }),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            return await Client.SendAsync(request);
        }

        public async Task<Guid> PlaceIdAsync(object? body = null, string key = "order-1") =>
            (await Json.ExpectSuccessAsync(await PlaceAsync(body, key))).GetProperty("id").GetGuid();

        /// <summary>Places an order, confirms its payment and lets the worker send the bill.</summary>
        public async Task<Guid> PaidOrderAsync(string amount = "100.00")
        {
            var id = await PlaceIdAsync(new { productCode = "EcgPrepaid", amount = Ghs(amount) });
            Simulator.SetStatus((await OrderRowAsync(id)).ClientReference, "Paid");
            await PumpAsync();
            return id;
        }

        /// <summary>What Hubtel received for the bill of a paid order built from <paramref name="body"/>.</summary>
        public async Task<HubtelSimulator.SimTransaction> BillSentForAsync(object body)
        {
            var id = await PlaceIdAsync(body);
            Simulator.SetStatus((await OrderRowAsync(id)).ClientReference, "Paid");
            await PumpAsync();
            return Simulator.Snapshot.Purchase;
        }

        public async Task<JsonElement> OrderAsync(Guid id) => await Json.ExpectSuccessAsync(await Client.GetAsync($"/api/v1/orders/{id}"));
        public Task<Order> OrderRowAsync(Guid id) => Factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == id));
        public Task<Bill> BillRowAsync(Guid order) => Factory.WithDbAsync(db => db.Bills.AsNoTracking().SingleAsync(b => b.OrderId == order));
        public Task<Refund> RefundRowAsync(Guid order) => Factory.WithDbAsync(db => db.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == order));

        /// <summary>Makes every scheduled row due now, then runs the worker once.</summary>
        public async Task PumpAsync()
        {
            var now = Factory.Time.GetUtcNow();
            await Factory.WithDbAsync(db => db.Orders.Where(x => x.NextCheckAt != null).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckAt, now)));
            await Factory.WithDbAsync(db => db.Bills.Where(x => x.NextCheckAt != null).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckAt, now)));
            await Factory.WithDbAsync(db => db.Refunds.Where(x => x.NextCheckAt != null).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckAt, now)));
            await Factory.WithDbAsync(db => db.FloatTransfers.Where(x => x.NextCheckAt != null).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckAt, now)));
            await RunDueAsync();
        }

        /// <summary>Runs the worker once without moving any schedule.</summary>
        public async Task RunDueAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BillPaymentProcessor>().RunDueAsync(100);
        }

        public string CallbackPath(string reference) => new Uri(Simulator.Snapshot.Find(reference).CallbackUrl).AbsolutePath;

        /// <summary>A callback shaped like Hubtel's receive-money and commission-services samples.</summary>
        public static object Callback(string reference, decimal amount, string code = "0000") => new
        {
            ResponseCode = code, Message = code == "0000" ? "success" : "failed",
            Data = new { Amount = amount, Charges = 1.0m, AmountAfterCharges = amount, Description = "Simulated", ClientReference = reference,
                TransactionId = "cb-" + reference, ExternalTransactionId = "ext-" + reference, AmountCharged = amount + 1m,
                Meta = new { Commission = "0.0198" } },
        };

        public async Task<HttpClient> FinanceAsync() =>
            _finance ??= (await Factory.SignInAsync(PaymentApiFactory.FinancePhone, fullName: "Finance Operator")).Client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); _finance?.Dispose();
            await ((IAsyncLifetime)Factory).DisposeAsync();
            await Simulator.DisposeAsync();
        }
    }
}
