using FastEndpoints;
using Matloob.Api.Features.Establishments.Members.Common;
using Matloob.Api.Features.Establishments.Members.InviteMember;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Members.ListInvitations;

/// <summary>
/// <c>GET /api/establishments/me/invitations</c> (legacy, context-resolved) +
/// <c>GET /api/v1/establishments/{establishmentId}/members/invitations</c>.
///
/// Owner-only (<c>members.manage</c>), establishment must be Approved. Returns
/// Pending invitations by default; <c>?include=accepted</c> also returns
/// Accepted ones (so the Owner can see who has joined via invite). Revoked /
/// Expired are excluded.
/// </summary>
public sealed class ListInvitationsEndpoint : EndpointWithoutRequest<IReadOnlyList<InvitationResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListInvitationsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/establishments/me/invitations",
            "/api/v1/establishments/{establishmentId}/members/invitations");
        // Authentication only; Owner (members.manage) or admin enforced inside.
        Description(b => b
            .Produces<IReadOnlyList<InvitationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s => s.Summary = "List the establishment's invitations.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishment = await InvitationSupport.ResolveApprovedForManageAsync(
            _db, HttpContext, _currentUser.UserId, ct);
        if (establishment is null) return;

        var includeAccepted = string.Equals(
            Query<string>("include", isRequired: false), "accepted", StringComparison.OrdinalIgnoreCase);

        var rows = await _db.EstablishmentInvitations
            .AsNoTracking()
            .Where(i => i.EstablishmentId == establishment.Id
                     && (i.Status == EstablishmentInvitationStatus.Pending
                      || (includeAccepted && i.Status == EstablishmentInvitationStatus.Accepted)))
            .OrderByDescending(i => i.InvitedAt)
            .ThenBy(i => i.Id)
            .ToListAsync(ct);

        await Send.OkAsync(rows.Select(InvitationResponse.From).ToList(), ct);
    }
}
