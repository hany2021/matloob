using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// Local application-side cache of an IdentityServer user. Matloob does NOT
/// own authentication — IdM does — but several application flows need a
/// stable local row keyed by the IdM <c>sub</c> claim:
///   - validating that a userId on AddMember resolves to a real user
///     (spec §6.3: 422 user_not_found_in_system),
///   - storing profile fields (email / name / phone) that the public
///     frontend reads without re-querying IdM,
///   - capturing first-seen / last-seen timestamps for ops.
///
/// The row is created or updated by <c>ICurrentUserSyncService</c> on
/// authenticated requests; nothing else writes to it today.
///
/// Inherits <see cref="BaseAuditableEntity{TId}"/> so the audit columns +
/// soft-delete are wired (a future "user removed by IdM" flow can flip
/// IsActive=false or soft-delete; both options exist).
/// </summary>
public sealed class User : BaseAuditableEntity<Guid>, IAggregateRoot
{
    /// <summary>
    /// <c>sub</c> claim value from the IdM JWT. The system-of-record id for
    /// the user. Unique + indexed.
    /// </summary>
    public string IdentityId { get; private set; } = string.Empty;

    public string? Email { get; private set; }
    public string? Name { get; private set; }
    public string? Phone { get; private set; }

    /// <summary>
    /// True for normal users. Set to false to block them from the system
    /// without deleting the row (preserves member references). A
    /// soft-deleted row is hidden from queries entirely; an IsActive=false
    /// row is still discoverable.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Updated by the sync service every time the user makes an
    /// authenticated request. Lets ops see who's actually using the
    /// system without scraping the audit log.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    /// <summary>
    /// True once the user has finished the public-frontend onboarding wizard.
    /// Mirrors the old Laravel <c>users.onboarded</c> flag. Set by
    /// <c>PATCH /api/users/profile/finish-onboarding</c>. The flag is
    /// surfaced on <c>GET /api/users/profile</c> so the frontend's
    /// onboarding guard can read it back.
    /// </summary>
    public bool Onboarded { get; private set; }

    // ---- Personal-info profile fields (migrated from Laravel users table) ----

    /// <summary>National ID / Iqama number. Read-only from the profile UI today
    /// (sourced from IdM); kept here for response parity.</summary>
    public string? IdNumber { get; private set; }

    public Gender? Gender { get; private set; }
    public int? Age { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public string? Bio { get; private set; }

    /// <summary>Secondary contact number (legacy <c>additional_phone_number</c>).</summary>
    public string? AdditionalPhone { get; private set; }

    /// <summary>Total years of experience. Legacy column defaulted to 0.</summary>
    public int YearsOfExperience { get; private set; }

    /// <summary>True once all four profile sections are filled. Recomputed by
    /// the application layer after each profile mutation.</summary>
    public bool ProfileCompleted { get; private set; }

    // Reference FKs (nullable until the user fills personal-info).
    public Guid? CityId { get; private set; }
    public Guid? RegionId { get; private set; }
    public Guid? NationalityId { get; private set; }

    /// <summary>Profile photo via the Asset GUID flow (replaces Spatie media).</summary>
    public Guid? PhotoAssetId { get; private set; }

    private User() { }

    /// <summary>
    /// Provisions a new local user row from JWT claims. Called by the sync
    /// service the first time an authenticated principal hits the API.
    /// </summary>
    public static User CreateFromIdentity(
        Guid id,
        string identityId,
        string? email,
        string? name,
        string? phone,
        DateTimeOffset firstSeenAt)
    {
        if (string.IsNullOrWhiteSpace(identityId))
        {
            throw new ArgumentException("IdentityId is required.", nameof(identityId));
        }

        return new User
        {
            Id = id,
            IdentityId = identityId,
            Email = Normalize(email),
            Name = Normalize(name),
            Phone = Normalize(phone),
            IsActive = true,
            LastSeenAt = firstSeenAt,
        };
    }

    /// <summary>
    /// Merge claims from the current request onto the existing row. Optional
    /// claims that arrive null are NOT cleared — IdM doesn't always carry
    /// the full profile in every token, and we don't want a stripped-down
    /// token to nuke previously-known values.
    /// </summary>
    public void SyncFromIdentity(
        string? email,
        string? name,
        string? phone,
        DateTimeOffset seenAt)
    {
        var normalizedEmail = Normalize(email);
        if (normalizedEmail is not null) Email = normalizedEmail;

        var normalizedName = Normalize(name);
        if (normalizedName is not null) Name = normalizedName;

        var normalizedPhone = Normalize(phone);
        if (normalizedPhone is not null) Phone = normalizedPhone;

        LastSeenAt = seenAt;
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    /// <summary>
    /// Mark the user as having completed the public-frontend onboarding
    /// wizard. Idempotent — calling it on an already-onboarded user is a
    /// no-op so the frontend can safely re-fire the PATCH on retry.
    /// </summary>
    public void MarkOnboarded() => Onboarded = true;

    /// <summary>
    /// Apply the personal-info form (PATCH /api/users/profile/personal-info).
    /// All fields are required by the legacy validator, so this is a full set
    /// rather than a partial patch. Reference ids are resolved to existing
    /// rows by the endpoint before calling this.
    /// </summary>
    public void UpdatePersonalInfo(
        string name,
        string email,
        string phone,
        string? additionalPhone,
        string? bio,
        Guid? cityId,
        Guid? regionId)
    {
        Name = Normalize(name);
        Email = Normalize(email);
        Phone = Normalize(phone);
        AdditionalPhone = Normalize(additionalPhone);
        Bio = bio?.Trim();
        CityId = cityId;
        RegionId = regionId;
    }

    public void SetPhoto(Guid? assetId) => PhotoAssetId = assetId;

    public void SetYearsOfExperience(int years) => YearsOfExperience = years;

    public void SetProfileCompleted(bool completed) => ProfileCompleted = completed;

    /// <summary>Personal/identity attributes that today flow from IdM claims.
    /// Exposed so the sync layer can backfill them without a profile edit.</summary>
    public void SetIdentityAttributes(
        string? idNumber,
        Gender? gender,
        int? age,
        DateOnly? dateOfBirth,
        Guid? nationalityId)
    {
        if (idNumber is not null) IdNumber = idNumber.Trim();
        if (gender is not null) Gender = gender;
        if (age is not null) Age = age;
        if (dateOfBirth is not null) DateOfBirth = dateOfBirth;
        if (nationalityId is not null) NationalityId = nationalityId;
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
