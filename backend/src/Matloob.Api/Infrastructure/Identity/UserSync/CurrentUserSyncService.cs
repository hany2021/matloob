using System.Security.Claims;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Matloob.Api.Infrastructure.Identity.UserSync;

/// <summary>
/// Default <see cref="ICurrentUserSyncService"/> implementation. Reads
/// claims from the request principal, looks up the local row by IdentityId
/// (the <c>sub</c> claim), and either creates or updates it.
///
/// Optional-claim handling: if a claim isn't present in this particular
/// JWT, we leave the existing column value alone rather than clear it.
/// IdM may issue scoped-down tokens that don't carry the full profile;
/// we never want a stripped token to wipe what an earlier token recorded.
///
/// Sync is best-effort: if the DB write fails (e.g. transient
/// connection error), we log and return null — the calling endpoint
/// should NOT block on user sync. Authentication itself has already
/// succeeded by the time we get here.
/// </summary>
internal sealed class CurrentUserSyncService : ICurrentUserSyncService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _clock;
    private readonly ILogger<CurrentUserSyncService> _logger;

    public CurrentUserSyncService(
        AppDbContext db,
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider clock,
        ILogger<CurrentUserSyncService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _clock = clock;
        _logger = logger;
    }

    public async Task<User?> EnsureCurrentUserAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return null;
        }

        var sub = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(sub) || sub == "system")
        {
            return null;
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        var email = principal?.FindFirst(ClaimTypes.Email)?.Value
                 ?? principal?.FindFirst("email")?.Value;
        var name = principal?.FindFirst("name")?.Value
                ?? principal?.FindFirst(ClaimTypes.Name)?.Value
                ?? principal?.FindFirst("preferred_username")?.Value;
        var phone = principal?.FindFirst("phone_number")?.Value
                 ?? principal?.FindFirst(ClaimTypes.MobilePhone)?.Value
                 ?? principal?.FindFirst(ClaimTypes.HomePhone)?.Value;

        var now = _clock.GetUtcNow();

        try
        {
            var existing = await _db.Users
                .FirstOrDefaultAsync(u => u.IdentityId == sub, ct);

            if (existing is null)
            {
                var fresh = User.CreateFromIdentity(
                    id: Guid.NewGuid(),
                    identityId: sub,
                    email: email,
                    name: name,
                    phone: phone,
                    firstSeenAt: now);
                _db.Users.Add(fresh);
                await _db.SaveChangesAsync(ct);
                return fresh;
            }

            existing.SyncFromIdentity(email, name, phone, now);
            await _db.SaveChangesAsync(ct);
            return existing;
        }
        catch (Exception ex)
        {
            // Sync is best-effort. A transient DB blip should not break
            // the endpoint -- auth itself already succeeded.
            _logger.LogWarning(ex,
                "Failed to sync local user row for sub={Sub}. Continuing without update.",
                sub);
            return null;
        }
    }
}
