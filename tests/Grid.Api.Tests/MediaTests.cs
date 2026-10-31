using System.Net.Http.Json;
using System.Text.Json;
using Grid.Api.Tests.Support;

namespace Grid.Api.Tests;

public class MediaTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> TicketAsync(HttpClient client, string contentType = "image/jpeg", long sizeBytes = 256, string purpose = "identity") =>
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/media/upload-requests",
            new { purpose, fileName = "card.jpg", contentType, sizeBytes }));

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string key) =>
        client.PostAsJsonAsync("/api/v1/media/uploads/confirm", new { storageKey = key });

    [Fact]
    public async Task Upload_round_trip_and_owner_only_download()
    {
        var owner = await api.SignInAsync("0241000001", "pro");
        var ticket = await TicketAsync(owner.Client);
        var url = ticket.GetProperty("uploadUrl").GetString()!;
        var key = ticket.GetProperty("storageKey").GetString()!;
        Assert.StartsWith("http://localhost/api/v1/media/uploads/", url);
        Assert.Equal("image/jpeg", ticket.GetProperty("headers").GetProperty("Content-Type").GetString());

        var bytes = Onboarding.Jpeg(300);
        await Json.ExpectErrorAsync(await ConfirmAsync(owner.Client, key), 400, "media.not_uploaded");
        await Json.ExpectErrorAsync(await Onboarding.PutBytesAsync(owner.Client, url, bytes, "image/jpeg"), 400, "media.too_large");
        await Json.ExpectSuccessAsync(await Onboarding.PutBytesAsync(owner.Client, url, bytes[..256], "image/jpeg"));
        var confirmed = await Json.ExpectSuccessAsync(await ConfirmAsync(owner.Client, key));
        Assert.Equal("Confirmed", confirmed.GetProperty("status").GetString());
        Assert.Equal(256, confirmed.GetProperty("sizeBytes").GetInt64());

        await Json.ExpectErrorAsync(await Onboarding.PutBytesAsync(owner.Client, url, bytes[..256], "image/jpeg"), 409, "media.already_confirmed");

        var download = await owner.Client.GetAsync($"/api/v1/media/{key}");
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes[..256], await download.Content.ReadAsByteArrayAsync());

        var stranger = await api.SignInAsync("0241000002", "pro");
        await Json.ExpectErrorAsync(await stranger.Client.GetAsync($"/api/v1/media/{key}"), 404, "media.not_found");
        await Json.ExpectErrorAsync(await Onboarding.PutBytesAsync(stranger.Client, url, bytes[..256], "image/jpeg"), 404, "media.not_found");
        await Json.ExpectErrorAsync(await ConfirmAsync(stranger.Client, key), 404, "media.not_found");
        await Json.ExpectErrorAsync(await api.Client("pro").GetAsync($"/api/v1/media/{key}"), 401, "auth.unauthenticated");
    }

    [Fact]
    public async Task Unsupported_and_oversized_uploads_are_refused_up_front()
    {
        var me = await api.SignInAsync("0241000003", "pro");
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/media/upload-requests",
            new { purpose = "identity", fileName = "x.gif", contentType = "image/gif", sizeBytes = 10 }), 400, "media.unsupported_type");
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/media/upload-requests",
            new { purpose = "identity", fileName = "x.jpg", contentType = "image/jpeg", sizeBytes = ApiFactory.MediaMaxBytes + 1 }),
            400, "media.too_large");
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/media/upload-requests",
            new { fileName = "", contentType = "image/jpeg" }), 400, "validation.failed");
    }

    [Fact]
    public async Task Bytes_must_match_the_declared_type()
    {
        var me = await api.SignInAsync("0241000004", "pro");

        var ticket = await TicketAsync(me.Client);
        var url = ticket.GetProperty("uploadUrl").GetString()!;
        await Json.ExpectErrorAsync(await Onboarding.PutBytesAsync(me.Client, url, Onboarding.Jpeg(), "image/png"), 400, "media.unsupported_type");

        // Declared JPEG, actually PNG: caught at confirmation.
        await Json.ExpectSuccessAsync(await Onboarding.PutBytesAsync(me.Client, url, Onboarding.Png(), "image/jpeg"));
        await Json.ExpectErrorAsync(await ConfirmAsync(me.Client, ticket.GetProperty("storageKey").GetString()!), 400, "media.unsupported_type");

        // A real PDF passes.
        var pdf = "%PDF-1.7\n"u8.ToArray().Concat(new byte[100]).ToArray();
        var pdfTicket = await TicketAsync(me.Client, "application/pdf", pdf.Length, "certificates");
        await Json.ExpectSuccessAsync(await Onboarding.PutBytesAsync(me.Client, pdfTicket.GetProperty("uploadUrl").GetString()!, pdf, "application/pdf"));
        await Json.ExpectSuccessAsync(await ConfirmAsync(me.Client, pdfTicket.GetProperty("storageKey").GetString()!));
    }

    [Fact]
    public async Task Upload_links_expire()
    {
        var me = await api.SignInAsync("0241000005", "pro");
        var ticket = await TicketAsync(me.Client);
        api.Time.Advance(TimeSpan.FromMinutes(16));
        await Json.ExpectErrorAsync(await Onboarding.PutBytesAsync(me.Client, ticket.GetProperty("uploadUrl").GetString()!, Onboarding.Jpeg(), "image/jpeg"),
            400, "media.upload_expired");
    }

    [Fact]
    public async Task Uploads_attach_once_to_the_right_kind_of_record()
    {
        var me = await api.SignInAsync("0241000006", "pro", fullName: "Efua Files");
        await Onboarding.CreateAccountAsync(me.Client, "Efua Files");

        var portfolioKey = await Onboarding.UploadAsync(me.Client, "portfolio", Onboarding.Jpeg());
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-verification-documents",
            new { documentType = "GhanaCard", storageKey = portfolioKey }), 400, "media.wrong_purpose");

        var identityKey = await Onboarding.UploadAsync(me.Client, "identity", Onboarding.Jpeg());
        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-verification-documents",
            new { documentType = "GhanaCard", storageKey = identityKey }));
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-verification-documents",
            new { documentType = "GhanaCardBack", storageKey = identityKey }), 409, "media.already_attached");

        // Someone else's upload can't be attached, even with its key.
        var other = await api.SignInAsync("0241000007", "pro", fullName: "Other Person");
        await Onboarding.CreateAccountAsync(other.Client, "Other Person");
        await Json.ExpectErrorAsync(await other.Client.PostAsJsonAsync("/api/v1/provider-portfolio",
            new { title = "Mine", storageKey = portfolioKey }), 404, "media.not_found");
        Assert.Empty((await Json.ExpectSuccessAsync(await other.Client.GetAsync("/api/v1/provider-verification-documents"))).EnumerateArray());

        var item = await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-portfolio",
            new { title = "Kitchen deep clean", storageKey = portfolioKey }));
        await Json.ExpectSuccessAsync(await me.Client.DeleteAsync($"/api/v1/provider-portfolio/{item.GetProperty("id")}"));
        Assert.Empty((await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-portfolio"))).EnumerateArray());
    }
}
