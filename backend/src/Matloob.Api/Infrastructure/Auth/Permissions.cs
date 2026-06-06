namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Flat permission slugs gating establishment write surfaces. Each slug names
/// one capability an active member's role may or may not grant; the role →
/// permission map lives in <see cref="RolePermissions"/>.
///
/// Reference these constants from <c>MembershipChecks.HasPermissionAsync</c>
/// call sites instead of typing the string literal — the value IS the wire
/// slug surfaced to the Next.js client (auth-context <c>permissions[]</c>),
/// so it must stay byte-stable.
///
/// Reads are NOT gated by a slug — every active member can read the
/// establishment's events / opportunities / offers / applications /
/// evaluations (gated by <c>IsActiveMemberAsync</c>). These slugs cover
/// create / edit / send / respond surfaces only.
/// </summary>
public static class Permissions
{
    public static class Profile
    {
        public const string Edit = "profile.edit";
    }

    public static class Events
    {
        public const string Create = "events.create";
        public const string Manage = "events.manage";
    }

    public static class Opportunities
    {
        public const string Create = "opportunities.create";
        public const string Manage = "opportunities.manage";
    }

    public static class Applications
    {
        public const string Read = "applications.read";
    }

    public static class Offers
    {
        public const string Send = "offers.send";
        public const string Respond = "offers.respond";
    }

    public static class Evaluations
    {
        public const string Create = "evaluations.create";
    }

    public static class ChangeRequests
    {
        public const string Submit = "change_requests.submit";
    }

    public static class Members
    {
        public const string Manage = "members.manage";
    }

    public static class Finance
    {
        /// <summary>
        /// Reserved for the Accountant write surface (bank / finance editing).
        /// No endpoint gates on it today — see the plan's "out of scope".
        /// </summary>
        public const string Edit = "finance.edit";
    }

    /// <summary>
    /// Every defined slug. This is the set an Owner implicitly holds — used by
    /// <see cref="RolePermissions.PermissionsFor"/> to materialize the Owner's
    /// union and by the read projections that surface <c>permissions[]</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Profile.Edit,
        Events.Create,
        Events.Manage,
        Opportunities.Create,
        Opportunities.Manage,
        Applications.Read,
        Offers.Send,
        Offers.Respond,
        Evaluations.Create,
        ChangeRequests.Submit,
        Members.Manage,
        Finance.Edit,
    };
}
