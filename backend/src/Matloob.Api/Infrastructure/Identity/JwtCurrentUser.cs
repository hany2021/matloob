using System.Security.Claims;

namespace Matloob.Api.Infrastructure.Identity;

/// <summary>
/// Reads "who is making this request" from the authenticated HttpContext, with
/// a safe fallback to <c>"system"</c> whenever no HTTP request is in flight
/// (EF migrations / design-time host builds, background jobs, container
/// initializers) or the request is unauthenticated.
///
/// Singleton-safe: <see cref="IHttpContextAccessor"/> is itself a singleton
/// that resolves the per-request HttpContext from AsyncLocal storage.
///
/// The auditing interceptor depends on <see cref="ICurrentUser"/> ONLY —
/// no class outside <c>Infrastructure/Identity</c> should touch HttpContext
/// or claims directly.
/// </summary>
internal sealed class JwtCurrentUser : ICurrentUser
{
    private const string SystemUserId = "system";
    private const string UnknownAuthenticatedUserId = "unknown-authenticated-user";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity is null || !user.Identity.IsAuthenticated)
            {
                return SystemUserId;
            }

            // `sub` is the OAuth/OIDC subject id. AuthRegistration sets
            // NameClaimType="sub" so Identity.Name normally equals it; we still
            // check the raw claim defensively in case a custom ClaimsTransformer
            // changes the picture later.
            var sub = user.FindFirst("sub")?.Value
                   ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? user.Identity.Name;

            return string.IsNullOrEmpty(sub) ? UnknownAuthenticatedUserId : sub;
        }
    }

    /// <summary>Local users.id, set per-request by CurrentUserMiddleware after sync.</summary>
    public Guid? MatloobUserId { get; private set; }

    public void SetMatloobUserId(Guid matloobUserId) => MatloobUserId = matloobUserId;

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
}
