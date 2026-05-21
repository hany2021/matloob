using System.Security.Claims;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Assets;

/// <summary>
/// Shared authorization logic for "can this caller see / mutate this asset?"
/// Centralized here so the metadata, download, and delete endpoints don't
/// drift.
///
/// Read grants:
///   - Public assets: anyone (no claims needed).
///   - matloob_admin: any asset.
///   - The uploader (<c>OwnerUserId</c> matches <c>sub</c>): their own asset.
///   - Establishment members: assets carrying an
///     <see cref="Asset.OwnerEstablishmentId"/> can be read by any active
///     <c>EstablishmentMember</c> of that establishment. This is what binds
///     onboarding documents (AuthorizationLetter / CommercialRegistration)
///     to the Owner / Manager / HR / etc. who logged in.
///
/// Delete grants stay narrow (owner OR admin only) — establishment-member
/// delete is intentionally deferred until we know whether non-Owner roles
/// should also be able to soft-delete shared assets.
///
/// Soft-deleted assets are filtered by the global EF query filter, so any
/// <see cref="Asset"/> reaching these methods is by definition live.
/// </summary>
internal static class AssetAccessRules
{
    public enum AccessVerdict
    {
        Allow,
        Unauthenticated,   // -> 401
        Forbidden,         // -> 403
    }

    /// <summary>
    /// Decide whether the given principal may read the asset's bytes /
    /// metadata.
    ///
    /// This overload accepts an <see cref="AppDbContext"/> so it can resolve
    /// establishment-member grants. Callers that don't have a DbContext
    /// available should use <see cref="CanReadWithoutMembership"/>, which
    /// only handles the static (Public / admin / owner) branches.
    /// </summary>
    public static async Task<AccessVerdict> CanReadAsync(
        Asset asset,
        ClaimsPrincipal? principal,
        AppDbContext db,
        CancellationToken ct)
    {
        if (asset.Visibility == AssetVisibility.Public)
        {
            return AccessVerdict.Allow;
        }

        var staticVerdict = EvaluateStaticBranches(asset, principal);
        if (staticVerdict != AccessVerdict.Forbidden)
        {
            // Allow or Unauthenticated -- both are terminal.
            return staticVerdict;
        }

        // Static branches said "forbidden" -- check establishment-member grant.
        if (asset.OwnerEstablishmentId is { } estId)
        {
            var sub = SubjectOf(principal!);
            if (!string.IsNullOrEmpty(sub))
            {
                var isMember = await db.EstablishmentMembers
                    .AsNoTracking()
                    .AnyAsync(m =>
                        m.EstablishmentId == estId &&
                        m.UserId == sub &&
                        m.IsActive,
                        ct);
                if (isMember)
                {
                    return AccessVerdict.Allow;
                }
            }
        }

        return AccessVerdict.Forbidden;
    }

    /// <summary>
    /// Decide whether the given principal may delete the asset. Today the
    /// rule is exactly the same as the static read branches: owner OR
    /// admin. Establishment-member delete is intentionally deferred.
    /// </summary>
    public static AccessVerdict CanDelete(Asset asset, ClaimsPrincipal? principal)
        => EvaluateStaticBranches(asset, principal);

    /// <summary>
    /// Static-branches-only version of CanRead. Used by code paths that
    /// don't have an <see cref="AppDbContext"/> handy and only need to test
    /// the Public / admin / owner-sub branches.
    /// </summary>
    public static AccessVerdict CanReadWithoutMembership(Asset asset, ClaimsPrincipal? principal)
    {
        if (asset.Visibility == AssetVisibility.Public)
        {
            return AccessVerdict.Allow;
        }
        return EvaluateStaticBranches(asset, principal);
    }

    private static AccessVerdict EvaluateStaticBranches(Asset asset, ClaimsPrincipal? principal)
    {
        if (principal?.Identity is null || !principal.Identity.IsAuthenticated)
        {
            return AccessVerdict.Unauthenticated;
        }

        if (IsAdmin(principal))
        {
            return AccessVerdict.Allow;
        }

        if (asset.OwnerUserId is { Length: > 0 } owner &&
            string.Equals(owner, SubjectOf(principal), StringComparison.Ordinal))
        {
            return AccessVerdict.Allow;
        }

        return AccessVerdict.Forbidden;
    }

    private static bool IsAdmin(ClaimsPrincipal principal) =>
        principal.HasClaim(c => c.Type == "role" && c.Value == "matloob_admin");

    private static string? SubjectOf(ClaimsPrincipal principal) =>
        principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
