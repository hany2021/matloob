using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.Create;

/// <summary>
/// <c>POST /api/v1/establishments/{id}/change-requests</c> — opens a new
/// <see cref="EstablishmentChangeRequest"/> in <c>Status = Draft</c>. The
/// Owner then fills in the proposed fields / documents via subsequent
/// endpoints before submitting for admin review.
///
/// Auth: active Owner on this establishment OR matloob_admin. Spec §10:
/// "Submit ChangeRequest — Active Owner". Admin override is convenient
/// for support flows where an admin shepherds a customer through a fix.
///
/// Status guard: Establishment must be <see cref="EstablishmentStatus.Approved"/>.
/// Suspended will eventually return 423 Locked once that phase lands; for
/// now it falls through the same <c>cannot_edit_in_status</c> bucket.
///
/// One-in-flight rule (spec §7.1): if any change request for this
/// establishment is already in Draft or PendingReview, the request is
/// refused with 409 <c>change_request_already_exists</c>. The DB partial
/// unique index <c>ux_establishment_change_requests_pending_per_estab</c>
/// would also block a second PendingReview row, but we widen the check to
/// Draft so the Owner finishes one before opening another.
/// </summary>
public sealed class CreateChangeRequestEndpoint : EndpointWithoutRequest<CreateChangeRequestResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CreateChangeRequestEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Post("/api/v1/establishments/{id}/change-requests");
        Description(b => b
            .Produces<CreateChangeRequestResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Open a new ChangeRequest for an Approved establishment.";
            s.Description =
                "Active Owner or matloob_admin only. At most one Draft/PendingReview " +
                "change request per establishment.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = Route<Guid>("id");

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == establishmentId, ct);
        if (establishment is null)
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
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"ChangeRequests can only be created for Approved establishments. Current: {establishment.Status}.",
                ct);
            return;
        }

        // One-in-flight rule. Both Draft and PendingReview count; rejected /
        // approved / cancelled change requests don't block a new one.
        var hasInFlight = await _db.EstablishmentChangeRequests
            .AsNoTracking()
            .AnyAsync(cr =>
                cr.EstablishmentId == establishmentId &&
                (cr.Status == EstablishmentChangeRequestStatus.Draft ||
                 cr.Status == EstablishmentChangeRequestStatus.PendingReview),
                ct);
        if (hasInFlight)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.ChangeRequestAlreadyExists,
                "An in-flight ChangeRequest (Draft or PendingReview) already exists for this establishment.",
                ct);
            return;
        }

        var cr = EstablishmentChangeRequest.CreateDraft(
            id: Guid.NewGuid(),
            establishmentId: establishmentId,
            createdByUserId: _currentUser.UserId);
        _db.EstablishmentChangeRequests.Add(cr);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueConstraintTranslator.TryTranslate(ex) is { } conflict)
        {
            // The application-side "one-in-flight" check races with a
            // concurrent Create; let the DB-level partial unique index
            // settle the tie and reply with the same 409 the pre-flight
            // would have produced.
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                conflict.Code, conflict.Detail, ct);
            return;
        }

        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId}/change-requests/{cr.Id}";
        await Send.ResponseAsync(
            new CreateChangeRequestResponse(
                Id: cr.Id,
                EstablishmentId: cr.EstablishmentId,
                Status: cr.Status,
                CreatedAt: cr.CreatedAt),
            StatusCodes.Status201Created,
            ct);
    }
}

public sealed record CreateChangeRequestResponse(
    Guid Id,
    Guid EstablishmentId,
    EstablishmentChangeRequestStatus Status,
    DateTimeOffset CreatedAt);
