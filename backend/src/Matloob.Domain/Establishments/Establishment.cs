using Matloob.Domain.Common;

namespace Matloob.Domain.Establishments;

/// <summary>
/// The Establishment aggregate root. One row spans what the legacy schema
/// split across three tables (<c>establishments</c>, <c>establishment_profiles</c>,
/// <c>establishment_contact_infos</c>) — see
/// docs/15-establishment-onboarding-spec.md §3.4 for the consolidation rationale.
///
/// Status lifecycle and the rules around each field are documented in
/// docs/15-establishment-onboarding-spec.md §§2-3 and §10 (authorization).
///
/// Setters are <c>private</c>; behavior methods will be added on demand in
/// later commits as each transition (Submit, Approve, Reject, Suspend,
/// Reinstate, apply ChangeRequest, …) is implemented. This commit ships the
/// type only — no orchestration yet.
/// </summary>
public sealed class Establishment : BaseAuditableEntity<Guid>, IAggregateRoot
{
    // --- §3.1 Required at submit-for-review ---------------------------------
    public string Name { get; private set; } = string.Empty;
    public string CommercialRegistrationNumber { get; private set; } = string.Empty;
    public string LaborOfficeId { get; private set; } = string.Empty;
    public string SequenceNumber { get; private set; } = string.Empty;

