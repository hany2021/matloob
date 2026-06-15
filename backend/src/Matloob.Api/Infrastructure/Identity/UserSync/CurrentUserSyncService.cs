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
        var name = principal?.FindFirst("given_name")?.Value
                ?? principal?.FindFirst(ClaimTypes.GivenName)?.Value
                ?? principal?.FindFirst("name")?.Value
                ?? principal?.FindFirst(ClaimTypes.Name)?.Value
                ?? principal?.FindFirst("preferred_username")?.Value;
        var phone = principal?.FindFirst("phone_number")?.Value
                 ?? principal?.FindFirst(ClaimTypes.MobilePhone)?.Value
                 ?? principal?.FindFirst(ClaimTypes.HomePhone)?.Value;
        var dateOfBirth = ParseBirthdate(
            principal?.FindFirst("birthdate")?.Value
            ?? principal?.FindFirst(ClaimTypes.DateOfBirth)?.Value);

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
                if (dateOfBirth is not null)
                {
                    fresh.SetIdentityAttributes(null, null, CalculateAge(dateOfBirth.Value, now), dateOfBirth, null);
                }
                _db.Users.Add(fresh);
                await _db.SaveChangesAsync(ct);
                await MaterializeAcceptedInvitationsAsync(sub, email, now, ct);
                return fresh;
            }

            existing.SyncFromIdentity(email, name, phone, now);
            if (dateOfBirth is not null)
            {
                existing.SetIdentityAttributes(null, null, CalculateAge(dateOfBirth.Value, now), dateOfBirth, null);
            }
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
    /// Parse the OIDC <c>birthdate</c> claim. Per OIDC Core §5.1 it is a string
    /// in one of three shapes: <c>YYYY-MM-DD</c>, <c>YYYY</c> (year only), or
    /// <c>0000-MM-DD</c> (year hidden). Any other shape (localized format,
    /// Hijri, empty) returns null rather than throwing — sync is best-effort
    /// and an unparseable claim must not break the request.
    /// </summary>
    private static DateOnly? ParseBirthdate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var iso) && iso.Year >= 1) return iso;
        if (DateOnly.TryParseExact(raw, "yyyy", out var yearOnly)) return yearOnly;
        return null;
    }

    /// <summary>
    /// Whole-years age as of <paramref name="asOf"/>, adjusted so the user
    /// hasn't "turned" the new age until their birthday has passed this year.
    /// Returns null for a future birthdate (clock skew / bad data).
    /// </summary>
    private static int? CalculateAge(DateOnly dateOfBirth, DateTimeOffset asOf)
    {
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        if (dateOfBirth > today) return null;
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age)) age--;
        return age;
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
