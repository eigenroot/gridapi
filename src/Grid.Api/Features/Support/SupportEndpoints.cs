using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Providers;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Support;

public enum TicketCategory { Job, Payment, Customer, Safety, Account, Other }

public enum TicketStatus { Open, InProgress, Resolved }

/// <summary>A problem someone reported from either app, for GRID support to work through.</summary>
public sealed class SupportTicket : Entity
{
    /// <summary>Counts up from a sequence; shown as <c>GRID-2841</c>.</summary>
    public int Number { get; set; }
    public Guid ConsumerId { get; set; }
    public Guid? ProviderAccountId { get; set; }
    public Guid? JobId { get; set; }
    public AppKind App { get; set; }
    public TicketCategory Category { get; set; }
    public required string Subject { get; set; }
    public required string Details { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public string? ResolutionNote { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public string Reference => $"GRID-{Number}";
}

public sealed record CreateTicketRequest(TicketCategory? Category, string? Subject, string? Details, Guid? JobId);

public sealed record TicketResponse(
    Guid Id,
    string Reference,
    TicketCategory Category,
    string Subject,
    string Details,
    Guid? JobId,
    TicketStatus Status,
    string? ResolutionNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt)
{
    public static TicketResponse From(SupportTicket t) =>
        new(t.Id, t.Reference, t.Category, t.Subject, t.Details, t.JobId, t.Status, t.ResolutionNote, t.CreatedAt, t.ResolvedAt);
}

public static class SupportEndpoints
{
    public static void MapSupportEndpoints(this IEndpointRouteBuilder api)
    {
        var tickets = api.MapGroup("/support-tickets").WithTags("Support").RequireAuthorization();
        tickets.MapPost("", Create);
        tickets.MapGet("", List);
    }

    /// <summary>Works for anyone signed in. A <c>jobId</c> must be a job the caller is the provider or the customer of.</summary>
    private static async Task<EnvelopeResult<TicketResponse>> Create(
        CreateTicketRequest body, HttpContext http, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var v = new Validator();
        var subject = v.Required("subject", body.Subject, 120, "Subject");
        var details = v.Required("details", body.Details, 4000, "Details");
        v.ThrowIfInvalid();

        var consumer = await current.LoadAsync(ct);
        var accountId = await CurrentProvider.AccountIdForAsync(db, consumer.Id, ct);
        if (body.JobId is { } jobId
            && !await db.Jobs.AnyAsync(j => j.Id == jobId && (j.CustomerConsumerId == consumer.Id || j.ProviderAccountId == accountId), ct))
            throw Errors.NotFound("jobs.not_found", "That job was not found.");

        var ticket = new SupportTicket
        {
            ConsumerId = consumer.Id,
            ProviderAccountId = accountId,
            JobId = body.JobId,
            App = AppKindHeader.From(http),
            Category = body.Category ?? TicketCategory.Other,
            Subject = subject,
            Details = details,
        };
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        return Envelope.Created(TicketResponse.From(ticket));
    }

    private static async Task<EnvelopeResult<List<TicketResponse>>> List(
        CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var rows = await db.SupportTickets.Where(t => t.ConsumerId == consumerId)
            .OrderByDescending(t => t.CreatedAt).Take(50).ToListAsync(ct);
        return Envelope.Ok(rows.Select(TicketResponse.From).ToList());
    }
}
