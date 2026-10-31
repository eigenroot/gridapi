using System.Security.Claims;
using Grid.Api.Common;
using Grid.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Grid.Api.Features.Consumers;

/// <summary>
/// The signed-in person, taken from the access token's <c>sub</c> claim. Ownership checks always
/// start here — never from an id in the request body.
/// </summary>
public sealed class CurrentConsumer(IHttpContextAccessor accessor, GridDbContext db)
{
    public Guid Id
    {
        get
        {
            var sub = accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(sub, out var id) ? id : throw Errors.Unauthenticated();
        }
    }

    /// <summary>Loads the consumer for update; rejects suspended accounts.</summary>
    public async Task<Consumer> LoadAsync(CancellationToken ct)
    {
        var id = Id;
        var consumer = await db.Consumers.SingleOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw Errors.Unauthenticated();
        if (consumer.Status == ConsumerStatus.Suspended) throw Errors.ConsumerSuspended();
        return consumer;
    }
}
