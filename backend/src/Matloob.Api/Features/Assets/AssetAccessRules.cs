using System.Security.Claims;
using Matloob.Domain.Assets;

namespace Matloob.Api.Features.Assets;

/// <summary>
/// Shared authorization logic for "can this caller see / mutate this asset?"
/// Centralized here so the metadata, download, and (later) delete endpoints
/// don't drift.
///
/// Roles assumed:
///   - <c>matloob_admin</c> can always access any asset.
///   - The uploader (<c>OwnerUserId</c> matches <c>sub</c>) can always access
///     their own asset.
///   - Establishment-membership grants (asset belongs to my establishment)
///     will arrive in Phase 8 once <c>EstablishmentMember</c> exists.
///
/// Soft-deleted assets are filtered out by the global EF query filter, so
/// any <see cref="Asset"/> reaching these methods is by definition live.
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
    /// </summary>
    public static AccessVerdict CanRead(Asset asset, ClaimsPrincipal? principal)
    {
        if (asset.Visibility == AssetVisibility.Public)
        {
            return AccessVerdict.Allow;
        }

        return CanMutateInternal(asset, principal);
    }

    /// <summary>
    /// Decide whether the given principal may delete the asset. Always
    /// requires authentication; Public visibility does NOT bypass deletion
    /// the way it bypasses reads.
    /// </summary>
    public static AccessVerdict CanDelete(Asset asset, ClaimsPrincipal? principal)
        => CanMutateInternal(asset, principal);

    private static AccessVerdict CanMutateInternal(Asset asset, ClaimsPrincipal? principal)
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
