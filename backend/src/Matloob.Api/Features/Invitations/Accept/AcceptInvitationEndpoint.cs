using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Members.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Invitations.Accept;

/// <summary>
/// <c>POST /api/v1/invitations/accept</c> (+ legacy <c>/api/invitations/accept</c>).
/// Authenticated user (<c>matloob_user</c>): the invitee accepts after logging
/// in via IdM. Two materialization paths:
///
/// <list type="number">
///   <item>the caller's sub already maps to a local users row → the
///     <see cref="EstablishmentMember"/> is created immediately
///     (<c>membershipMaterialized: true</c>);</item>
///   <item>no local users row yet → the invite is marked Accepted and the
///     CurrentUserSyncService sweep materializes the membership on the next
///     authenticated request (<c>membershipMaterialized: false</c>).</item>
/// </list>
///
/// 404 unknown token, 410 expired, 409 <c>invitation_already_used</c> (already
/// accepted/revoked — also the lost side of a race), 409
/// <c>cannot_edit_in_status</c> if the establishment is not Approved.
/// </summary>
public sealed class AcceptInvitationEndpoint : Endpoint<AcceptInvitationRequest, AcceptInvitationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AcceptInvitationEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Post("/api/invitations/accept", "/api/v1/invitations/accept");
        Policies(Infrastructure.Auth.MatloobPolicies.User);
        Description(b => b
            .Produces<AcceptInvitationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .WithTags("Invitations"));
        Summary(s => s.Summary = "Accept an invitation (auth required).");
    }

    public override async Task HandleAsync(AcceptInvitationRequest req, CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(req.Token))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status400BadRequest,
                "invalid_token", "A token is required.", ct);
            return;
        }

        var hash = InvitationSupport.Hash(req.Token);
        var invitation = await _db.EstablishmentInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invitation is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var now = _clock.GetUtcNow();

        if (invitation.Status != EstablishmentInvitationStatus.Pending)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.InvitationAlreadyUsed,
                "This invitation has already been used.", ct);
            return;
        }

        if (invitation.ExpiresAt <= now)
        {
            invitation.MarkExpired();
            await _db.SaveChangesAsync(ct);
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status410Gone,
                "invitation_expired", "This invitation has expired.", ct);
            return;
        }

        var establishmentApproved = await _db.Establishments
            .AsNoTracking()
            .AnyAsync(e => e.Id == invitation.EstablishmentId
                        && e.Status == EstablishmentStatus.Approved, ct);
        if (!establishmentApproved)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                "The establishment is not currently accepting members.", ct);
            return;
        }

        invitation.Accept(now, sub);

        // Materialize immediately only when the accepter IS the invitee — i.e.
        // a local users row exists for this sub AND its email matches the
        // invited email. Otherwise defer: the CurrentUserSyncService sweep
        // materializes the membership for whoever logs in with the invited
        // email (the invite is addressed to an email, not a sub). This also
        // matches the test harness, where the sync middleware provisions a
        // row by sub on every request — so row-existence alone can't be the
        // signal.
        var localEmail = await _db.Users
            .AsNoTracking()
            .Where(u => u.IdentityId == sub && u.IsActive)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        var isInvitee = localEmail != null
            && EstablishmentInvitation.Normalize(localEmail) == invitation.Email;
        if (isInvitee)
        {
            await InvitationMaterializer.MaterializeAsync(_db, invitation, sub, now, ct);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost an accept race (status flipped under us) or the membership
            // pair already exists. Either way the invite is spent.
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.InvitationAlreadyUsed,
                "This invitation has already been used.", ct);
            return;
        }

        await Send.OkAsync(new AcceptInvitationResponse(
            EstablishmentId: invitation.EstablishmentId,
            MembershipMaterialized: isInvitee), ct);
    }
}

public sealed class AcceptInvitationRequest
{
    public string Token { get; init; } = string.Empty;
}

public sealed record AcceptInvitationResponse(
    Guid EstablishmentId,
    bool MembershipMaterialized);
