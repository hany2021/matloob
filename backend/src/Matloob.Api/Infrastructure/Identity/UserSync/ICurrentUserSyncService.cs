using Matloob.Domain.Users;

namespace Matloob.Api.Infrastructure.Identity.UserSync;

/// <summary>
/// Ensures a local <see cref="User"/> row exists for the authenticated
/// caller and is up-to-date with their JWT claims.
///
/// Endpoints / middleware call <see cref="EnsureCurrentUserAsync"/> on
/// every authenticated request. The first call provisions the row; later
/// calls update Email / Name / Phone (if claims carry them) and bump
/// LastSeenAt. Unauthenticated calls return null without touching the DB.
///
/// The service is request-scoped and holds no state; it reads the current
/// principal from <see cref="ICurrentUser"/> + <see cref="IHttpContextAccessor"/>
/// and writes through the request's <see cref="Matloob.Api.Infrastructure.Persistence.AppDbContext"/>.
/// </summary>
public interface ICurrentUserSyncService
{
    /// <summary>
    /// Resolve (and create-or-update) the local user row matching the
    /// current request's authenticated principal. Returns null if the
    /// caller isn't authenticated; otherwise the persisted User row.
    /// </summary>
    Task<User?> EnsureCurrentUserAsync(CancellationToken cancellationToken);
}
