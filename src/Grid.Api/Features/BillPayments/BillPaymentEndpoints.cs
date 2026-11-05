using System.Text.Json;
using Grid.Api.Common;

namespace Grid.Api.Features.BillPayments;

public static class BillPaymentEndpoints
{
    public const string RateLimitPolicy = "Payments";

    public static void MapBillPaymentEndpoints(this IEndpointRouteBuilder api)
    {
        var customer = api.MapGroup("").WithTags("Bill payments").RequireAuthorization();
        customer.AddEndpointFilter(NoStore);

        customer.MapGet("/bill-products", (OrderService orders) => Envelope.Ok(orders.Products()));
        customer.MapGet("/payment-channels", async (OrderService orders, CancellationToken ct) => Envelope.Ok(await orders.ChannelsAsync(ct)));
        customer.MapPost("/bill-lookups", async (LookupRequest body, OrderService orders, CancellationToken ct) =>
            Envelope.Ok(await orders.LookupAsync(body, ct))).RequireRateLimiting(RateLimitPolicy);
        customer.MapPost("/orders", async (CreateOrderRequest body, HttpContext http, OrderService orders, CancellationToken ct) =>
            Envelope.Created(await orders.CreateAsync(body, http.Request.Headers["Idempotency-Key"].ToString(), ct)))
            .RequireRateLimiting(RateLimitPolicy).Produces<ApiEnvelope<OrderResponse>>(201);
        customer.MapGet("/orders", async (OrderService orders, CancellationToken ct, Guid? before = null, int limit = 20) =>
            Envelope.Ok(await orders.HistoryAsync(before, limit, ct)));
        customer.MapGet("/orders/{id:guid}", async (Guid id, OrderService orders, CancellationToken ct) => Envelope.Ok(await orders.GetAsync(id, ct)));
        customer.MapGet("/orders/{id:guid}/receipt", async (Guid id, OrderService orders, CancellationToken ct) =>
            Envelope.Ok(await orders.ReceiptAsync(id, ct)));

        // Hubtel POSTs each operation's final state here. The signed URL is the credential.
        var callbacks = api.MapGroup("/hubtel/callbacks").WithTags("Hubtel callbacks").AllowAnonymous();
        foreach (var kind in Enum.GetValues<CallbackKind>())
        {
            callbacks.MapPost($"/{CallbackUrls.Segment(kind)}/{{id:guid}}/{{signature}}", async (Guid id, string signature, JsonElement body,
                HttpContext http, HubtelCallbackHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(kind, id, signature, http.Connection.RemoteIpAddress, body, ct);
                return Envelope.Empty();
            });
        }

        var finance = api.MapGroup("/ops/finance").WithTags("Finance").RequireAuthorization();
        finance.AddEndpointFilter(NoStore);
        finance.MapGet("/summary", async (FinanceService f, CancellationToken ct) => Envelope.Ok(await f.SummaryAsync(ct)));
        finance.MapGet("/float", async (FinanceService f, CancellationToken ct) => Envelope.Ok(await f.FloatAsync(ct)));
        finance.MapGet("/orders", async (FinanceService f, CancellationToken ct, OrderStatus? status = null, bool? needsReview = null, int limit = 50) =>
            Envelope.Ok(await f.OrdersAsync(status, needsReview, limit, ct)));
        finance.MapGet("/orders/{id:guid}", async (Guid id, FinanceService f, CancellationToken ct) => Envelope.Ok(await f.OrderAsync(id, ct)));
        finance.MapPost("/orders/{id:guid}/resolve-review", async (Guid id, ReasonRequest body, FinanceService f, CancellationToken ct) =>
            Envelope.Ok(await f.ResolveReviewAsync(id, body, ct)));
        finance.MapGet("/refunds", async (FinanceService f, CancellationToken ct, TxnState? state = null) => Envelope.Ok(await f.RefundsAsync(state, ct)));
        finance.MapPost("/refunds/{id:guid}/retry", async (Guid id, ReasonRequest body, FinanceService f, CancellationToken ct) =>
            Envelope.Ok(await f.RetryRefundAsync(id, body, ct)));
        finance.MapPost("/refunds/{id:guid}/complete", async (Guid id, CompleteRefundRequest body, FinanceService f, CancellationToken ct) =>
            Envelope.Ok(await f.CompleteRefundAsync(id, body, ct)));
    }

    private static async ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return await next(context);
    }
}
