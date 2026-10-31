using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grid.Api.Data;
using Grid.Api.Features.Kyc;
using Grid.Api.Integrations.Sms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Grid.Api.Tests.Support;

/// <summary>
/// The real API against a throwaway PostgreSQL database. Point it at a server with
/// <c>GRID_TEST_DB</c> (a connection string without a database name); it defaults to the local
/// server as the current OS user.
/// </summary>
public sealed partial class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _database = $"grid_api_test_{Guid.NewGuid():N}";
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"grid_api_test_media_{Guid.NewGuid():N}");

    /// <summary>Small enough that tests can exceed it cheaply.</summary>
    public const long MediaMaxBytes = 64 * 1024;

    public FakeSmsSender Sms { get; } = new();
    public FakeIdentityLookup Kyc { get; } = new();
    public FakeTime Time { get; } = new();

    private string ConnectionString =>
        $"{Environment.GetEnvironmentVariable("GRID_TEST_DB") ?? $"Host=localhost;Port=5432;Username={Environment.UserName}"};Database={_database}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Grid", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Docs:Enabled", "false");
        builder.UseSetting("RateLimits:OtpPerIpPerHour", "10000");
        builder.UseSetting("Otp:DevelopmentMode", "false");
        builder.UseSetting("Otp:Pepper", "test-pepper-0123456789-0123456789-0123");
        builder.UseSetting("Otp:ReviewNumbers:0209999999", "424242");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789-0123456789");
        builder.UseSetting("Sms:Provider", "Log");
        builder.UseSetting("Kyc:Provider", "None");
        builder.UseSetting("Kyc:RunWorker", "false");
        builder.UseSetting("Media:RootPath", _mediaRoot);
        builder.UseSetting("Media:MaxBytes", MediaMaxBytes.ToString());
        builder.UseSetting("Payouts:EncryptionKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray()));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);
            services.RemoveAll<IIdentityLookup>();
            services.AddSingleton<IIdentityLookup>(Kyc);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    public Task InitializeAsync()
    {
        _ = Server; // Starts the app, which creates and migrates the database.
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<GridDbContext>().Database.EnsureDeletedAsync();
        if (Directory.Exists(_mediaRoot)) Directory.Delete(_mediaRoot, recursive: true);
        await DisposeAsync();
    }

    public HttpClient Client(string app = "consumer")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Grid-App", app);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<GridDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<GridDbContext>());
    }

    public async Task<int> RunKycAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<KycProcessor>().ProcessDueAsync(CancellationToken.None);
    }

    /// <summary>Requests a code, reads it from the fake SMS, verifies it, and returns the session data.</summary>
    public async Task<SignedIn> SignInAsync(string phone, string app = "consumer", string? fullName = null)
    {
        var client = Client(app);
        var request = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = phone });
        await Json.ExpectSuccessAsync(request);

        var verify = await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
            new { phoneNumber = phone, code = Sms.LastCodeFor(phone), fullName });
        var data = await Json.ExpectSuccessAsync(verify);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", data.GetProperty("accessToken").GetString());
        return new SignedIn(client, data);
    }

    public sealed record SignedIn(HttpClient Client, JsonElement Session)
    {
        public string RefreshToken => Session.GetProperty("refreshToken").GetString()!;
        public JsonElement User => Session.GetProperty("user");
    }
}

public sealed partial class FakeSmsSender : ISmsSender
{
    private readonly List<SmsMessage> _sent = [];

    public SmsProviderKind Kind => SmsProviderKind.Log;
    public bool Fail { get; set; }

    public IReadOnlyList<SmsMessage> Sent
    {
        get { lock (_sent) return _sent.ToList(); }
    }

    public Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct)
    {
        if (Fail) return Task.FromResult(SmsSendResult.Failed(Kind, "test_failure", "Configured to fail."));
        lock (_sent) _sent.Add(message);
        return Task.FromResult(new SmsSendResult(true, Kind, Guid.NewGuid().ToString("N")));
    }

    /// <summary>The code in the latest message to <paramref name="phone"/> (any accepted format).</summary>
    public string LastCodeFor(string phone)
    {
        var normalized = Common.PhoneNumbers.Normalize(phone);
        var body = Sent.Last(m => m.To == normalized).Body;
        return CodePattern().Match(body).Groups[1].Value;
    }

    [GeneratedRegex(@"code is (\d+)")]
    private static partial Regex CodePattern();
}

public sealed class FakeIdentityLookup : IIdentityLookup
{
    public string Provider => "fake";
    public bool Enabled { get; set; } = true;
    public Func<string, IdentityLookupResult> Respond { get; set; } =
        _ => new IdentityLookupResult(IdentityLookupOutcome.Found, "KWAME MENSAH");
    public int Calls { get; private set; }

    public Task<IdentityLookupResult> LookupNameAsync(string phoneNumber, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Respond(phoneNumber));
    }
}

/// <summary>Starts at the real time (so issued JWTs validate) and only moves when told to.</summary>
public sealed class FakeTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public static class Json
{
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Asserts a 2xx envelope and returns its <c>data</c> (or default when there is none).</summary>
    public static async Task<JsonElement> ExpectSuccessAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        Assert.True(body.GetProperty("success").GetBoolean());
        return body.TryGetProperty("data", out var data) ? data : default;
    }

    /// <summary>Asserts a failure envelope with the given status and error code.</summary>
    public static async Task<JsonElement> ExpectErrorAsync(HttpResponseMessage response, int status, string code)
    {
        var body = await ReadAsync(response);
        Assert.True((int)response.StatusCode == status, $"expected {status}, got {(int)response.StatusCode}: {body}");
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(code, body.GetProperty("error").GetProperty("code").GetString());
        return body.GetProperty("error");
    }
}
