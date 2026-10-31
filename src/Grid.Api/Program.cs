using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Auth;
using Grid.Api.Features.Catalogue;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Kyc;
using Grid.Api.Features.Media;
using Grid.Api.Features.Properties;
using Grid.Api.Features.Providers;
using Grid.Api.Features.UtilityAccounts;
using Grid.Api.Integrations.Hubtel;
using Grid.Api.Integrations.Sms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var config = builder.Configuration;

// ---- Options (validated at startup so a bad deploy fails fast) ----
services.AddOptions<OtpOptions>().Bind(config.GetSection(OtpOptions.Section)).ValidateOnStart();
services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section)).ValidateOnStart();
services.AddOptions<SmsOptions>().Bind(config.GetSection(SmsOptions.Section)).ValidateOnStart();
services.AddOptions<KycOptions>().Bind(config.GetSection(KycOptions.Section)).ValidateOnStart();
services.AddOptions<MediaOptions>().Bind(config.GetSection(MediaOptions.Section)).ValidateOnStart();
services.AddOptions<PayoutOptions>().Bind(config.GetSection(PayoutOptions.Section)).ValidateOnStart();
services.AddSingleton<IValidateOptions<OtpOptions>, OtpOptionsValidator>();
services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
services.AddSingleton<IValidateOptions<SmsOptions>, SmsOptionsValidator>();
services.AddSingleton<IValidateOptions<KycOptions>, KycOptionsValidator>();
services.AddSingleton<IValidateOptions<MediaOptions>, MediaOptionsValidator>();
services.AddSingleton<IValidateOptions<PayoutOptions>, PayoutOptionsValidator>();

if (builder.Environment.IsProduction() && config.GetValue<bool>("Otp:DevelopmentMode"))
    throw new InvalidOperationException("Otp:DevelopmentMode must not be enabled in Production.");

// ---- Infrastructure ----
services.AddSingleton(TimeProvider.System);
services.AddHttpContextAccessor();
services.AddDbContext<GridDbContext>(o => o
    .UseNpgsql(config.GetConnectionString("Grid"))
    .UseSnakeCaseNamingConvention());

services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

if (config.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
{
    // Behind Caddy on the same host: take the client IP from X-Forwarded-For.
    services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

// ---- SMS: every provider is registered; Sms:Provider / Sms:FallbackProvider choose between them ----
services.AddSingleton<ISmsSender, LogSmsSender>();
services.AddHttpClient<ArkeselSmsSender>(c => c.Timeout = TimeSpan.FromSeconds(15));
services.AddHttpClient<HubtelSmsSender>(c => c.Timeout = TimeSpan.FromSeconds(15));
services.AddTransient<ISmsSender>(sp => sp.GetRequiredService<ArkeselSmsSender>());
services.AddTransient<ISmsSender>(sp => sp.GetRequiredService<HubtelSmsSender>());
services.AddScoped<SmsRouter>();

// ---- KYC: optional; with Kyc:Provider=None nothing calls out ----
if (config.GetValue<KycProviderKind>("Kyc:Provider") == KycProviderKind.Hubtel)
{
    services.AddHttpClient<HubtelIdentityLookup>();
    services.AddTransient<IIdentityLookup>(sp => sp.GetRequiredService<HubtelIdentityLookup>());
}
else
{
    services.AddSingleton<IIdentityLookup, NoIdentityLookup>();
}
services.AddScoped<KycProcessor>();
services.AddHostedService<KycWorker>();

// ---- Auth ----
services.AddScoped<OtpService>();
services.AddScoped<TokenService>();
services.AddScoped<CurrentConsumer>();

// ---- Provider onboarding and media ----
services.AddScoped<CurrentProvider>();
services.AddSingleton<PayoutProtector>();
services.AddSingleton<IMediaStore, LocalMediaStore>();

services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>, TimeProvider>((o, jwt, time) =>
    {
        var skew = TimeSpan.FromSeconds(30);
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt.Value),
            // Judge expiry by the same clock that issued the token.
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                return (notBefore is null || notBefore <= now + skew) && expires is { } exp && exp > now - skew;
            },
        };
        o.Events = new JwtBearerEvents
        {
            // Keep 401/403 in the standard envelope. 401 is what tells the apps to refresh.
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                await Envelope.WriteErrorAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized,
                    "auth.unauthenticated", "Sign in to continue.");
            },
            OnForbidden = ctx => Envelope.WriteErrorAsync(ctx.HttpContext, StatusCodes.Status403Forbidden,
                "auth.forbidden", "You don't have access to this."),
        };
    });
services.AddAuthorization();

services.AddRateLimiter(o =>
{
    // Per-IP backstop for the OTP endpoints; per-phone limits are enforced by OtpService.
    o.AddPolicy(AuthEndpoints.OtpRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:OtpPerIpPerHour", 30),
            Window = TimeSpan.FromHours(1),
        }));
    o.OnRejected = async (ctx, _) =>
    {
        var retryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var r) ? (int)r.TotalSeconds : 3600;
        await Envelope.WriteErrorAsync(ctx.HttpContext, StatusCodes.Status429TooManyRequests,
            "auth.otp_rate_limited", "Too many requests. Try again later.", new { retryAfterSeconds = retryAfter });
    };
});

services.AddOpenApi();

var app = builder.Build();

// ---- Pipeline ----
if (config.GetValue<bool>("ReverseProxy:TrustForwardedHeaders")) app.UseForwardedHeaders();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (config.GetValue("Docs:Enabled", !app.Environment.IsProduction()))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health", async (GridDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "database_unreachable" }, statusCode: 503))
    .ExcludeFromDescription();

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapConsumerEndpoints();
api.MapPropertyEndpoints();
api.MapUtilityAccountEndpoints();
api.MapCatalogueEndpoints();
api.MapMediaEndpoints();
api.MapProviderAccountEndpoints();
api.MapProviderApplicationEndpoints();
api.MapProviderSetupEndpoints();
api.MapProviderEvidenceEndpoints();
api.MapPayoutMethodEndpoints();

// Unknown routes get the envelope too.
app.MapFallback((HttpContext http) =>
    Envelope.WriteErrorAsync(http, StatusCodes.Status404NotFound, "route.not_found", "No such endpoint."));

if (config.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GridDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment()) await DevelopmentSeed.RunAsync(db);
}

app.Run();

public partial class Program;
