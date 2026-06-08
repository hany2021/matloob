using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Establishments.Members.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Notifications;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Members.InviteMember;

/// <summary>
/// <c>POST /api/establishments/me/invitations</c> (legacy, context-resolved) +
/// <c>POST /api/v1/establishments/{establishmentId}/members/invitations</c>.
///
/// Owner-only (<c>members.manage</c>), establishment must be Approved. Issues a
/// pending invitation for an email + role, hashes the raw token (never stored),
/// and fires the invite email via <see cref="IEmailSender"/>. The raw token is
/// returned to no one — it only exists inside the invite URL e-mailed out.
///
/// 409 <c>email_already_member</c> (email already an active member),
/// 409 <c>invitation_already_pending</c> (a Pending invite exists),
/// 400 <c>invitation_role_not_allowed</c> (Owner cannot be invited).
/// </summary>
public sealed class InviteMemberEndpoint : Endpoint<InviteMemberRequest, InvitationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public InviteMemberEndpoint(
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
        Post("/api/establishments/me/invitations",
             "/api/v1/establishments/{establishmentId}/members/invitations");
        // Authentication only; Owner (members.manage) or admin is enforced
        // inside via InvitationSupport (mirrors AddMember / ListMembers).
        Description(b => b
            .Produces<InvitationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishments"));
        Summary(s => s.Summary = "Invite someone to the establishment by email.");
    }

    public override async Task HandleAsync(InviteMemberRequest req, CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishment = await InvitationSupport.ResolveApprovedForManageAsync(_db, HttpContext, sub, ct);
        if (establishment is null) return;

        var email = EstablishmentInvitation.Normalize(req.Email);
        if (email.Length == 0 || !email.Contains('@'))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status400BadRequest,
                "invalid_email", "A valid email address is required.", ct);
            return;
        }

        // Only the AdminReview approval flow grants Owner; the invite picker
        // excludes it.
        if (req.Role == EstablishmentMemberRole.Owner)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status400BadRequest,
                EstablishmentErrorCodes.InvitationRoleNotAllowed,
                "Owner cannot be assigned via invitation.", ct);
            return;
        }

        // Already an active member? (member sub -> local users row -> email)
        var alreadyMember = await _db.EstablishmentMembers
            .AsNoTracking()
            .Where(m => m.EstablishmentId == establishment.Id && m.IsActive)
            .Join(_db.Users.AsNoTracking(),
                m => m.UserId, u => u.IdentityId,
                (m, u) => u.Email)
            .AnyAsync(e => e != null && e.ToLower() == email, ct);
        if (alreadyMember)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.EmailAlreadyMember,
                "This email already belongs to an active member.", ct);
            return;
        }

        var pendingExists = await _db.EstablishmentInvitations
            .AsNoTracking()
            .AnyAsync(i => i.EstablishmentId == establishment.Id
                        && i.Email == email
                        && i.Status == EstablishmentInvitationStatus.Pending, ct);
        if (pendingExists)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.InvitationAlreadyPending,
                "A pending invitation already exists for this email.", ct);
            return;
        }

        var now = _clock.GetUtcNow();
        var rawToken = InvitationSupport.NewRawToken();
        var invitation = new EstablishmentInvitation(
            id: Guid.NewGuid(),
            establishmentId: establishment.Id,
            email: email,
            role: req.Role,
            tokenHash: InvitationSupport.Hash(rawToken),
            invitedByUserId: sub,
            invitedAt: now,
            expiresAt: now.AddDays(7));
        _db.EstablishmentInvitations.Add(invitation);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Raced past the application-side pending check and hit
            // ux_invitations_one_pending_per_email_per_est (or the token uq).
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.InvitationAlreadyPending,
                "A pending invitation already exists for this email.", ct);
            return;
        }

        var baseUrl = (_config["Notifications:InviteBaseUrl"]
                       ?? "http://localhost:3001/ar/invite").TrimEnd('/');
        await _email.SendInviteAsync(email, establishment.Name, $"{baseUrl}/{rawToken}", req.Role, ct);

        await Send.ResponseAsync(InvitationResponse.From(invitation), StatusCodes.Status201Created, ct);
    }
}

public sealed class InviteMemberRequest
{
    public string Email { get; init; } = string.Empty;
    public EstablishmentMemberRole Role { get; init; } = EstablishmentMemberRole.Other;
}

/// <summary>List/create shape for an invitation. Never carries the raw token.</summary>
public sealed record InvitationResponse(
    Guid Id,
    string Email,
    EstablishmentMemberRole Role,
    EstablishmentInvitationStatus Status,
    DateTimeOffset InvitedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt)
{
    public static InvitationResponse From(EstablishmentInvitation i) => new(
        i.Id, i.Email, i.Role, i.Status, i.InvitedAt, i.ExpiresAt, i.AcceptedAt);
}
