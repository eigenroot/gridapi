using Grid.Api.Common;
using Grid.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Consumers;

public static class ConsumerEndpoints
{
    public static void MapConsumerEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/consumers/me").WithTags("Profile").RequireAuthorization();

        me.MapGet("", Get);
        me.MapPut("", Update);
        me.MapPut("/default-property", SetDefaultProperty);
    }

    private static async Task<EnvelopeResult<ConsumerResponse>> Get(CurrentConsumer current, CancellationToken ct) =>
        Envelope.Ok(ConsumerResponse.From(await current.LoadAsync(ct)));

    /// <summary>Full replace of the self-provided profile. The first successful call completes sign-up.</summary>
    private static async Task<EnvelopeResult<ConsumerResponse>> Update(
        UpdateProfileRequest body,
        CurrentConsumer current,
        GridDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var v = new Validator();
        var firstName = v.Required("firstName", body.FirstName, 100, "First name");
        var lastName = v.Optional("lastName", body.LastName, 100, "Last name");
        var email = v.Email("email", body.Email);
        v.ThrowIfInvalid();

        var consumer = await current.LoadAsync(ct);
        consumer.FirstName = firstName;
        consumer.LastName = lastName;
        consumer.Email = email;
        consumer.ProfileCompletedAt ??= time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return Envelope.Ok(ConsumerResponse.From(consumer));
    }

    private static async Task<EnvelopeResult<ConsumerResponse>> SetDefaultProperty(
        SetDefaultPropertyRequest body,
        CurrentConsumer current,
        GridDbContext db,
        CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        if (body.PropertyId is { } propertyId)
        {
            var owned = await db.Properties.AnyAsync(
                p => p.Id == propertyId && p.OwnerConsumerId == consumer.Id && p.ArchivedAt == null, ct);
            if (!owned) throw Errors.NotFound("properties.not_found", "That home was not found.");
        }

        consumer.DefaultPropertyId = body.PropertyId;
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(ConsumerResponse.From(consumer));
    }
}
