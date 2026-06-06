using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Members.Common;

/// <summary>
/// Turns an Accepted <see cref="EstablishmentInvitation"/> into an active
/// <see cref="EstablishmentMember"/> for a given user sub, idempotently. Shared
/// by the Accept endpoint (immediate path — the invitee already had a local
/// users row) and the CurrentUserSyncService sweep (deferred path — the row
/// appears on the invitee's first authenticated request).
///
/// Stages the changes on the tracked context; the caller owns SaveChanges.
/// </summary>
internal static class InvitationMaterializer
{
    public static async Task MaterializeAsync(
        AppDbContext db,
        EstablishmentInvitation invitation,
        string sub,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await db.EstablishmentMembers
            .FirstOrDefaultAsync(m => m.EstablishmentId == invitation.EstablishmentId
                                   && m.UserId == sub, ct);

        Guid memberId;
        if (existing is null)
        {
            var member = new EstablishmentMember(
                id: Guid.NewGuid(),
                establishmentId: invitation.EstablishmentId,
                userId: sub,
                role: invitation.Role,
                addedByUserId: invitation.InvitedByUserId,
                addedAt: now,
                isActive: true);
            db.EstablishmentMembers.Add(member);
            memberId = member.Id;
        }
        else
        {
            // Already a member (e.g. re-invited then accepted): keep the row,
            // just make sure it's active and bind the invite to it.
            if (!existing.IsActive)
            {
                existing.Reactivate();
            }
            memberId = existing.Id;
        }

        invitation.SetMaterializedMember(memberId);
    }
}
