using Matloob.Domain.Establishments;

namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Static map from <see cref="EstablishmentMemberRole"/> to the set of
/// <see cref="Permissions"/> slugs that role grants. This is the single source
/// of truth for "what can a Manager / HR / Commissioner do?" — both the
/// server-side gates (<c>MembershipChecks.HasPermissionAsync</c>) and the
/// client-surfaced <c>permissions[]</c> projections read it.
///
/// <see cref="EstablishmentMemberRole.Owner"/> is implicit-all: it is not in
/// the map; <see cref="PermissionsFor"/> returns the full <see cref="Permissions.All"/>
/// union for it, and <c>HasPermissionAsync</c> short-circuits to <c>true</c>.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<EstablishmentMemberRole, IReadOnlySet<string>> Map =
        new Dictionary<EstablishmentMemberRole, IReadOnlySet<string>>
        {
            [EstablishmentMemberRole.Manager] = new HashSet<string>(StringComparer.Ordinal)
            {
                Permissions.Events.Create,
                Permissions.Events.Manage,
                Permissions.Opportunities.Create,
                Permissions.Opportunities.Manage,
                Permissions.Applications.Read,
                Permissions.Offers.Send,
                Permissions.Offers.Respond,
                Permissions.Evaluations.Create,
                Permissions.ChangeRequests.Submit,
            },

            [EstablishmentMemberRole.HR] = new HashSet<string>(StringComparer.Ordinal)
            {
                Permissions.Applications.Read,
                Permissions.Offers.Send,
                Permissions.Offers.Respond,
                Permissions.Evaluations.Create,
            },

            // Accountant holds nothing today; finance.edit is reserved but no
            // endpoint gates on it yet (see the plan's "out of scope").
            [EstablishmentMemberRole.Accountant] = None,

            [EstablishmentMemberRole.Commissioner] = new HashSet<string>(StringComparer.Ordinal)
            {
                Permissions.Offers.Send,
                Permissions.Offers.Respond,
            },

            // Read-only viewer.
            [EstablishmentMemberRole.Other] = None,
        };

    /// <summary>
    /// The permission set for a role. Owner gets the full <see cref="Permissions.All"/>
    /// union; every other role gets its mapped set (empty for Accountant / Other).
    /// </summary>
    public static IReadOnlySet<string> PermissionsFor(EstablishmentMemberRole role) =>
        role == EstablishmentMemberRole.Owner
            ? Permissions.All
            : Map.TryGetValue(role, out var set) ? set : None;
}
