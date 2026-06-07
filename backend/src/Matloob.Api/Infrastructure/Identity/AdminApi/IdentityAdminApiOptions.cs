namespace Matloob.Api.Infrastructure.Identity.AdminApi;

/// <summary>
/// Bound from the <c>Identity:AdminApi</c> configuration section. Carries the
/// connection details for the NEC IdM v2 STS Identity management API — the
/// same API the legacy Laravel <c>IdentityServerAdminApi</c> talked to.
///
/// Auth is a simple <c>x-api-key</c> header (no OAuth2 negotiation). The key +
/// base URL are deployment secrets: leave them blank in source and supply them
/// via env vars / user-secrets per environment. When <see cref="BaseUrl"/> is
/// blank the admin-user feature is effectively disabled (endpoints return a
/// clear 503), so the rest of the API still boots.
/// </summary>
public sealed class IdentityAdminApiOptions
{
    public const string SectionName = "Identity:AdminApi";

    /// <summary>Base URL of the IdM management API (e.g. https://idm.host). Blank = disabled.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Value sent in the <c>x-api-key</c> header.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>HTTP timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Verify the server TLS cert. False in dev when IdM uses a self-signed cert.</summary>
    public bool VerifySsl { get; set; } = true;

    /// <summary>The single Matloob-managed IdM role that gates admin SSO.</summary>
    public string AdminRole { get; set; } = "matloob_admin";

    /// <summary>
    /// Active-Directory domain suffix. A new admin identity is created with
    /// UserName = <c>{sam}@{ActiveDirectoryDomain}</c> (e.g. nec.lcl) and NO
    /// local password, which is what marks it an AD-login user in IdM.
    /// </summary>
    public string ActiveDirectoryDomain { get; set; } = "nec.lcl";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
