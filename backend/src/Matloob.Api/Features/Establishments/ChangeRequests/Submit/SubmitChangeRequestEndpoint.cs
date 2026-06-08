using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.Submit;

/// <summary>
/// <c>POST /api/v1/establishments/{id}/change-requests/{changeRequestId}/submit</c>
/// — transitions a Draft or Rejected change request to PendingReview after
/// gating on:
/// - the change request has at least one proposed field or document
///   (otherwise 400 with code <c>change_request_empty</c>),
/// - the proposed CR number (if any) is unique across the active
///   PendingReview / Approved / Suspended set, excluding this establishment
///   itself (otherwise 409 with code <c>cr_number_in_use</c>).
///
/// Auth: active Owner of the establishment OR matloob_admin.
///
/// On success, an <see cref="EstablishmentReviewHistory"/> row is appended
/// with action <c>ChangeRequestSubmitted</c>.
/// </summary>
public sealed class SubmitChangeRequestEndpoint : EndpointWithoutRequest<SubmitChangeRequestResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public SubmitChangeRequestEndpoint(
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
        Post("/api/v1/establishments/{id}/change-requests/{changeRequestId}/submit");
        Description(b => b
            .Produces<SubmitChangeRequestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Submit a Draft/Rejected ChangeRequest for admin review.";
            s.Description =
                "Requires at least one proposed field. CR-number uniqueness " +
                "is pre-flighted against PendingReview/Approved/Suspended " +
                "establishments (excluding this one).";
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

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isOwner = await MembershipChecks.HasPermissionAsync(
                _db, establishmentId, _currentUser.UserId, Infrastructure.Auth.Permissions.ChangeRequests.Submit, ct);
            if (!isOwner)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }
        }

        // Suspended-parent guard.
        if (await EstablishmentStatusGuards.WriteIfSuspendedAsync(_db, establishmentId, HttpContext, ct))
        {
            return;
        }

        if (!cr.IsEditableByOwner)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot submit in status '{cr.Status}'. Allowed: Draft, Rejected.",
                ct);
            return;
        }

        if (!cr.HasAnyProposedChange)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status400BadRequest,
                EstablishmentErrorCodes.ChangeRequestEmpty,
                "ChangeRequest must include at least one proposed field or document before submission.",
                ct);
            return;
        }

        // CR-number uniqueness pre-flight (spec §4). If a new CR number is
        // proposed, no OTHER establishment in the watched lifecycle set may
        // hold it.
        if (!string.IsNullOrEmpty(cr.ProposedCommercialRegistrationNumber))
        {
            var collision = await _db.Establishments
                .AsNoTracking()
                .Where(e => e.Id != establishmentId)
                .Where(e => e.CommercialRegistrationNumber == cr.ProposedCommercialRegistrationNumber)
                .Where(e => e.Status == EstablishmentStatus.PendingReview
                         || e.Status == EstablishmentStatus.Approved
                         || e.Status == EstablishmentStatus.Suspended)
                .AnyAsync(ct);
            if (collision)
            {
                await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                    EstablishmentErrorCodes.CrNumberInUse,
                    "Commercial registration number is already pending review or approved on another establishment.",
                    ct);
                return;
            }
        }

        var now = _clock.GetUtcNow();
        cr.Submit(now);

        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishmentId,
            action: EstablishmentReviewAction.ChangeRequestSubmitted,
            occurredAt: now,
            changeRequestId: cr.Id,
            actorUserId: isAdmin ? null : _currentUser.UserId,
            actorAdminId: isAdmin ? _currentUser.UserId : null));

        _outbox.Enqueue(
            EstablishmentEventTypes.ChangeRequestSubmitted,
            aggregateType: nameof(Establishment),
            aggregateId: establishmentId,
            payload: new
            {
                establishmentId,
                changeRequestId = cr.Id,
                submittedAt = now,
                submittedByUserId = _currentUser.UserId,
            });
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(
            new SubmitChangeRequestResponse(
                Id: cr.Id,
                Status: cr.Status,
                SubmittedAt: cr.SubmittedAt!.Value),
            ct);
    }
}

public sealed record SubmitChangeRequestResponse(
    Guid Id,
    EstablishmentChangeRequestStatus Status,
    DateTimeOffset SubmittedAt);
