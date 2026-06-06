using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Members.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Notifications;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Members.ResendInvitation;

/// <summary>
/// <c>POST /api/establishments/me/invitations/{invitationId}/resend</c> (legacy) +
/// <c>POST /api/v1/establishments/{establishmentId}/members/invitations/{invitationId}/resend</c>.
///
/// Owner-only (<c>members.manage</c>), establishment must be Approved. Issues a
/// fresh token + 7-day expiry on a still-Pending invitation and re-fires the
/// invite email (the previous token is invalidated by overwriting the hash).
/// 204 on success; 409 <c>invitation_already_used</c> if not Pending.
/// </summary>
public sealed class ResendInvitationEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public ResendInvitationEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider clock,
        IEmailSender email,
        IConfiguration config)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _email = email;
        _config = config;
    }

    public override void Configure()
    {
        Post("/api/establishments/me/invitations/{invitationId}/resend",
             "/api/v1/establishments/{establishmentId}/members/invitations/{invitationId}/resend");
        // Authentication only; Owner (members.manage) or admin enforced inside.
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s => s.Summary = "Re-issue and re-send a pending invitation.");
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
                $"Cannot resend an invitation in status '{invitation.Status}'.", ct);
            return;
        }

        var now = _clock.GetUtcNow();
        var rawToken = InvitationSupport.NewRawToken();
        invitation.Resend(InvitationSupport.Hash(rawToken), now, now.AddDays(7));
        await _db.SaveChangesAsync(ct);

        var baseUrl = (_config["Notifications:InviteBaseUrl"]
                       ?? "http://localhost:3001/ar/invite").TrimEnd('/');
        await _email.SendInviteAsync(
            invitation.Email, establishment.Name, $"{baseUrl}/{rawToken}", invitation.Role, ct);

        await Send.NoContentAsync(ct);
    }
}
