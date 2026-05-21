using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.Reject;

/// <summary>
/// <c>POST /api/v1/admin/establishments/change-requests/{id}/reject</c> —
/// admin transitions PendingReview → Rejected with a mandatory reason
/// (spec §7.4).
///
/// Live establishment data is NOT touched. Proposed document assets that
/// were attached to this change request become orphans; the nightly
/// cleanup job soft-deletes them once it lands (spec §11).
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class RejectChangeRequestEndpoint
    : Endpoint<RejectChangeRequestRequest, RejectChangeRequestResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public RejectChangeRequestEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider clock,
        IOutboxWriter outbox)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _outbox = outbox;
    }

    public override void Configure()
    {
        Post("/api/v1/admin/establishments/change-requests/{id}/reject");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<RejectChangeRequestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Reject a PendingReview ChangeRequest with a mandatory reason.";
            s.Description =
                "The live establishment is unchanged; proposed asset uploads " +
                "become orphans for the nightly cleanup job.";
        });
    }

    public override async Task HandleAsync(RejectChangeRequestRequest req, CancellationToken ct)
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

        var cr = await _db.EstablishmentChangeRequests
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cr is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (cr.Status != EstablishmentChangeRequestStatus.PendingReview)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot reject in status '{cr.Status}'. Allowed: PendingReview.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        cr.Reject(now, _currentUser.UserId, req.Reason);

        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: cr.EstablishmentId,
            action: EstablishmentReviewAction.ChangeRequestRejected,
            occurredAt: now,
            changeRequestId: cr.Id,
            actorAdminId: _currentUser.UserId,
            reason: cr.ReviewReason));

        _outbox.Enqueue(
            EstablishmentEventTypes.ChangeRequestRejected,
            aggregateType: nameof(Establishment),
            aggregateId: cr.EstablishmentId,
            payload: new
            {
                establishmentId = cr.EstablishmentId,
                changeRequestId = cr.Id,
                rejectedByAdminId = _currentUser.UserId,
                reviewedAt = now,
                reason = cr.ReviewReason,
            });
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(
            new RejectChangeRequestResponse(
                Id: cr.Id,
                EstablishmentId: cr.EstablishmentId,
                Status: cr.Status,
                ReviewedAt: cr.ReviewedAt!.Value,
                Reason: cr.ReviewReason!),
            ct);
    }
}

public sealed class RejectChangeRequestRequest
{
    public string Reason { get; init; } = string.Empty;
}

public sealed record RejectChangeRequestResponse(
    Guid Id,
    Guid EstablishmentId,
    EstablishmentChangeRequestStatus Status,
    DateTimeOffset ReviewedAt,
    string Reason);
