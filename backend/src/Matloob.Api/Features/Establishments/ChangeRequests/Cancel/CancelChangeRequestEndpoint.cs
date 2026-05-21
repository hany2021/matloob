using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.Cancel;

/// <summary>
/// <c>DELETE /api/v1/establishments/{id}/change-requests/{changeRequestId}</c>
/// — cancel an in-flight ChangeRequest. Spec §7.1.
///
/// Auth: any of
/// - the change request's <c>CreatedByUserId</c> (the submitter),
/// - an active <see cref="EstablishmentMemberRole.Owner"/> on the
///   establishment, OR
/// - <c>matloob_admin</c>.
///
/// Status guard: <c>Draft</c> or <c>PendingReview</c> only. Approved /
/// Rejected / Cancelled return 409 <c>cannot_edit_in_status</c>.
///
/// Suspended-establishment decision (documented per the Phase 8E prompt):
/// CANCELLATION IS ALLOWED while the parent establishment is Suspended.
/// Cancelling reduces pending work without mutating the live row — exactly
/// what the suspension is meant to prevent. The submitter can withdraw a
/// proposed-change that no longer applies, and an admin can clean up a
/// stale CR before reinstating. <see cref="EstablishmentStatusGuards"/>
/// is therefore deliberately NOT invoked here.
///
/// The row transitions to <see cref="EstablishmentChangeRequestStatus.Cancelled"/>
/// rather than being soft-deleted: it is still useful audit ("this
/// proposal existed and was withdrawn"). The one-in-flight rule on
/// CreateChangeRequest counts only Draft / PendingReview, so a cancelled
/// row no longer blocks opening a new change request.
/// </summary>
public sealed class CancelChangeRequestEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public CancelChangeRequestEndpoint(
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
        Delete("/api/v1/establishments/{id}/change-requests/{changeRequestId}");
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Cancel a Draft or PendingReview ChangeRequest.";
            s.Description =
                "Submitter, any active Owner, OR matloob_admin may cancel. " +
                "Allowed even while the parent establishment is Suspended.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = Route<Guid>("id");
        var changeRequestId = Route<Guid>("changeRequestId");

        var cr = await _db.EstablishmentChangeRequests
            .FirstOrDefaultAsync(c =>
                c.Id == changeRequestId && c.EstablishmentId == establishmentId, ct);
        if (cr is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var sub = _currentUser.UserId;
        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        var isSubmitter = string.Equals(cr.CreatedByUserId, sub, StringComparison.Ordinal);

        bool isAllowed = isAdmin || isSubmitter;
        if (!isAllowed)
        {
            isAllowed = await MembershipChecks.IsActiveOwnerAsync(
                _db, establishmentId, sub, ct);
        }
        if (!isAllowed)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        if (cr.Status is not (EstablishmentChangeRequestStatus.Draft
                           or EstablishmentChangeRequestStatus.PendingReview))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot cancel ChangeRequest in status '{cr.Status}'. Allowed: Draft, PendingReview.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        cr.Cancel(now, sub);

        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishmentId,
            action: EstablishmentReviewAction.ChangeRequestCancelled,
            occurredAt: now,
            changeRequestId: cr.Id,
            actorUserId: isAdmin ? null : sub,
            actorAdminId: isAdmin ? sub : null));

        _outbox.Enqueue(
            EstablishmentEventTypes.ChangeRequestCancelled,
            aggregateType: nameof(Establishment),
            aggregateId: establishmentId,
            payload: new
            {
                establishmentId,
                changeRequestId = cr.Id,
                cancelledAt = now,
                cancelledByUserId = sub,
                cancelledByAdmin = isAdmin,
            });
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
