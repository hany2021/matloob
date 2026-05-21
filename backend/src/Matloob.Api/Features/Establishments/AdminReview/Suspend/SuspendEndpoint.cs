using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.AdminReview.Suspend;

/// <summary>
/// <c>POST /api/v1/admin/establishments/{id}/suspend</c> — admin
/// transitions <see cref="EstablishmentStatus.Approved"/> →
/// <see cref="EstablishmentStatus.Suspended"/> with a mandatory reason
/// (spec §8).
///
/// Effects:
/// - Reads keep working (members list, admin review queries,
///   change-request reads, asset reads/downloads).
/// - Writes are blocked at the endpoints by the suspension guard added in
///   Phase 8D commit 3 — those endpoints return 423 Locked with code
///   <c>establishment_suspended</c>.
/// - In-flight ChangeRequests and EstablishmentMembers are NOT auto-
///   cancelled. Existing PendingReview change requests stay pending but
///   are also frozen by the same guard.
/// - <see cref="EstablishmentReviewHistory"/> row appended with action
///   <c>Suspended</c>.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class SuspendEndpoint : Endpoint<SuspendRequest, SuspendResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public SuspendEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Post("/api/v1/admin/establishments/{id}/suspend");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<SuspendResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Suspend an Approved establishment with a mandatory reason.";
            s.Description =
                "Reads stay open; mutation endpoints return 423 Locked. " +
                "Existing pending offers / opportunities / change requests " +
                "freeze in their current state -- no auto-cancel.";
        });
    }

    public override async Task HandleAsync(SuspendRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");

        if (string.IsNullOrWhiteSpace(req.Reason))
        {
            AddError(r => r.Reason, "Reason is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }
        if (req.Reason.Length > 2000)
        {
            AddError(r => r.Reason, "Reason must be 2000 characters or fewer.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (establishment.Status != EstablishmentStatus.Approved)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot suspend in status '{establishment.Status}'. Allowed: Approved.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        establishment.Suspend(now, _currentUser.UserId, req.Reason);

        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishment.Id,
            action: EstablishmentReviewAction.Suspended,
            occurredAt: now,
            actorAdminId: _currentUser.UserId,
            reason: establishment.SuspensionReason));

        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(new SuspendResponse(
            Id: establishment.Id,
            Status: establishment.Status,
            SuspendedAt: establishment.SuspendedAt!.Value,
            Reason: establishment.SuspensionReason!), ct);
    }
}

public sealed class SuspendRequest
{
    public string Reason { get; init; } = string.Empty;
}

public sealed record SuspendResponse(
    Guid Id,
    EstablishmentStatus Status,
    DateTimeOffset SuspendedAt,
    string Reason);
