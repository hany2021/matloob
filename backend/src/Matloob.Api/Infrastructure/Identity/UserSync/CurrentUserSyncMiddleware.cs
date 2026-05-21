using Microsoft.AspNetCore.Http;

namespace Matloob.Api.Infrastructure.Identity.UserSync;

/// <summary>
/// Middleware that calls <see cref="ICurrentUserSyncService.EnsureCurrentUserAsync"/>
/// once per authenticated request, just after authentication has run.
/// Unauthenticated requests skip the sync entirely.
///
/// Placed AFTER <c>UseAuthentication</c> + <c>UseAuthorization</c> in the
/// pipeline so the principal is already populated. Failures inside the
/// sync service are caught + logged by the service itself; this middleware
/// never aborts the request.
/// </summary>
internal sealed class CurrentUserSyncMiddleware
{
    private readonly RequestDelegate _next;

    public CurrentUserSyncMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUserSyncService sync)
    {
        // The user identity is available after UseAuthentication has run.
        // For anonymous endpoints (Init-Data, /health, anonymous asset
        // downloads) the principal stays unauthenticated and the sync
        // service returns null without touching the DB.
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            await sync.EnsureCurrentUserAsync(context.RequestAborted);
        }

        await _next(context);
    }
}
