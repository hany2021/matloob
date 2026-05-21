using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.AdminReview.ReviewHistory;

/// <summary>
/// <c>GET /api/v1/admin/establishments/{id}/review-history</c> — full
/// audit timeline for one establishment, oldest event first so the admin
/// UI can render it as a story.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
///
/// 404 if the establishment doesn't exist. An establishment with no
/// history yet returns 200 with an empty array — distinct from "no such
/// id" so the admin UI can show "no events yet."
/// </summary>
public sealed class GetReviewHistoryEndpoint : EndpointWithoutRequest<GetReviewHistoryResponse>
{
    private readonly AppDbContext _db;

    public GetReviewHistoryEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/establishments/{id}/review-history");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<GetReviewHistoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Append-only audit timeline for an establishment.";
            s.Description =
                "Oldest event first. Includes lifecycle transitions, " +
                "ChangeRequest events, and member changes.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        // Confirm the establishment exists so an unknown id returns 404 rather
        // than an empty 200, which would be ambiguous.
        var exists = await _db.Establishments
            .AsNoTracking()
            .AnyAsync(e => e.Id == id, ct);
        if (!exists)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var rows = await _db.EstablishmentReviewHistory
            .AsNoTracking()
            .Where(h => h.EstablishmentId == id)
            .OrderBy(h => h.OccurredAt)
            .ThenBy(h => h.Id)
            .Select(h => new ReviewHistoryItem(
                h.Id,
                h.Action,
                h.OccurredAt,
                h.ActorUserId,
                h.ActorAdminId,
                h.Reason,
                h.ChangeRequestId,
                h.SnapshotJson))
            .ToListAsync(ct);

        await Send.OkAsync(new GetReviewHistoryResponse(id, rows), ct);
    }
}

public sealed record GetReviewHistoryResponse(
    Guid EstablishmentId,
    IReadOnlyList<ReviewHistoryItem> Items);

public sealed record ReviewHistoryItem(
    Guid Id,
    EstablishmentReviewAction Action,
    DateTimeOffset OccurredAt,
    string? ActorUserId,
    string? ActorAdminId,
    string? Reason,
    Guid? ChangeRequestId,
    string? SnapshotJson);
