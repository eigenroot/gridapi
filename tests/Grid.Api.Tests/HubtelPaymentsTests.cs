using System.Net;
using System.Text;
using System.Text.Json;
using Grid.Api.Features.BillPayments;
using Grid.Api.Integrations.Hubtel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Grid.Api.Tests;

/// <summary>The Hubtel payments client replayed against the samples in Hubtel's developer docs.</summary>
public class HubtelPaymentsTests
{
    private static HubtelPayments Client(StubHandler handler) => new(new HttpClient(handler), Options.Create(new BillPaymentOptions
    {
        Hubtel = new() { ApiId = "api-id", ApiKey = "api-key", CollectionAccountNumber = "11684", DisbursementAccountNumber = "11691" },
    }), NullLogger<HubtelPayments>.Instance);

    private static BillProduct Product(string code) => BillProducts.Require(code);

    [Fact]
    public async Task Receive_money_posts_the_documented_body_with_basic_auth()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"Message":"Transaction pending. Expect callback request for final state","ResponseCode":"0001",
             "Data":{"TransactionId":"09f84e20a283942e807128e8c21d08d6","Description":"Union Dues","ClientReference":"3jL2KlUy3vt21",
                     "Amount":0.8,"Charges":0.05,"AmountAfterCharges":0.8,"AmountCharged":0.85,"DeliveryFee":0.0}}
            """);
        var result = await Client(handler).ReceiveMoneyAsync("3jL2KlUy3vt21", "233200000000", "vodafone-gh", 0.8m, "GRID ECG prepaid",
            "Joe Doe", null, "https://grid.test/cb", CancellationToken.None);

        Assert.Equal("https://rmp.hubtel.com/merchantaccount/merchants/11684/receive/mobilemoney", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("api-id:api-key")), handler.Request.Headers.Authorization!.ToString());
        var body = JsonDocument.Parse(handler.Body).RootElement;
        Assert.Equal("233200000000", body.GetProperty("CustomerMsisdn").GetString());
        Assert.Equal("vodafone-gh", body.GetProperty("Channel").GetString());
        Assert.Equal(0.8m, body.GetProperty("Amount").GetDecimal());
        Assert.Equal("https://grid.test/cb", body.GetProperty("PrimaryCallbackUrl").GetString());
        Assert.Equal(HubtelOutcome.Pending, result.Outcome);
        Assert.Equal("09f84e20a283942e807128e8c21d08d6", result.OrderId);
        Assert.Equal(0.05m, result.Charges);
        Assert.Equal(0.85m, result.AmountCharged);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"ResponseCode":"4000","Message":"Validation errors"}""", HubtelOutcome.Failed)]
    [InlineData(HttpStatusCode.OK, """{"ResponseCode":"4070","Message":"Fees not set"}""", HubtelOutcome.Failed)]
    [InlineData(HttpStatusCode.Forbidden, "", HubtelOutcome.Failed)] // Not whitelisted: it never reached processing.
    [InlineData(HttpStatusCode.ServiceUnavailable, "", HubtelOutcome.Unknown)]
    [InlineData(HttpStatusCode.OK, "not json", HubtelOutcome.Unknown)]
    public void Receive_money_outcomes(HttpStatusCode status, string body, HubtelOutcome expected) =>
        Assert.Equal(expected, HubtelPayments.InterpretReceiveMoney(status, body).Outcome);

    [Fact]
    public async Task ECG_top_up_sends_the_phone_as_destination_and_the_meter_as_bundle()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"ResponseCode":"0001","Message":"Transaction pending. Expect callback request for final state",
             "Data":{"ClientReference":" TestDATA2022052105","Amount":1,"TransactionId":" 6bf810b8f7d5424a973165b36cc1817e","Meta":{"Commission":"0.0171"}}}
            """);
        var result = await Client(handler).PayBillAsync(Product("EcgPrepaid"), "TestDATA2022052105", "233541312238", 1m, "P09137104", null, null,
            "https://grid.test/cb", CancellationToken.None);

        Assert.Equal("https://cs.hubtel.com/commissionservices/11691/e6d6bac062b5499cb1ece1ac3d742a84", handler.Request!.RequestUri!.ToString());
        var body = JsonDocument.Parse(handler.Body).RootElement;
        Assert.Equal("233541312238", body.GetProperty("Destination").GetString());
        Assert.Equal("P09137104", body.GetProperty("Extradata").GetProperty("bundle").GetString());
        Assert.Equal("https://grid.test/cb", body.GetProperty("CallbackUrl").GetString());
        Assert.Equal(HubtelOutcome.Pending, result.Outcome);
        Assert.Equal("6bf810b8f7d5424a973165b36cc1817e", result.TransactionId);
        Assert.Equal(0.0171m, result.Commission);
    }

    [Fact]
    public async Task Water_top_up_carries_email_and_session_in_extradata()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"ResponseCode":"0001","Data":{"Amount":1.0,"TransactionId":"7304f27f559a4680a912ce7ec19e6d2a"}}""");
        await Client(handler).PayBillAsync(Product("GhanaWater"), "ref", "233548359582", 1m, "080706070056",
            "e7a113dd2f8b08b85a149d3db40854de2f13410b", "username@example.com", "https://grid.test/cb", CancellationToken.None);
        var extra = JsonDocument.Parse(handler.Body).RootElement.GetProperty("Extradata");
        Assert.Equal("080706070056", extra.GetProperty("bundle").GetString());
        Assert.Equal("username@example.com", extra.GetProperty("Email").GetString());
        Assert.Equal("e7a113dd2f8b08b85a149d3db40854de2f13410b", extra.GetProperty("SessionId").GetString());
    }

    [Fact]
    public async Task Service_ids_can_be_overridden_from_configuration()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"ResponseCode":"0001","Data":{}}""");
        var client = new HubtelPayments(new HttpClient(handler), Options.Create(new BillPaymentOptions
        {
            Hubtel = new() { DisbursementAccountNumber = "11691", ServiceIds = { ["Dstv"] = "override" } },
        }), NullLogger<HubtelPayments>.Instance);
        await client.PayBillAsync(Product("Dstv"), "r", "7029864396", 50m, null, null, null, "https://grid.test/cb", CancellationToken.None);
        Assert.EndsWith("/commissionservices/11691/override", handler.Request!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("""{"ResponseCode":"4075","Message":"Insufficient prepaid balance."}""", HubtelOutcome.Failed, true, true)]
    [InlineData("""{"ResponseCode":"4101","Message":"Authorization denied"}""", HubtelOutcome.Failed, true, false)]
    [InlineData("""{"ResponseCode":"2001","Message":"The bundle is invalid"}""", HubtelOutcome.Failed, false, false)]
    [InlineData("""{"ResponseCode":"0005","Message":"There was an HTTP failure/exception."}""", HubtelOutcome.Unknown, false, false)]
    public void Bill_payment_outcomes(string body, HubtelOutcome expected, bool retryable, bool noFloat)
    {
        var result = HubtelPayments.InterpretPayBill(HttpStatusCode.OK, body);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal(noFloat, result.InsufficientFloat);
    }

    [Fact]
    public void ECG_meter_query_reads_each_linked_meter()
    {
        var lookup = HubtelPayments.InterpretLookup(Product("EcgPrepaid"), "233541312238", HttpStatusCode.OK, """
            {"ResponseCode":"0000","Message":"Successful","Label":"Successful","Data":[
              {"Display":" THOMAS ANANE (G131099826)","Value":" G131099826","Amount":-1.1432},
              {"Display":" ADEMAU LYDIA (24911947992)","Value":" 24911947992","Amount":0}]}
            """);
        Assert.Equal([new("G131099826", "THOMAS ANANE", -1.1432m), new BeneficiaryCandidate("24911947992", "ADEMAU LYDIA", 0m)], lookup.Candidates);
    }

    [Fact]
    public void Water_query_returns_name_amount_due_and_session()
    {
        var lookup = HubtelPayments.InterpretLookup(Product("GhanaWater"), "091019010006", HttpStatusCode.OK, """
            {"ResponseCode":"0000","Message":"Successful","Label":"Successful","Data":[
              {"Display":"name","Value":"AMAKYE FREMPONG","Amount":0.0},
              {"Display":"amountDue","Value":"371.89","Amount":371.89},
              {"Display":"sessionId","Value":"3792660ccf0e687b64cdb3f776fd6e368ca4260d","Amount":0.0}]}
            """);
        Assert.Equal(new BeneficiaryCandidate("091019010006", "AMAKYE FREMPONG", 371.89m), lookup.Candidates.Single());
        Assert.Equal("3792660ccf0e687b64cdb3f776fd6e368ca4260d", lookup.SessionId);
    }

    [Fact]
    public void StarTimes_query_uses_its_capitalised_keys()
    {
        var lookup = HubtelPayments.InterpretLookup(Product("Startimes"), "02190617357", HttpStatusCode.OK, """
            {"ResponseCode":"0000","Data":[{"Display":"Name","Value":"Joe Nti","Amount":0.0},
              {"Display":"Account Number","Value":"02190617357","Amount":0.0},{"Display":"Bouquet","Value":"DTH_Super ","Amount":0.0}]}
            """);
        Assert.Equal(new BeneficiaryCandidate("02190617357", "Joe Nti", null), lookup.Candidates.Single());
    }

    [Fact]
    public void Telecel_bundles_combine_plan_and_size_in_the_label()
    {
        var lookup = HubtelPayments.InterpretLookup(Product("TelecelData"), "233206439599", HttpStatusCode.OK, """
            {"ResponseCode":"0000","Data":[{"Display":"Chat Monthly","Value":"2 GB","Amount":20.0},{"Display":" Starter Daily","Value":"25 MB","Amount":0.5}]}
            """);
        Assert.Equal([new("2 GB", "Chat Monthly · 2 GB", 20m), new BundleOption("25 MB", "Starter Daily · 25 MB", 0.5m)], lookup.Bundles);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"ResponseCode":"2001","Message":"Invalid account","Data":[]}""", 404)]
    [InlineData(HttpStatusCode.NotFound, """{"ResponseCode":"4000"}""", 404)]
    [InlineData(HttpStatusCode.Unauthorized, "", 503)]
    [InlineData(HttpStatusCode.InternalServerError, "", 503)]
    public void Failed_lookups_tell_not_found_from_unavailable(HttpStatusCode status, string body, int expected)
    {
        var ex = Assert.Throws<Common.ApiException>(() => HubtelPayments.InterpretLookup(Product("Dstv"), "7029864396", status, body));
        Assert.Equal(expected, ex.StatusCode);
    }

    [Theory]
    [InlineData("Paid", HubtelOutcome.Succeeded)]
    [InlineData("Unpaid", HubtelOutcome.Pending)]
    [InlineData("Refunded", HubtelOutcome.Succeeded)]
    public void Status_check_reads_Paid_Unpaid_and_Refunded(string status, HubtelOutcome expected)
    {
        var result = HubtelPayments.InterpretStatus("3jL2KlUy3vt21", HttpStatusCode.OK, $$$"""
            {"message":"Successful","responseCode":"0000","data":{"date":"2024-04-25T21:45:48.4740964Z","status":"{{{status}}}",
             "transactionId":"7fd01221faeb41469daec7b3561bddc5","externalTransactionId":"0000006824852622","paymentMethod":"mobilemoney",
             "clientReference":"3jL2KlUy3vt21","currencyCode":null,"amount":0.1,"charges":0.02,"amountAfterCharges":0.08,"isFulfilled":null}}
            """);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(status, result.Status);
        Assert.Equal(0.1m, result.Amount);
        Assert.Equal("0000006824852622", result.ExternalReference);
    }

    [Fact]
    public void Status_check_for_someone_elses_reference_proves_nothing() =>
        Assert.Equal(HubtelOutcome.Unknown, HubtelPayments.InterpretStatus("mine", HttpStatusCode.OK,
            """{"responseCode":"0000","data":{"status":"Paid","clientReference":"theirs","amount":1}}""").Outcome);

    [Theory]
    [InlineData("""{"message":"Transaction pending.","responseCode":"0001","data":{"orderId":"b230733cd56b4a0fad820e39f66bc27c"}}""", HubtelOutcome.Pending)]
    [InlineData("""{"message":"Could not find order","responseCode":"3000","data":null}""", HubtelOutcome.Failed)]
    [InlineData("""{"message":"less than 1 cedi","responseCode":"4000","data":null}""", HubtelOutcome.Failed)]
    [InlineData("""{"message":"not eligible","responseCode":"4509","data":null}""", HubtelOutcome.Failed)]
    [InlineData("""{"message":"being processed","responseCode":"4515","data":null}""", HubtelOutcome.Pending)]
    public void Refund_outcomes(string body, HubtelOutcome expected) =>
        Assert.Equal(expected, HubtelPayments.InterpretRefund(HttpStatusCode.OK, body).Outcome);

    [Fact]
    public async Task Balance_and_transfer_use_the_documented_endpoints()
    {
        var balance = new StubHandler(HttpStatusCode.OK, """{"responseCode":"0000","message":"Success","data":{"amount":11.50}}""");
        Assert.Equal(11.50m, await Client(balance).GetFloatBalanceAsync(CancellationToken.None));
        Assert.Equal("https://trnf.hubtel.com/api/inter-transfers/prepaid/11691", balance.Request!.RequestUri!.ToString());

        var transfer = new StubHandler(HttpStatusCode.OK, """{"responseCode":"0001","message":"Pending","data":{"clientReference":"bd0i03202ef49","amount":0.8}}""");
        var result = await Client(transfer).TransferToFloatAsync("bd0i03202ef49", 0.8m, "https://grid.test/cb", CancellationToken.None);
        Assert.Equal(HubtelOutcome.Pending, result.Outcome);
        Assert.Equal("https://trnf.hubtel.com/api/inter-transfers/11684", transfer.Request!.RequestUri!.ToString());
        Assert.Equal("11691", JsonDocument.Parse(transfer.Body).RootElement.GetProperty("DestinationAccountNumber").GetString());

        Assert.Equal(HubtelOutcome.Failed, HubtelPayments.InterpretTransferStatus("926943563465346376", HttpStatusCode.OK, """
            {"message":"success","code":"200","data":{"id":"6f1a","clientReference":"926943563465346376","transferAmount":1000.0,"status":"failed","failureReason":"Low balance"}}
            """).Outcome);
    }

    [Fact]
    public void Callbacks_from_receive_money_commission_services_and_refunds_are_read()
    {
        var payment = HubtelPayments.InterpretCallback(JsonDocument.Parse("""
            {"ResponseCode":"0000","Message":"success","Data":{"Amount":0.8,"Charges":0.05,"AmountAfterCharges":0.8,"ClientReference":"3jL2KlUy3vt21",
             "TransactionId":"09f84e20a283942e807128e8c21d08d6","ExternalTransactionId":"2116938399","AmountCharged":0.85,"OrderId":"09f84e20a283942e807128e8c21d08d6"}}
            """).RootElement, out var paymentReference);
        Assert.Equal(HubtelOutcome.Succeeded, payment.Outcome);
        Assert.Equal("3jL2KlUy3vt21", paymentReference);
        Assert.Equal(0.85m, payment.AmountCharged);

        var bill = HubtelPayments.InterpretCallback(JsonDocument.Parse("""
            {"ResponseCode":"2001","Data":{"AmountDebited":0,"TransactionId":"x","ClientReference":"TestTV0101","Amount":50,"Meta":{"Commission":"0.35"}}}
            """).RootElement, out _);
        Assert.Equal(HubtelOutcome.Failed, bill.Outcome);
        Assert.Equal("hubtel.2001", bill.ErrorCode);
        Assert.Equal(0.35m, bill.Commission);

        var refund = HubtelPayments.InterpretCallback(JsonDocument.Parse("""
            {"responseCode":"0000","message":"Successful","data":{"amount":0.5,"charges":0.01,"orderId":"a4e406711db74687bbd307ead23bd015","externalTransactionId":"0000002673867446"}}
            """).RootElement, out _);
        Assert.Equal(HubtelOutcome.Succeeded, refund.Outcome);
        Assert.Equal("a4e406711db74687bbd307ead23bd015", refund.OrderId);
        Assert.Equal("0000002673867446", refund.ExternalReference);
    }

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
}
