using FastEndpoints;
using Matloob.Api.Features.Establishments.Members.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Invitations.Preview;

/// <summary>
/// <c>GET /api/v1/invitations/preview?token=...</c> (+ legacy
/// <c>/api/invitations/preview</c>). Anonymous: the public invite landing page
/// calls this before the invitee has logged in.
///
/// Returns 200 with the establishment name, role, inviter name and expiry for a
/// valid Pending invite whose establishment is Approved. Anything else
/// (unknown / revoked / accepted / expired token, or non-Approved
/// establishment) returns 404 — we don't leak which case it was.
/// </summary>
public sealed class PreviewInvitationEndpoint : EndpointWithoutRequest<PreviewInvitationResponse>
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public PreviewInvitationEndpoint(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public override void Configure()
    {
        Get("/api/invitations/preview", "/api/v1/invitations/preview");
        AllowAnonymous();
        Description(b => b
            .Produces<PreviewInvitationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Invitations"));
        Summary(s => s.Summary = "Public preview of an invitation by raw token.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = Query<string>("token", isRequired: false);
        if (string.IsNullOrWhiteSpace(token))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var hash = InvitationSupport.Hash(token);
        var invitation = await _db.EstablishmentInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        var now = _clock.GetUtcNow();
        if (invitation is null || !invitation.IsAcceptable(now))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == invitation.EstablishmentId, ct);
        if (establishment is null || establishment.Status != EstablishmentStatus.Approved)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var inviterName = await _db.Users
            .AsNoTracking()
            .Where(u => u.IdentityId == invitation.InvitedByUserId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct);

        await Send.OkAsync(new PreviewInvitationResponse(
            EstablishmentName: establishment.Name,
            Role: invitation.Role,
            InviterName: inviterName,
            ExpiresAt: invitation.ExpiresAt), ct);
    }
}

public sealed record PreviewInvitationResponse(
    string EstablishmentName,
    EstablishmentMemberRole Role,
    string? InviterName,
    DateTimeOffset ExpiresAt);