    /// <summary>Free text in v1 (O-3). Promotion to FK on the cities lookup is deferred.</summary>
    public string City { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;

    // --- §3.2 Optional at any time ------------------------------------------
    public DateOnly? CommercialRegistrationExpiry { get; private set; }
    public string? EconomicActivity { get; private set; }
    public string? SubEconomicActivity { get; private set; }

    /// <summary>Maps to legacy <c>neighborhood</c>.</summary>
    public string? District { get; private set; }

    public string? Area { get; private set; }

    /// <summary>Maps to legacy <c>street_name</c>.</summary>
    public string? Street { get; private set; }

    public string? Description { get; private set; }
    public string? LocationTitle { get; private set; }

    /// <summary>Saudi precision: numeric(8,6). Optional.</summary>
    public decimal? Latitude { get; private set; }

    /// <summary>Saudi precision: numeric(9,6). Optional.</summary>
    public decimal? Longitude { get; private set; }

    public string? BuildingNumber { get; private set; }
    public string? PostalCode { get; private set; }
    public string? AdditionalNumber { get; private set; }
    public string? Website { get; private set; }
    public int? YearsOfExperience { get; private set; }

    /// <summary>Small / Medium / Large free text — used as a public-profile filter.</summary>
    public string? EstablishmentSize { get; private set; }

    public string? AdditionalContactNumber { get; private set; }

    // --- §3.3 Admin-controlled flags ----------------------------------------
    /// <summary>Set by admins post-approval. Gates sponsor-only flows.</summary>
    public bool IsSponsor { get; private set; }

    /// <summary>Set by admins post-approval. Gates event-management routes.</summary>
    public bool CanManageEvents { get; private set; }

    // --- §2 Lifecycle -------------------------------------------------------
    public EstablishmentStatus Status { get; private set; } = EstablishmentStatus.Draft;

    /// <summary>
    /// Sub claim of the user who created the draft. Captured because they
    /// become the first <see cref="EstablishmentMember"/> with role
    /// <c>Owner</c> on approval (§6.2).
    /// </summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    /// <summary>
    /// Flags rows brought over from the legacy Laravel system that may not
    /// have AuthorizationLetter / CommercialRegistration documents
    /// (docs/15-establishment-onboarding-spec.md §12.4).
    /// </summary>
    public bool IsLegacyImport { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? ApprovedByAdminId { get; private set; }

    public DateTimeOffset? RejectedAt { get; private set; }
    public string? RejectedByAdminId { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }
    public string? SuspendedByAdminId { get; private set; }
    public string? SuspensionReason { get; private set; }

    private Establishment() { }

    /// <summary>
    /// Create a fresh Draft establishment. All §3.1 / §3.2 fields start empty
    /// or null — the user fills them in via the basic-info endpoint (next
    /// commits). Status is forced to Draft regardless of caller input.
    /// </summary>
    public static Establishment CreateDraft(Guid id, string createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(createdByUserId))
        {
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        }

        return new Establishment
        {
            Id = id,
            CreatedByUserId = createdByUserId,
            Status = EstablishmentStatus.Draft,
            IsLegacyImport = false,
        };
    }

    /// <summary>
    /// Whether the editable §3 surface accepts writes right now. Draft and
    /// Rejected let the creator keep filling things in; everything else is
    /// locked (PendingReview is in the admin queue, Approved goes through
    /// ChangeRequest, Suspended blocks all mutations).
    /// </summary>
    public bool IsEditableByCreator =>
        Status is EstablishmentStatus.Draft or EstablishmentStatus.Rejected;

    /// <summary>
    /// Apply a partial update to the §3.1 / §3.2 field set. Each parameter is
    /// a <see cref="FieldChange{T}"/>: <c>NoChange</c> leaves the column
    /// alone, <c>SetTo(value)</c> overwrites it (including setting an
    /// optional column to <c>null</c>). String values are trimmed; empty /
    /// whitespace-only inputs collapse to empty string for §3.1 (which is
    /// modelled as non-null) and to <c>null</c> for §3.2 (which is modelled
    /// as nullable).
    /// </summary>
    public void UpdateBasicInfo(
        FieldChange<string?> name = default,
        FieldChange<string?> commercialRegistrationNumber = default,
        FieldChange<string?> laborOfficeId = default,
        FieldChange<string?> sequenceNumber = default,
        FieldChange<string?> city = default,
        FieldChange<string?> email = default,
        FieldChange<string?> phone = default,
        FieldChange<DateOnly?> commercialRegistrationExpiry = default,
        FieldChange<string?> economicActivity = default,
        FieldChange<string?> subEconomicActivity = default,
        FieldChange<string?> district = default,
        FieldChange<string?> area = default,
        FieldChange<string?> street = default,
        FieldChange<string?> description = default,
        FieldChange<string?> locationTitle = default,
        FieldChange<decimal?> latitude = default,
        FieldChange<decimal?> longitude = default,
        FieldChange<string?> buildingNumber = default,
        FieldChange<string?> postalCode = default,
        FieldChange<string?> additionalNumber = default,
        FieldChange<string?> website = default,
        FieldChange<int?> yearsOfExperience = default,
        FieldChange<string?> establishmentSize = default,
        FieldChange<string?> additionalContactNumber = default)
    {
        if (!IsEditableByCreator)
        {
            throw new InvalidOperationException(
                $"Establishment is in status {Status}; basic-info edits are only allowed in Draft or Rejected.");
        }

        if (name.IsSet) Name = NormalizeRequired(name.Value);
        if (commercialRegistrationNumber.IsSet) CommercialRegistrationNumber = NormalizeRequired(commercialRegistrationNumber.Value);
        if (laborOfficeId.IsSet) LaborOfficeId = NormalizeRequired(laborOfficeId.Value);
        if (sequenceNumber.IsSet) SequenceNumber = NormalizeRequired(sequenceNumber.Value);
        if (city.IsSet) City = NormalizeRequired(city.Value);
        if (email.IsSet) Email = NormalizeRequired(email.Value);
        if (phone.IsSet) Phone = NormalizeRequired(phone.Value);

        if (commercialRegistrationExpiry.IsSet) CommercialRegistrationExpiry = commercialRegistrationExpiry.Value;
        if (economicActivity.IsSet) EconomicActivity = NormalizeOptional(economicActivity.Value);
        if (subEconomicActivity.IsSet) SubEconomicActivity = NormalizeOptional(subEconomicActivity.Value);
        if (district.IsSet) District = NormalizeOptional(district.Value);
        if (area.IsSet) Area = NormalizeOptional(area.Value);
        if (street.IsSet) Street = NormalizeOptional(street.Value);
        if (description.IsSet) Description = NormalizeOptional(description.Value);
        if (locationTitle.IsSet) LocationTitle = NormalizeOptional(locationTitle.Value);
        if (latitude.IsSet) Latitude = latitude.Value;
        if (longitude.IsSet) Longitude = longitude.Value;
        if (buildingNumber.IsSet) BuildingNumber = NormalizeOptional(buildingNumber.Value);
        if (postalCode.IsSet) PostalCode = NormalizeOptional(postalCode.Value);
        if (additionalNumber.IsSet) AdditionalNumber = NormalizeOptional(additionalNumber.Value);
        if (website.IsSet) Website = NormalizeOptional(website.Value);
        if (yearsOfExperience.IsSet) YearsOfExperience = yearsOfExperience.Value;
        if (establishmentSize.IsSet) EstablishmentSize = NormalizeOptional(establishmentSize.Value);
        if (additionalContactNumber.IsSet) AdditionalContactNumber = NormalizeOptional(additionalContactNumber.Value);
    }

    private static string NormalizeRequired(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? string.Empty : trimmed;
    }

    private static string? NormalizeOptional(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Transition Draft / Rejected → PendingReview. Spec §2 + §3.1: the seven
    /// required scalar fields must be populated. Document presence is checked
    /// by the caller (which has the DbContext) — keeping that out of the
    /// aggregate keeps it from depending on persistence.
    /// </summary>
    /// <param name="now">Server clock — the caller passes
    /// <c>TimeProvider.GetUtcNow()</c> so tests can pin a deterministic
    /// timestamp.</param>
    public void SubmitForReview(DateTimeOffset now)
    {
        if (!IsEditableByCreator)
        {
            throw new InvalidOperationException(
                $"Cannot submit from status {Status}; allowed only from Draft or Rejected.");
        }

        EnsureRequiredFieldsPopulated();

        Status = EstablishmentStatus.PendingReview;
        SubmittedAt = now;

        // Clear the current Rejected* triplet -- the rejection is now history
        // (kept in EstablishmentReviewHistory) and shouldn't leak back into
        // the admin review pane on resubmit.
        RejectedAt = null;
        RejectedByAdminId = null;
        RejectionReason = null;
    }

    private void EnsureRequiredFieldsPopulated()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) missing.Add(nameof(Name));
        if (string.IsNullOrWhiteSpace(CommercialRegistrationNumber)) missing.Add(nameof(CommercialRegistrationNumber));
        if (string.IsNullOrWhiteSpace(LaborOfficeId)) missing.Add(nameof(LaborOfficeId));
        if (string.IsNullOrWhiteSpace(SequenceNumber)) missing.Add(nameof(SequenceNumber));
        if (string.IsNullOrWhiteSpace(City)) missing.Add(nameof(City));
        if (string.IsNullOrWhiteSpace(Email)) missing.Add(nameof(Email));
        if (string.IsNullOrWhiteSpace(Phone)) missing.Add(nameof(Phone));

        if (missing.Count > 0)
        {
            throw new EstablishmentRequiredFieldsMissingException(missing);
        }
    }
}

/// <summary>
/// Thrown by <see cref="Establishment.SubmitForReview"/> when one or more
/// §3.1 fields are still empty. The endpoint maps this to a 400 with a
/// per-field error list.
/// </summary>
public sealed class EstablishmentRequiredFieldsMissingException : Exception
{
    public IReadOnlyList<string> MissingFields { get; }

    public EstablishmentRequiredFieldsMissingException(IReadOnlyList<string> missingFields)
        : base($"Required fields missing: {string.Join(", ", missingFields)}.")
    {
        MissingFields = missingFields;
    }
}
