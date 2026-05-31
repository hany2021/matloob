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

    /// <summary>
    /// Owner-side FK to the establishment's single bank account (the
    /// <c>BankAccount</c> aggregate is ownerless and shared with the user side).
    /// Null until bank details are saved via the profile bank-account endpoint.
    /// </summary>
    public Guid? BankAccountId { get; private set; }

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

    /// <summary>
    /// Admin approves a PendingReview submission. The endpoint is
    /// responsible for inserting the first Owner row + audit row; this
    /// method just flips the lifecycle fields on the aggregate.
    /// </summary>
    public void Approve(DateTimeOffset now, string approvedByAdminId)
    {
        if (string.IsNullOrWhiteSpace(approvedByAdminId))
        {
            throw new ArgumentException("ApprovedByAdminId is required.", nameof(approvedByAdminId));
        }
        if (Status != EstablishmentStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"Cannot approve from status {Status}; only PendingReview is approvable.");
        }

        Status = EstablishmentStatus.Approved;
        ApprovedAt = now;
        ApprovedByAdminId = approvedByAdminId;
    }

    /// <summary>
    /// Admin rejects a PendingReview submission with a mandatory reason.
    /// The Rejected* triplet stays populated until the next submit clears it.
    /// </summary>
    public void Reject(DateTimeOffset now, string rejectedByAdminId, string reason)
    {
        if (string.IsNullOrWhiteSpace(rejectedByAdminId))
        {
            throw new ArgumentException("RejectedByAdminId is required.", nameof(rejectedByAdminId));
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }
        if (Status != EstablishmentStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"Cannot reject from status {Status}; only PendingReview is rejectable.");
        }

        Status = EstablishmentStatus.Rejected;
        RejectedAt = now;
        RejectedByAdminId = rejectedByAdminId;
        RejectionReason = reason.Trim();
    }

    /// <summary>
    /// Admin suspends an Approved establishment. Spec §8: writes are locked
    /// (mutation endpoints return 423 Locked) but reads keep working. The
    /// suspension fields live on the row until <see cref="Reinstate"/>
    /// clears them; the audit trail in
    /// <see cref="EstablishmentReviewHistory"/> preserves each suspension /
    /// reinstatement event.
    /// </summary>
    public void Suspend(DateTimeOffset now, string suspendedByAdminId, string reason)
    {
        if (string.IsNullOrWhiteSpace(suspendedByAdminId))
        {
            throw new ArgumentException("SuspendedByAdminId is required.", nameof(suspendedByAdminId));
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }
        if (Status != EstablishmentStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Cannot suspend from status {Status}; only Approved is suspendable.");
        }

        Status = EstablishmentStatus.Suspended;
        SuspendedAt = now;
        SuspendedByAdminId = suspendedByAdminId;
        SuspensionReason = reason.Trim();
    }

    /// <summary>
    /// Admin lifts the suspension. The live suspension fields are CLEARED on
    /// reinstatement — the audit row in
    /// <see cref="EstablishmentReviewHistory"/> is the durable record of
    /// "this establishment was once suspended for this reason." Keeping
    /// SuspendedAt / SuspensionReason live after reinstatement would leak
    /// stale state into the admin review pane next time the establishment
    /// hit a status check.
    /// </summary>
    public void Reinstate(DateTimeOffset now, string reinstatedByAdminId)
    {
        if (string.IsNullOrWhiteSpace(reinstatedByAdminId))
        {
            throw new ArgumentException("ReinstatedByAdminId is required.", nameof(reinstatedByAdminId));
        }
        if (Status != EstablishmentStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"Cannot reinstate from status {Status}; only Suspended is reinstatable.");
        }

        Status = EstablishmentStatus.Approved;
        SuspendedAt = null;
        SuspendedByAdminId = null;
        SuspensionReason = null;
        // 'now' / 'reinstatedByAdminId' don't live on the row; the endpoint
        // captures them on the EstablishmentReviewHistory append that follows.
        _ = now;
        _ = reinstatedByAdminId;
    }

    /// <summary>
    /// Apply an admin-approved <see cref="EstablishmentChangeRequest"/>'s
    /// Proposed* mirror to the live row. Bypasses the
    /// <see cref="IsEditableByCreator"/> guard because this is exactly the
    /// admin-mediated path for editing an Approved establishment (spec §7).
    /// Status must currently be Approved (Suspended is out of scope this
    /// phase).
    ///
    /// For each Proposed* that is non-null, the matching live column is
    /// overwritten. Document swaps live OUTSIDE this method (they touch
    /// other aggregates and are orchestrated by the approve endpoint).
    /// </summary>
    public void ApplyApprovedChangeRequest(EstablishmentChangeRequest cr)
    {
        ArgumentNullException.ThrowIfNull(cr);
        if (cr.EstablishmentId != Id)
        {
            throw new ArgumentException(
                "ChangeRequest does not belong to this establishment.", nameof(cr));
        }
        if (Status != EstablishmentStatus.Approved)
        {
            throw new InvalidOperationException(
                $"ChangeRequest can only be applied to an Approved establishment. Current: {Status}.");
        }

        if (cr.ProposedName is { } name) Name = name;
        if (cr.ProposedCommercialRegistrationNumber is { } crNumber) CommercialRegistrationNumber = crNumber;
        if (cr.ProposedLaborOfficeId is { } laborOffice) LaborOfficeId = laborOffice;
        if (cr.ProposedSequenceNumber is { } seq) SequenceNumber = seq;
        if (cr.ProposedCity is { } city) City = city;
        if (cr.ProposedEmail is { } email) Email = email;
        if (cr.ProposedPhone is { } phone) Phone = phone;

        if (cr.ProposedCommercialRegistrationExpiry is { } expiry) CommercialRegistrationExpiry = expiry;
        if (cr.ProposedEconomicActivity is { } ea) EconomicActivity = ea;
        if (cr.ProposedSubEconomicActivity is { } sea) SubEconomicActivity = sea;
        if (cr.ProposedDistrict is { } district) District = district;
        if (cr.ProposedArea is { } area) Area = area;
        if (cr.ProposedStreet is { } street) Street = street;
        if (cr.ProposedDescription is { } description) Description = description;
        if (cr.ProposedLocationTitle is { } locTitle) LocationTitle = locTitle;
        if (cr.ProposedLatitude is { } lat) Latitude = lat;
        if (cr.ProposedLongitude is { } lon) Longitude = lon;
        if (cr.ProposedBuildingNumber is { } building) BuildingNumber = building;
        if (cr.ProposedPostalCode is { } postal) PostalCode = postal;
        if (cr.ProposedAdditionalNumber is { } addl) AdditionalNumber = addl;
        if (cr.ProposedWebsite is { } web) Website = web;
        if (cr.ProposedYearsOfExperience is { } yoe) YearsOfExperience = yoe;
        if (cr.ProposedEstablishmentSize is { } size) EstablishmentSize = size;
        if (cr.ProposedAdditionalContactNumber is { } additionalContact) AdditionalContactNumber = additionalContact;
    }

    // --- Post-approval profile editing (Laravel Establishments/Me/Profile) ---
    // These mirror the legacy UpdateProfile{GeneralInfo,ContactInfo,Experience}
    // controllers. Unlike UpdateBasicInfo (Draft/Rejected-only self-service
    // registration), these are the live profile-edit surface used while the
    // establishment is Approved. They deliberately do NOT check
    // IsEditableByCreator — the endpoint owns the status gate (writes are blocked
    // with 423 while Suspended via EstablishmentResourceGuards.ResolveForWrite).

    /// <summary>
    /// Edit the public general-info section (Laravel
    /// <c>UpdateProfileGeneralInfoController</c>). Only the fields the profile
    /// form submits are touched; address parts captured at registration
    /// (street / city / district / additional_number) are preserved.
    /// </summary>
    public void EditGeneralInfo(
        string description,
        decimal latitude,
        decimal longitude,
        string buildingNumber,
        string? postalCode,
        string website)
    {
        Description = NormalizeOptional(description);
        Latitude = latitude;
        Longitude = longitude;
        BuildingNumber = NormalizeOptional(buildingNumber);
        PostalCode = NormalizeOptional(postalCode);
        Website = NormalizeOptional(website);
    }

    /// <summary>
    /// Edit the contact-info section (Laravel
    /// <c>UpdateProfileContactInfoController</c>): phone (legacy
    /// <c>contact_number</c>), additional contact number, and email — all three
    /// collapsed onto the establishment row in the new schema.
    /// </summary>
    public void EditContactInfo(string contactNumber, string additionalContactNumber, string email)
    {
        Phone = NormalizeRequired(contactNumber);
        AdditionalContactNumber = NormalizeOptional(additionalContactNumber);
        Email = NormalizeRequired(email);
    }

    /// <summary>
    /// Set years of experience (Laravel <c>UpdateProfileExperienceController</c>,
    /// which wrote <c>years_of_experience</c> onto the establishment profile).
    /// </summary>
    public void SetYearsOfExperience(int years) => YearsOfExperience = years;

    /// <summary>Link the establishment to its bank account (set on first upsert).</summary>
    public void SetBankAccount(Guid bankAccountId) => BankAccountId = bankAccountId;
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
