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

    /// <summary>
    /// Authenticated user with the <c>matloob_user</c> role + an
    /// <c>X-Commissioner-UUID</c> request header. Applies to
    /// <c>/api/v1/establishments/*</c>.
    ///
    /// IMPORTANT: this policy currently only validates that the header is
    /// present. Once the EstablishmentMember entity exists (Phase 8), the
    /// handler MUST also verify:
    ///   - the header value is a valid Guid
    ///   - an active EstablishmentMember row exists for (UserId, EstablishmentId)
    ///   - the Establishment.Status is Approved (or, for read endpoints, Suspended)
    /// Until then, no business endpoint uses this policy.
    /// </summary>
    public const string EstablishmentContext = "matloob.establishment-context";
}
