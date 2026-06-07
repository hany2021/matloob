namespace Matloob.Api.Features.Admins.Common;

/// <summary>
/// Admin email-domain allow-list. Based on the legacy <c>ValidAdminEmailDomain</c>
/// rule, narrowed to NEC only: an admin's email must sit on the nec.gov.sa domain.
/// </summary>
internal static class AdminEmailDomain
{
    private static readonly string[] Allowed = { "nec.gov.sa" };

    public static bool IsAllowed(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return false;
        var domain = email[(at + 1)..].Trim().ToLowerInvariant();
        return Array.IndexOf(Allowed, domain) >= 0;
    }

    /// <summary>Human-readable list for the 422 message (e.g. "takamol.sa، @nec.gov.sa").</summary>
    public static string AllowedDisplay => string.Join("، @", Allowed);
}
