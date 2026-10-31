using System.Net;
using System.Text;
using Grid.Api.Features.Kyc;
using Grid.Api.Integrations.Hubtel;
using Grid.Api.Integrations.Sms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Grid.Api.Tests;

/// <summary>Adapters replayed against the sample requests and responses in each provider's docs.</summary>
public class ProviderAdapterTests
{
    private static SmsOptions SmsConfig() => new()
    {
        SenderId = "GRID",
        Arkesel = new ArkeselSmsOptions { ApiKey = "ark-key", Sandbox = true },
        Hubtel = new HubtelSmsOptions { ClientId = "cid", ClientSecret = "secret" },
    };

    private static readonly SmsMessage Message = new("233558151870", "Your GRID code is 123456.", "Otp");

    // ---- Arkesel ----

    [Fact]
    public async Task Arkesel_sends_documented_request_and_reads_message_id()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"status":"success","data":[{"recipient":"233558151870","id":"9b752841-7ee7-4d40-b4fe-768bfb1da4f0"}]}""");
        var sender = new ArkeselSmsSender(new HttpClient(handler), Options.Create(SmsConfig()), NullLogger<ArkeselSmsSender>.Instance);

        var result = await sender.SendAsync(Message, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("9b752841-7ee7-4d40-b4fe-768bfb1da4f0", result.ProviderMessageId);
        Assert.Equal("https://sms.arkesel.com/api/v2/sms/send", handler.Request!.RequestUri!.ToString());
        Assert.Equal("ark-key", handler.Request.Headers.GetValues("api-key").Single());
        Assert.Contains("\"sender\":\"GRID\"", handler.Body);
        Assert.Contains("\"recipients\":[\"233558151870\"]", handler.Body);
        Assert.Contains("\"sandbox\":true", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired, """{"status":"error","message":"Insufficient balance or invalid coverage!"}""", "insufficient_balance")]
    [InlineData(HttpStatusCode.Forbidden, """{"status":"error","message":"Inactive SMS Gateway!"}""", "gateway_inactive")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"status":"error","message":"The message field is required."}""", "validation")]
    [InlineData(HttpStatusCode.InternalServerError, """{"status":"error","message":"SMS request failed!"}""", "http_500")]
    [InlineData(HttpStatusCode.OK, """{"status":"success","data":[{"invalid numbers":["22354674948"]}]}""", "invalid_number")]
    public void Arkesel_maps_documented_failures(HttpStatusCode status, string body, string code)
    {
        var sender = new ArkeselSmsSender(new HttpClient(), Options.Create(SmsConfig()), NullLogger<ArkeselSmsSender>.Instance);
        var result = sender.Interpret(status, body);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.ErrorCode);
    }

    // ---- Hubtel SMS ----

    [Fact]
    public async Task Hubtel_sends_documented_request_and_reads_message_id_and_rate()
    {
        var handler = new StubHandler(HttpStatusCode.Created,
            """{"rate":0.0246,"messageId":"fab43849-6c5b-4334-a88b-d06520b1ace8","status":0,"networkId":"62002","clientReference":null,"statusDescription":"request submitted successfully"}""");
        var sender = new HubtelSmsSender(new HttpClient(handler), Options.Create(SmsConfig()), NullLogger<HubtelSmsSender>.Instance);

        var result = await sender.SendAsync(Message, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("fab43849-6c5b-4334-a88b-d06520b1ace8", result.ProviderMessageId);
        Assert.Equal(0.0246m, result.Cost);
        Assert.Equal("https://sms.hubtel.com/v1/messages/send", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Basic", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("cid:secret", Encoding.UTF8.GetString(Convert.FromBase64String(handler.Request.Headers.Authorization.Parameter!)));
        Assert.Contains("\"From\":\"GRID\"", handler.Body);
        Assert.Contains("\"To\":\"233558151870\"", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, """{"status":1,"statusDescription":"Invalid destination"}""", "hubtel_1")]
    [InlineData(HttpStatusCode.BadRequest, """{"status":4,"statusDescription":"Not routable"}""", "hubtel_4")]
    [InlineData(HttpStatusCode.Unauthorized, "", "unauthorized")]
    [InlineData(HttpStatusCode.PaymentRequired, "", "insufficient_balance")]
    public void Hubtel_maps_documented_failures(HttpStatusCode status, string body, string code)
    {
        var sender = new HubtelSmsSender(new HttpClient(), Options.Create(SmsConfig()), NullLogger<HubtelSmsSender>.Instance);
        var result = sender.Interpret(status, body);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public async Task Network_errors_become_failed_results_not_exceptions()
    {
        var sender = new ArkeselSmsSender(new HttpClient(new ThrowingHandler()), Options.Create(SmsConfig()), NullLogger<ArkeselSmsSender>.Instance);
        var result = await sender.SendAsync(Message, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.Equal("network_error", result.ErrorCode);
    }

    // ---- Router ----

    [Fact]
    public async Task Router_falls_back_once_when_primary_fails()
    {
        var arkesel = new ScriptedSender(SmsProviderKind.Arkesel, accept: false);
        var hubtel = new ScriptedSender(SmsProviderKind.Hubtel, accept: true);
        var config = SmsConfig();
        config.Provider = SmsProviderKind.Arkesel;
        config.FallbackProvider = SmsProviderKind.Hubtel;
        var router = new SmsRouter([arkesel, hubtel], Options.Create(config), NullLogger<SmsRouter>.Instance);

        var result = await router.SendAsync(Message, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(SmsProviderKind.Hubtel, result.Provider);
        Assert.Equal(1, arkesel.Calls);
        Assert.Equal(1, hubtel.Calls);
    }

    [Fact]
    public async Task Router_uses_only_the_selected_provider_when_it_succeeds()
    {
        var arkesel = new ScriptedSender(SmsProviderKind.Arkesel, accept: true);
        var hubtel = new ScriptedSender(SmsProviderKind.Hubtel, accept: true);
        var config = SmsConfig();
        config.Provider = SmsProviderKind.Hubtel;
        config.FallbackProvider = SmsProviderKind.Arkesel;
        var router = new SmsRouter([arkesel, hubtel], Options.Create(config), NullLogger<SmsRouter>.Instance);

        var result = await router.SendAsync(Message, CancellationToken.None);

        Assert.Equal(SmsProviderKind.Hubtel, result.Provider);
        Assert.Equal(0, arkesel.Calls);
    }

    [Fact]
    public async Task Router_without_fallback_reports_the_failure()
    {
        var arkesel = new ScriptedSender(SmsProviderKind.Arkesel, accept: false);
        var config = SmsConfig();
        config.Provider = SmsProviderKind.Arkesel;
        var router = new SmsRouter([arkesel], Options.Create(config), NullLogger<SmsRouter>.Instance);

        Assert.False((await router.SendAsync(Message, CancellationToken.None)).Accepted);
    }

    [Fact]
    public void Options_validation_requires_credentials_for_providers_in_use()
    {
        var config = new SmsOptions { Provider = SmsProviderKind.Arkesel, FallbackProvider = SmsProviderKind.Hubtel };
        var result = new SmsOptionsValidator().Validate(null, config);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Arkesel:ApiKey"));
        Assert.Contains(result.Failures!, f => f.Contains("Hubtel:ClientId"));

        Assert.True(new SmsOptionsValidator().Validate(null, new SmsOptions { Provider = SmsProviderKind.Log }).Succeeded);
    }

    // ---- Hubtel KYC ----

    [Fact]
    public async Task Hubtel_lookup_calls_documented_url_and_reads_name()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"ResponseCode":"0000","Message":"Customer Details","Label":"Customer Details","Data":[{"Display":"name","Value":"JOSEPH ANNOH","Amount":0.0}]}""");
        var options = Options.Create(new KycOptions
        {
            Provider = KycProviderKind.Hubtel,
            Hubtel = new HubtelKycOptions { ApiId = "id", ApiKey = "key", CollectionAccountNumber = "11684" },
        });
        var lookup = new HubtelIdentityLookup(new HttpClient(handler), options, NullLogger<HubtelIdentityLookup>.Instance);

        var result = await lookup.LookupNameAsync("233501431586", CancellationToken.None);

        Assert.Equal(IdentityLookupOutcome.Found, result.Outcome);
        Assert.Equal("JOSEPH ANNOH", result.Name);
        Assert.Equal(
            "https://cs.hubtel.com/commissionservices/11684/3e0841e70afc42fb97d13d19abd36384?destination=233501431586",
            handler.Request!.RequestUri!.ToString());
        Assert.Equal("id:key", Encoding.UTF8.GetString(Convert.FromBase64String(handler.Request.Headers.Authorization!.Parameter!)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"message":"Not found","responseCode":"3000","data":null}""", IdentityLookupOutcome.NotFound)]
    [InlineData(HttpStatusCode.NotFound, """{"message":"Number not registered with Telco","responseCode":"2001","data":null}""", IdentityLookupOutcome.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, """{"message":"Missing required parameter CustomerMsisdn","responseCode":"4000"}""", IdentityLookupOutcome.NotFound)]
    [InlineData(HttpStatusCode.FailedDependency, """{"message":"Failed dependency","responseCode":"2001"}""", IdentityLookupOutcome.Retry)]
    [InlineData(HttpStatusCode.InternalServerError, "oops", IdentityLookupOutcome.Retry)]
    [InlineData(HttpStatusCode.Unauthorized, "", IdentityLookupOutcome.Retry)]
    [InlineData(HttpStatusCode.OK, """{"ResponseCode":"0000","Data":[]}""", IdentityLookupOutcome.NotFound)]
    public void Hubtel_lookup_maps_documented_responses(HttpStatusCode status, string body, IdentityLookupOutcome expected) =>
        Assert.Equal(expected, HubtelIdentityLookup.Interpret(status, body).Outcome);

    // ---- helpers ----

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class ScriptedSender(SmsProviderKind kind, bool accept) : ISmsSender
    {
        public int Calls { get; private set; }
        public SmsProviderKind Kind => kind;

        public Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(accept
                ? new SmsSendResult(true, kind, "id")
                : SmsSendResult.Failed(kind, "down", "Provider down"));
        }
    }
}
