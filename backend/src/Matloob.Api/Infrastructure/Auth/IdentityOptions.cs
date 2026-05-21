namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Bound from the <c>Identity</c> configuration section. Carries the values
/// needed to validate JWTs issued by NEC IdentityServer.
///
/// Two URLs intentionally split:
///   <see cref="Authority"/> is the address the API reaches for OIDC discovery
///   and JWKS — must be reachable from inside the API process (e.g. the Docker
///   container hits <c>host.docker.internal:44310</c>).
///   <see cref="Issuer"/> is the literal string in the JWT's <c>iss</c> claim,
///   which IdM pins to the hostname of its first incoming request
///   (typically <c>https://localhost:44310</c>). The two values diverge in
///   local dev and converge in production.
/// </summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>Base URL where the API fetches OIDC discovery and JWKS.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Expected <c>iss</c> claim value inside JWTs.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Required <c>aud</c> claim for tokens used by /api/v1/users/* and /api/v1/establishments/*.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Required <c>aud</c> claim for tokens used by /api/v1/admin/*.</summary>
    public string AdminAudience { get; set; } = string.Empty;

    /// <summary>
    /// Whether OIDC discovery requires HTTPS. <c>false</c> in dev when IdM runs
    /// on <c>http://</c> or with a self-signed cert.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Which JWT claim carries roles. NEC IdM emits roles as the <c>role</c> claim.</summary>
    public string RoleClaimType { get; set; } = "role";

    /// <summary>Which JWT claim becomes <c>HttpContext.User.Identity.Name</c>. IdM puts the subject id in <c>sub</c>.</summary>
    public string NameClaimType { get; set; } = "sub";
}
