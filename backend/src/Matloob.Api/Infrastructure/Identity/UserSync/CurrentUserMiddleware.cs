using Matloob.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Infrastructure.Identity.UserSync;

/// <summary>
/// Runs immediately AFTER <see cref="CurrentUserSyncMiddleware"/> (which has
/// already provisioned/updated the local <c>users</c> row for the caller).
/// Resolves that row's local id by the JWT <c>sub</c> and stamps it onto
/// <see cref="ICurrentUser.MatloobUserId"/> so downstream code (and the audit
/// interceptors) can reference the Matloob user id rather than only the sub.
///
/// Read-only — it does not re-sync; ordering guarantees the row already exists.
/// </summary>
internal sealed class CurrentUserMiddleware
{
    private readonly RequestDelegate _next;

    public CurrentUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, AppDbContext db)
    {
        if (context.User?.Identity?.IsAuthenticated == true && currentUser.IsAuthenticated)
        {
            var sub = currentUser.UserId;
            if (!string.IsNullOrWhiteSpace(sub) && sub != "system")
            {
                var localId = await db.Users
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(u => u.IdentityId == sub)
                    .Select(u => (Guid?)u.Id)
                    .FirstOrDefaultAsync(context.RequestAborted);

                if (localId is Guid id)
                {
                    currentUser.SetMatloobUserId(id);
                }
            }
        }

        await _next(context);
    }
}
