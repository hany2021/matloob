using Matloob.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Authorization requirement added to <see cref="MatloobPolicies.Admin"/>:
/// the caller's local <c>users</c> row must be active (and not soft-deleted).
/// Mirrors the legacy Filament <c>Admin::canAccessPanel</c>, which gated panel
/// access on <c>is_active</c> — so a deactivated admin loses access even though
/// the JWT still carries the <c>matloob_admin</c> role.
/// </summary>
public sealed class ActiveAdminRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Handler for <see cref="ActiveAdminRequirement"/>. Looks up the caller by the
/// <c>sub</c> claim and fails authorization when their row is explicitly
/// inactive or soft-deleted. Registered scoped so it can use the request's
/// <see cref="AppDbContext"/>.
///
/// Fail-open when no row exists yet: authorization runs before the
/// CurrentUserSync middleware provisions the row, so a brand-new principal has
/// no row on their very first request — they can't be "deactivated" before they
/// exist, and the row is created moments later.
/// </summary>
public sealed class ActiveAdminHandler : AuthorizationHandler<ActiveAdminRequirement>
{
    private readonly AppDbContext _db;

    public ActiveAdminHandler(AppDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveAdminRequirement requirement)
    {
        var sub = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(sub))
        {
            // No subject — other requirements (RequireAuthenticatedUser) handle this.
            return;
        }

        var row = await _db.Users
            .AsNoTracking()
            .IgnoreQueryFilters() // also see soft-deleted rows
            .Where(u => u.IdentityId == sub)
            .Select(u => new { u.IsActive, u.IsDeleted })
            .FirstOrDefaultAsync();

        // Deactivated or soft-deleted admin → deny (do not Succeed; Fail to be
        // explicit even if other handlers for this requirement run).
        if (row is not null && (!row.IsActive || row.IsDeleted))
        {
            context.Fail(new AuthorizationFailureReason(this, "Admin account is inactive."));
            return;
        }

        // Active row, or no row yet (first request) → allow this requirement.
        context.Succeed(requirement);
    }
}
