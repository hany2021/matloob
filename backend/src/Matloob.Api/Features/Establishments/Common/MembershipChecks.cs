using System.Security.Claims;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Shared "what can this caller do to this establishment?" lookups.
///
/// The two questions every member endpoint needs are:
/// - is the caller an admin?
/// - does the caller hold an active <see cref="EstablishmentMember"/> row
///   for the establishment, optionally with a specific role?
///
/// Both rules are pulled into one place so the four CRUD endpoints (list /
/// add / update / remove) and the asset-download rule can't drift.
/// </summary>
internal static class MembershipChecks
{
    public static bool IsAdmin(ClaimsPrincipal? principal) =>
        principal?.HasClaim(c => c.Type == "role" && c.Value == "matloob_admin") == true;

    /// <summary>
    /// True if the caller has ANY active membership row on the establishment.
    /// Used by the list-members endpoint and (later) asset-download.
    /// </summary>
    public static Task<bool> IsActiveMemberAsync(
        AppDbContext db,
        Guid establishmentId,
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(false);
        }

        return db.EstablishmentMembers
            .AsNoTracking()
            .AnyAsync(m =>
                m.EstablishmentId == establishmentId &&
                m.UserId == userId &&
                m.IsActive,
                ct);
    }

    /// <summary>
    /// True if the caller has an active <see cref="EstablishmentMemberRole.Owner"/>
    /// row on the establishment. Used by add / update / remove endpoints.
    /// </summary>
    public static Task<bool> IsActiveOwnerAsync(
        AppDbContext db,
        Guid establishmentId,
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(false);
        }

        return db.EstablishmentMembers
            .AsNoTracking()
            .AnyAsync(m =>
                m.EstablishmentId == establishmentId &&
                m.UserId == userId &&
                m.Role == EstablishmentMemberRole.Owner &&
                m.IsActive,
                ct);
    }

    /// <summary>
    /// True if removing/demoting/deactivating <paramref name="member"/> would
    /// leave the establishment with zero active Owners. The four CRUD
    /// endpoints call this before persisting any change that touches an
    /// Owner row.
    /// </summary>
    public static async Task<bool> WouldDropLastOwnerAsync(
        AppDbContext db,
        EstablishmentMember member,
        CancellationToken ct)
    {
        if (member.Role != EstablishmentMemberRole.Owner || !member.IsActive)
        {
            return false; // touching a non-Owner / inactive row can't drop the count.
        }

        var otherActiveOwners = await db.EstablishmentMembers
            .AsNoTracking()
            .CountAsync(m =>
                m.EstablishmentId == member.EstablishmentId &&
                m.Id != member.Id &&
                m.Role == EstablishmentMemberRole.Owner &&
                m.IsActive,
                ct);

        return otherActiveOwners == 0;
    }
}
