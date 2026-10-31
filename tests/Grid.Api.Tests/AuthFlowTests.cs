using System.Net.Http.Headers;
using System.Net.Http.Json;
using Grid.Api.Common;
using Grid.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Tests;

public class AuthFlowTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task New_phone_signs_in_and_completes_profile()
    {
        var signedIn = await api.SignInAsync("0551000001");

        Assert.True(signedIn.Session.GetProperty("isNewUser").GetBoolean());
        Assert.Equal("233551000001", signedIn.User.GetProperty("phoneNumber").GetString());
        Assert.False(signedIn.User.GetProperty("profileComplete").GetBoolean());

        var update = await signedIn.Client.PutAsJsonAsync("/api/v1/consumers/me",
            new { firstName = " Abena ", lastName = "Owusu", email = "Abena@Example.com" });
        var profile = await Json.ExpectSuccessAsync(update);
        Assert.Equal("Abena", profile.GetProperty("firstName").GetString());
        Assert.Equal("Abena Owusu", profile.GetProperty("fullName").GetString());
        Assert.Equal("abena@example.com", profile.GetProperty("email").GetString());
        Assert.True(profile.GetProperty("profileComplete").GetBoolean());
        Assert.Equal("Mtn", profile.GetProperty("phoneNetwork").GetString());

        var me = await Json.ExpectSuccessAsync(await signedIn.Client.GetAsync("/api/v1/auth/me"));
        Assert.Equal("Abena Owusu", me.GetProperty("fullName").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.GetProperty("providerAccountId").ValueKind);
    }

    [Fact]
    public async Task Returning_phone_gets_the_same_record_from_either_app_in_any_format()
    {
        var first = await api.SignInAsync("0551000002", app: "consumer");
        api.Time.Advance(TimeSpan.FromMinutes(2));
        var second = await api.SignInAsync("+233 55 100 0002", app: "pro");

        Assert.False(second.Session.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(first.User.GetProperty("id").GetString(), second.User.GetProperty("id").GetString());

        var row = await api.WithDbAsync(db => db.Consumers.SingleAsync(c => c.PhoneNumber == "233551000002"));
        Assert.Equal(AppKind.Consumer, row.FirstSeenApp);
        Assert.Equal(AppKind.Pro, row.LastLoginApp);
    }

    [Fact]
    public async Task Pro_full_name_at_sign_in_completes_the_profile()
    {
        var signedIn = await api.SignInAsync("0551000003", app: "pro", fullName: "Kofi  Mensah Junior");
        Assert.Equal("Kofi", signedIn.User.GetProperty("firstName").GetString());
        Assert.Equal("Mensah Junior", signedIn.User.GetProperty("lastName").GetString());
        Assert.True(signedIn.User.GetProperty("profileComplete").GetBoolean());
    }

    [Fact]
    public async Task Code_is_hashed_at_rest_and_texted_in_233_format()
    {
        await Json.ExpectSuccessAsync(await api.Client().PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000004" }));
        var code = api.Sms.LastCodeFor("0551000004");
        Assert.Matches(@"^\d{6}$", code);
        Assert.Equal("233551000004", api.Sms.Sent.Last().To);

        var stored = await api.WithDbAsync(db => db.OtpChallenges.SingleAsync(c => c.PhoneNumber == "233551000004"));
        Assert.NotEqual(code, stored.CodeHash);
        Assert.DoesNotContain(code, stored.CodeHash);
    }

    [Fact]
    public async Task Wrong_codes_count_down_then_lock_the_challenge()
    {
        var client = api.Client();
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000005" }));
        var real = api.Sms.LastCodeFor("0551000005");
        var wrong = real == "000000" ? "111111" : "000000";

        for (var remaining = 4; remaining >= 1; remaining--)
        {
            var error = await Json.ExpectErrorAsync(
                await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0551000005", code = wrong }),
                400, "auth.otp_invalid");
            Assert.Equal(remaining, error.GetProperty("detail").GetProperty("attemptsRemaining").GetInt32());
        }
        await Json.ExpectErrorAsync(
            await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0551000005", code = wrong }),
            429, "auth.otp_attempts_exceeded");

        // Even the right code no longer works.
        await Json.ExpectErrorAsync(
            await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0551000005", code = real }),
            429, "auth.otp_attempts_exceeded");
    }

    [Fact]
    public async Task Code_expires_and_can_only_be_used_once()
    {
        var client = api.Client();
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000006" }));
        var code = api.Sms.LastCodeFor("0551000006");

        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0551000006", code }));
        await Json.ExpectErrorAsync(
            await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0551000006", code }),
            400, "auth.otp_expired");

        api.Time.Advance(TimeSpan.FromMinutes(2));
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000006" }));
        var second = api.Sms.LastCodeFor("0551000006");
        api.Time.Advance(TimeSpan.FromMinutes(6));
        await Json.ExpectErrorAsync(
            await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0551000006", code = second }),
            400, "auth.otp_expired");
    }

    [Fact]
    public async Task Requesting_a_new_code_retires_the_old_one()
    {
        var client = api.Client();
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000007" }));
        var old = api.Sms.LastCodeFor("0551000007");
        api.Time.Advance(TimeSpan.FromSeconds(61));
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000007" }));
        var current = api.Sms.LastCodeFor("0551000007");

        if (old != current)
        {
            await Json.ExpectErrorAsync(
                await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0551000007", code = old }),
                400, "auth.otp_invalid");
        }
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0551000007", code = current }));
    }

    [Fact]
    public async Task Resend_cooldown_and_hourly_cap_are_enforced_per_phone()
    {
        var client = api.Client();
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000008" }));

        var cooldown = await Json.ExpectErrorAsync(
            await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000008" }),
            429, "auth.otp_rate_limited");
        Assert.InRange(cooldown.GetProperty("detail").GetProperty("retryAfterSeconds").GetInt32(), 1, 60);

        for (var i = 0; i < 4; i++)
        {
            api.Time.Advance(TimeSpan.FromSeconds(61));
            await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000008" }));
        }
        api.Time.Advance(TimeSpan.FromSeconds(61));
        await Json.ExpectErrorAsync(
            await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000008" }),
            429, "auth.otp_rate_limited");

        api.Time.Advance(TimeSpan.FromHours(1));
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000008" }));
    }

    [Fact]
    public async Task Sms_outage_returns_503_and_saves_nothing()
    {
        api.Sms.Fail = true;
        try
        {
            await Json.ExpectErrorAsync(
                await api.Client().PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000009" }),
                503, "sms.unavailable");
        }
        finally
        {
            api.Sms.Fail = false;
        }
        Assert.False(await api.WithDbAsync(db => db.OtpChallenges.AnyAsync(c => c.PhoneNumber == "233551000009")));

        // No cooldown was spent on the failed attempt.
        await Json.ExpectSuccessAsync(await api.Client().PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0551000009" }));
    }

    [Fact]
    public async Task Review_numbers_use_their_fixed_code_without_sending_sms()
    {
        var client = api.Client();
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0209999999" }));
        Assert.DoesNotContain(api.Sms.Sent, m => m.To == "233209999999");

        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { phoneNumber = "0209999999", code = "424242" }));
    }

    [Fact]
    public async Task Invalid_phone_and_malformed_body_use_the_envelope()
    {
        await Json.ExpectErrorAsync(
            await api.Client().PostAsJsonAsync("/api/v1/auth/otp/request", new { phoneNumber = "0302123456" }),
            400, "auth.invalid_phone");

        var malformed = await api.Client().PostAsync("/api/v1/auth/otp/request",
            new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        await Json.ExpectErrorAsync(malformed, 400, "request.malformed");
    }

    [Fact]
    public async Task Missing_or_bad_token_is_401_in_the_envelope()
    {
        await Json.ExpectErrorAsync(await api.Client().GetAsync("/api/v1/auth/me"), 401, "auth.unauthenticated");

        var client = api.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        await Json.ExpectErrorAsync(await client.GetAsync("/api/v1/consumers/me"), 401, "auth.unauthenticated");
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_session()
    {
        var signedIn = await api.SignInAsync("0551000010");
        var client = api.Client();

        var rotated = await Json.ExpectSuccessAsync(
            await client.PostAsJsonAsync("/api/v1/auth/tokens/refresh", new { refreshToken = signedIn.RefreshToken }));
        var next = rotated.GetProperty("refreshToken").GetString();
        Assert.NotEqual(signedIn.RefreshToken, next);

        // Replaying the old token is treated as theft: it fails and kills the new one too.
        await Json.ExpectErrorAsync(
            await client.PostAsJsonAsync("/api/v1/auth/tokens/refresh", new { refreshToken = signedIn.RefreshToken }),
            401, "auth.refresh_invalid");
        await Json.ExpectErrorAsync(
            await client.PostAsJsonAsync("/api/v1/auth/tokens/refresh", new { refreshToken = next }),
            401, "auth.refresh_invalid");
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var signedIn = await api.SignInAsync("0551000011");
        var logout = await api.Client().PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = signedIn.RefreshToken });
        var body = await Json.ReadAsync(logout);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(body.TryGetProperty("data", out _)); // void commands carry no data key

        await Json.ExpectErrorAsync(
            await api.Client().PostAsJsonAsync("/api/v1/auth/tokens/refresh", new { refreshToken = signedIn.RefreshToken }),
            401, "auth.refresh_invalid");
    }

    [Fact]
    public async Task Profile_requires_a_first_name_and_a_valid_email()
    {
        var signedIn = await api.SignInAsync("0551000012");
        var error = await Json.ExpectErrorAsync(
            await signedIn.Client.PutAsJsonAsync("/api/v1/consumers/me", new { firstName = " ", email = "nope" }),
            400, "validation.failed");
        var fields = error.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("field").GetString()).ToList();
        Assert.Contains("firstName", fields);
        Assert.Contains("email", fields);
    }
}
