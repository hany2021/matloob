using System.Security.Claims;
using Matloob.Api.Features.Establishments.Members.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
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
                await MaterializeAcceptedInvitationsAsync(sub, email, now, ct);
                return fresh;
            }

            existing.SyncFromIdentity(email, name, phone, now);
            await _db.SaveChangesAsync(ct);
            await MaterializeAcceptedInvitationsAsync(sub, email, now, ct);
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

    /// <summary>
    /// Deferred-materialization sweep: the single "this user just appeared"
    /// hook. After the local users row is upserted for this sub, any
    /// invitations Accepted for this user's email but not yet materialized
    /// (the invitee accepted before they had a local row) become
    /// <see cref="EstablishmentMember"/> rows — but only for establishments
    /// that are currently Approved (a Suspended establishment's deferred
    /// accepts wait until it is reinstated, then materialize on a later run).
    /// </summary>
    private async Task MaterializeAcceptedInvitationsAsync(
        string sub, string? email, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var normalizedEmail = EstablishmentInvitation.Normalize(email);

        var pending = await _db.EstablishmentInvitations
            .Where(i => i.Email == normalizedEmail
                     && i.Status == EstablishmentInvitationStatus.Accepted
                     && i.MaterializedMemberId == null)
            .ToListAsync(ct);
        if (pending.Count == 0)
        {
            return;
        }

        var materializedAny = false;
        foreach (var invitation in pending)
        {
            var approved = await _db.Establishments
                .AsNoTracking()
                .AnyAsync(e => e.Id == invitation.EstablishmentId
                            && e.Status == EstablishmentStatus.Approved, ct);
            if (!approved)
            {
                continue; // wait for reinstatement.
            }

            await InvitationMaterializer.MaterializeAsync(_db, invitation, sub, now, ct);
            materializedAny = true;
        }

        if (materializedAny)
        {
            await _db.SaveChangesAsync(ct);
        }
    }
}
