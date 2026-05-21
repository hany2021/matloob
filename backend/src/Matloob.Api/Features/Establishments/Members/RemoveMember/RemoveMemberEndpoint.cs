using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Establishments.Members.RemoveMember;

/// <summary>
/// <c>DELETE /api/v1/establishments/{id}/members/{memberId}</c> — remove a
/// member from an Approved establishment.
///
/// Choice: <b>soft-delete</b> over deactivate. Deactivation
/// (<c>IsActive=false</c>) means "still a member, just not currently
/// authorized to act"; soft-delete means "no longer a member at all."
/// The two concepts are distinct and worth keeping separate: PATCH
/// handles deactivate / reactivate, DELETE handles end-of-tenure. Both
/// rows survive in the DB for audit; the global query filter hides
/// soft-deleted ones.
///
/// Auth: active Owner of this establishment OR matloob_admin.
///
/// Last-Owner protection (spec §6.2): the same
/// <see cref="MembershipChecks.WouldDropLastOwnerAsync"/> check used by
/// PATCH applies here — the only active Owner row cannot be removed.
/// </summary>
public sealed class RemoveMemberEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public RemoveMemberEndpoint(
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
        Delete("/api/v1/establishments/{id}/members/{memberId}");
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Soft-delete a member from an Approved establishment.";
            s.Description =
                "Active Owner of this establishment OR matloob_admin. " +
                "Cannot remove the last active Owner.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = Route<Guid>("id");
        var memberId = Route<Guid>("memberId");

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == establishmentId, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var member = await _db.EstablishmentMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.EstablishmentId == establishmentId, ct);
        if (member is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isOwner = await MembershipChecks.IsActiveOwnerAsync(
                _db, establishmentId, _currentUser.UserId, ct);
            if (!isOwner)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }
        }

        // Suspended -> 423 Locked.
        if (await EstablishmentStatusGuards.WriteIfSuspendedAsync(HttpContext, establishment, ct))
        {
            return;
        }

        if (establishment.Status != EstablishmentStatus.Approved)
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Members can only be removed in Status=Approved. Current: {establishment.Status}.",
                ct);
            return;
        }

        if (await MembershipChecks.WouldDropLastOwnerAsync(_db, member, ct))
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.LastOwnerProtected,
                "Cannot remove the last active Owner of this establishment.",
                ct);
            return;
        }

        // Remove(...) -> SoftDeleteInterceptor rewrites Deleted -> Modified
        // and stamps IsDeleted/DeletedAt/DeletedBy. The audit interceptor
        // then captures the update.
        _db.EstablishmentMembers.Remove(member);

        var now = _clock.GetUtcNow();
        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishmentId,
            action: EstablishmentReviewAction.MemberRemoved,
            occurredAt: now,
            actorUserId: isAdmin ? null : _currentUser.UserId,
            actorAdminId: isAdmin ? _currentUser.UserId : null,
            snapshotJson: $"{{\"memberId\":\"{member.Id}\",\"userId\":\"{member.UserId}\",\"role\":\"{member.Role}\"}}"));

        _outbox.Enqueue(
            EstablishmentEventTypes.MemberRemoved,
            aggregateType: nameof(Establishment),
            aggregateId: establishmentId,
            payload: new
            {
                establishmentId,
                memberId = member.Id,
                userId = member.UserId,
                role = member.Role.ToString(),
                removedAt = now,
                removedByUserId = _currentUser.UserId,
                removedByAdmin = isAdmin,
            });
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }

    private async Task WriteConflictAsync(string code, string detail, CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = detail,
            Type = "https://httpstatuses.io/409",
        };
        problem.Extensions["code"] = code;
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        HttpContext.Response.ContentType = "application/problem+json";
        await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
    }
}
