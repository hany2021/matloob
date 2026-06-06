using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Members.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Members.RevokeInvitation;

/// <summary>
/// <c>DELETE /api/establishments/me/invitations/{invitationId}</c> (legacy) +
/// <c>DELETE /api/v1/establishments/{establishmentId}/members/invitations/{invitationId}</c>.
///
/// Owner-only (<c>members.manage</c>), establishment must be Approved. Sets the
/// invitation status to Revoked (soft state change, the row is kept). Only a
/// Pending invite can be revoked; an already-accepted/revoked/expired invite
/// returns 409 <c>invitation_already_used</c>. 204 on success.
/// </summary>
public sealed class RevokeInvitationEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public RevokeInvitationEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Delete("/api/establishments/me/invitations/{invitationId}",
               "/api/v1/establishments/{establishmentId}/members/invitations/{invitationId}");
        // Authentication only; Owner (members.manage) or admin enforced inside.
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s => s.Summary = "Revoke a pending invitation.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishment = await InvitationSupport.ResolveApprovedForManageAsync(
            _db, HttpContext, _currentUser.UserId, ct);
        if (establishment is null) return;

        var invitationId = Route<Guid>("invitationId");
        var invitation = await _db.EstablishmentInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.EstablishmentId == establishment.Id, ct);
        if (invitation is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (invitation.Status != EstablishmentInvitationStatus.Pending)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.InvitationAlreadyUsed,
                $"Cannot revoke an invitation in status '{invitation.Status}'.", ct);
            return;
        }

        invitation.Revoke();
        await _db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
