namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Centralized authorization-policy names. Reference these constants from
/// FastEndpoints' <c>Policies(...)</c> call and ASP.NET Core
/// <c>[Authorize(Policy=...)]</c> attributes — never typo'd string literals.
///
/// Policy definitions live in <see cref="AuthRegistration"/>.
/// </summary>
public static class MatloobPolicies
{
    /// <summary>
    /// Authenticated user with the <c>matloob_user</c> role.
    /// Applies to <c>/api/v1/users/*</c> routes (the worker / individual API).
    /// </summary>
    public const string User = "matloob.user";

    /// <summary>
    /// Authenticated user with the <c>matloob_admin</c> role AND an
    /// <c>aud</c> claim matching <c>IdentityOptions.AdminAudience</c>
    /// (the back-office audience). Applies to <c>/api/v1/admin/*</c>.
    /// </summary>
    public const string Admin = "matloob.admin";

    // Note: the earlier "matloob.establishment-context" policy + its
    // X-Commissioner-UUID header gate were removed during the Phase-8
    // polish pass. Phase 8 endpoints establish establishment context
    // from the URL id (loaded once per request) and use
    // MembershipChecks.IsActive{Member,Owner}Async for the active-row
    // / role check -- no header round-trip needed. If a future flow
    // needs header-driven context, reintroduce alongside the use case.
}
