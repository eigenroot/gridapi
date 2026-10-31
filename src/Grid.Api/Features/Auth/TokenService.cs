using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Grid.Api.Features.Auth;

public sealed record IssuedTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record ClientInfo(AppKind App, string? Ip, string? UserAgent);

public sealed class TokenService(GridDbContext db, IOptions<JwtOptions> options, TimeProvider time)
{
    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.SigningKey));

    /// <summary>Starts a new session (a new refresh-token family).</summary>
    public async Task<IssuedTokens> IssueAsync(Consumer consumer, ClientInfo client, CancellationToken ct)
    {
        var (tokens, row) = Create(consumer, Guid.CreateVersion7(), client);
        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(ct);
        return tokens;
    }

    /// <summary>
    /// Swaps a refresh token for a new pair. Each token works once; presenting a token that was
    /// already swapped means it leaked or was replayed, so the whole session is revoked.
    /// </summary>
    public async Task<(IssuedTokens Tokens, Consumer Consumer)> RefreshAsync(
        string rawToken, ClientInfo client, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = HashToken(rawToken);
        var current = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null) throw Errors.RefreshInvalid();

        if (current.RevokedAt is not null)
        {
            if (current.ReplacedById is not null) await RevokeFamilyAsync(current.FamilyId, ct);
            throw Errors.RefreshInvalid();
        }
        if (current.ExpiresAt <= now) throw Errors.RefreshInvalid();

        var consumer = await db.Consumers.SingleAsync(c => c.Id == current.ConsumerId, ct);
        if (consumer.Status == ConsumerStatus.Suspended) throw Errors.ConsumerSuspended();

        var (tokens, next) = Create(consumer, current.FamilyId, client);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Conditional so two concurrent refreshes with the same token can't both succeed.
        var rotated = await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedById, next.Id), ct);
        if (rotated == 0)
        {
            await tx.RollbackAsync(ct);
            await RevokeFamilyAsync(current.FamilyId, ct);
            throw Errors.RefreshInvalid();
        }
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (tokens, consumer);
    }

    /// <summary>Ends the session the token belongs to. Unknown tokens are ignored.</summary>
    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var hash = HashToken(rawToken);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);
        if (familyId is { } id) await RevokeFamilyAsync(id, ct);
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private (IssuedTokens, RefreshToken) Create(Consumer consumer, Guid familyId, ClientInfo client)
    {
        var config = options.Value;
        var now = time.GetUtcNow();
        var accessExpires = now.AddMinutes(config.AccessTokenMinutes);
        var refreshExpires = now.AddDays(config.RefreshTokenDays);

        var access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = config.Issuer,
            Audience = config.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = accessExpires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, consumer.Id.ToString()),
                new Claim("phone_number", consumer.PhoneNumber),
                new Claim("app", client.App.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256),
        });

        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var row = new RefreshToken
        {
            ConsumerId = consumer.Id,
            TokenHash = HashToken(raw),
            FamilyId = familyId,
            ExpiresAt = refreshExpires,
            App = client.App,
            CreatedIp = client.Ip,
            UserAgent = client.UserAgent is { Length: > 300 } ua ? ua[..300] : client.UserAgent,
        };

        return (new IssuedTokens(access, accessExpires, raw, refreshExpires), row);
    }

    private static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
