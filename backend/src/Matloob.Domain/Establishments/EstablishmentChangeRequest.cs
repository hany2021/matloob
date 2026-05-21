using Matloob.Domain.Common;

namespace Matloob.Domain.Establishments;

/// <summary>
/// A proposal to edit an Approved <see cref="Establishment"/>. The live
/// establishment continues operating while review is pending; on approval,
/// the non-null Proposed* values overwrite the matching live fields.
///
/// See docs/15-establishment-onboarding-spec.md §7.
///
/// Each Proposed* mirrors an editable field on <see cref="Establishment"/>
/// and is nullable — NULL means "this field is unchanged". §7.5 limits the
/// editable surface to all §3.1 fields (except documents — those are
/// re-uploaded via the two ProposedXxxAssetId columns) plus all §3.2
/// optional fields. <c>IsSponsor</c> and <c>CanManageEvents</c> are
/// deliberately absent — they are admin-only and edited directly on the
/// Establishment.
/// </summary>
public sealed class EstablishmentChangeRequest : BaseAuditableEntity<Guid>
{
    public Guid EstablishmentId { get; private set; }

    public EstablishmentChangeRequestStatus Status { get; private set; }
        = EstablishmentChangeRequestStatus.Draft;

    /// <summary>Sub claim of the Owner who initiated this change request.</summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewedByAdminId { get; private set; }

    /// <summary>Free-form admin note. Populated on Approved or Rejected.</summary>
    public string? ReviewReason { get; private set; }

    /// <summary>Set when the approved changes are actually written back to the live row.</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    // --- Proposed §3.1 (required-at-submit) fields, all nullable --------------
    public string? ProposedName { get; private set; }
    public string? ProposedCommercialRegistrationNumber { get; private set; }
    public string? ProposedLaborOfficeId { get; private set; }
    public string? ProposedSequenceNumber { get; private set; }
    public string? ProposedCity { get; private set; }
    public string? ProposedEmail { get; private set; }
    public string? ProposedPhone { get; private set; }

    // --- Proposed §3.2 optional fields, all nullable --------------------------
    public DateOnly? ProposedCommercialRegistrationExpiry { get; private set; }
    public string? ProposedEconomicActivity { get; private set; }
    public string? ProposedSubEconomicActivity { get; private set; }
    public string? ProposedDistrict { get; private set; }
    public string? ProposedArea { get; private set; }
    public string? ProposedStreet { get; private set; }
    public string? ProposedDescription { get; private set; }
    public string? ProposedLocationTitle { get; private set; }
    public decimal? ProposedLatitude { get; private set; }
    public decimal? ProposedLongitude { get; private set; }
    public string? ProposedBuildingNumber { get; private set; }
    public string? ProposedPostalCode { get; private set; }
    public string? ProposedAdditionalNumber { get; private set; }
    public string? ProposedWebsite { get; private set; }
    public int? ProposedYearsOfExperience { get; private set; }
    public string? ProposedEstablishmentSize { get; private set; }
    public string? ProposedAdditionalContactNumber { get; private set; }

    // --- Proposed document re-uploads ----------------------------------------
    /// <summary>
    /// New AuthorizationLetter Asset id, or null if the document is unchanged.
    /// On approval, the live <see cref="EstablishmentDocument"/> of this type
    /// is soft-deleted and a fresh one is inserted pointing at this asset.
    /// </summary>
    public Guid? ProposedAuthorizationLetterAssetId { get; private set; }

    /// <summary>
    /// New CommercialRegistration Asset id, or null if the document is unchanged.
    /// On approval, the live <see cref="EstablishmentDocument"/> of this type
    /// is soft-deleted and a fresh one is inserted pointing at this asset.
    /// </summary>
    public Guid? ProposedCommercialRegistrationAssetId { get; private set; }

    private EstablishmentChangeRequest() { }

    /// <summary>
    /// Open a new ChangeRequest in <see cref="EstablishmentChangeRequestStatus.Draft"/>.
    /// All Proposed* fields start null; the Owner fills in only the ones
    /// they want to change before submission.
    /// </summary>
    public static EstablishmentChangeRequest CreateDraft(
        Guid id,
        Guid establishmentId,
        string createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(createdByUserId))
        {
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        }

        return new EstablishmentChangeRequest
        {
            Id = id,
            EstablishmentId = establishmentId,
            CreatedByUserId = createdByUserId,
            Status = EstablishmentChangeRequestStatus.Draft,
        };
    }

    /// <summary>
    /// True if the editable Proposed* fields accept writes right now. The
    /// Owner can keep editing during <see cref="EstablishmentChangeRequestStatus.Draft"/>
    /// and after a Rejected review (resubmit path); other statuses lock
    /// the row.
    /// </summary>
    public bool IsEditableByOwner =>
        Status is EstablishmentChangeRequestStatus.Draft
              or EstablishmentChangeRequestStatus.Rejected;

    /// <summary>
    /// Apply a partial update to the Proposed* mirror. PATCH semantics:
    /// <c>FieldChange.NoChange</c> leaves the column alone; <c>SetTo(value)</c>
    /// overwrites (passing null clears). Trimming + empty-to-null
    /// normalization applies to strings, matching
    /// <see cref="Establishment.UpdateBasicInfo"/>.
    /// </summary>
    public void UpdateProposedBasicInfo(
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
        if (!IsEditableByOwner)
        {
            throw new InvalidOperationException(
                $"ChangeRequest is in status {Status}; proposed edits are only allowed in Draft or Rejected.");
        }

        if (name.IsSet) ProposedName = NormalizeOptional(name.Value);
        if (commercialRegistrationNumber.IsSet) ProposedCommercialRegistrationNumber = NormalizeOptional(commercialRegistrationNumber.Value);
        if (laborOfficeId.IsSet) ProposedLaborOfficeId = NormalizeOptional(laborOfficeId.Value);
        if (sequenceNumber.IsSet) ProposedSequenceNumber = NormalizeOptional(sequenceNumber.Value);
        if (city.IsSet) ProposedCity = NormalizeOptional(city.Value);
        if (email.IsSet) ProposedEmail = NormalizeOptional(email.Value);
        if (phone.IsSet) ProposedPhone = NormalizeOptional(phone.Value);

        if (commercialRegistrationExpiry.IsSet) ProposedCommercialRegistrationExpiry = commercialRegistrationExpiry.Value;
        if (economicActivity.IsSet) ProposedEconomicActivity = NormalizeOptional(economicActivity.Value);
        if (subEconomicActivity.IsSet) ProposedSubEconomicActivity = NormalizeOptional(subEconomicActivity.Value);
        if (district.IsSet) ProposedDistrict = NormalizeOptional(district.Value);
        if (area.IsSet) ProposedArea = NormalizeOptional(area.Value);
        if (street.IsSet) ProposedStreet = NormalizeOptional(street.Value);
        if (description.IsSet) ProposedDescription = NormalizeOptional(description.Value);
        if (locationTitle.IsSet) ProposedLocationTitle = NormalizeOptional(locationTitle.Value);
        if (latitude.IsSet) ProposedLatitude = latitude.Value;
        if (longitude.IsSet) ProposedLongitude = longitude.Value;
        if (buildingNumber.IsSet) ProposedBuildingNumber = NormalizeOptional(buildingNumber.Value);
        if (postalCode.IsSet) ProposedPostalCode = NormalizeOptional(postalCode.Value);
        if (additionalNumber.IsSet) ProposedAdditionalNumber = NormalizeOptional(additionalNumber.Value);
        if (website.IsSet) ProposedWebsite = NormalizeOptional(website.Value);
        if (yearsOfExperience.IsSet) ProposedYearsOfExperience = yearsOfExperience.Value;
        if (establishmentSize.IsSet) ProposedEstablishmentSize = NormalizeOptional(establishmentSize.Value);
        if (additionalContactNumber.IsSet) ProposedAdditionalContactNumber = NormalizeOptional(additionalContactNumber.Value);
    }

    /// <summary>Attach (or clear) the proposed AuthorizationLetter asset.</summary>
    public void AttachAuthorizationLetter(Guid? assetId)
    {
        if (!IsEditableByOwner)
        {
            throw new InvalidOperationException(
                $"ChangeRequest is in status {Status}; document edits are only allowed in Draft or Rejected.");
        }
        ProposedAuthorizationLetterAssetId = assetId;
    }

    /// <summary>Attach (or clear) the proposed CommercialRegistration asset.</summary>
    public void AttachCommercialRegistration(Guid? assetId)
    {
        if (!IsEditableByOwner)
        {
            throw new InvalidOperationException(
                $"ChangeRequest is in status {Status}; document edits are only allowed in Draft or Rejected.");
        }
        ProposedCommercialRegistrationAssetId = assetId;
    }

    /// <summary>
    /// True if at least one Proposed* field or proposed document is set —
    /// SubmitForReview refuses to advance empty change requests (spec sec 7).
    /// </summary>
    public bool HasAnyProposedChange =>
        ProposedName is not null
        || ProposedCommercialRegistrationNumber is not null
        || ProposedLaborOfficeId is not null
        || ProposedSequenceNumber is not null
        || ProposedCity is not null
        || ProposedEmail is not null
        || ProposedPhone is not null
        || ProposedCommercialRegistrationExpiry is not null
        || ProposedEconomicActivity is not null
        || ProposedSubEconomicActivity is not null
        || ProposedDistrict is not null
        || ProposedArea is not null
        || ProposedStreet is not null
        || ProposedDescription is not null
        || ProposedLocationTitle is not null
        || ProposedLatitude is not null
        || ProposedLongitude is not null
        || ProposedBuildingNumber is not null
        || ProposedPostalCode is not null
        || ProposedAdditionalNumber is not null
        || ProposedWebsite is not null
        || ProposedYearsOfExperience is not null
        || ProposedEstablishmentSize is not null
        || ProposedAdditionalContactNumber is not null
        || ProposedAuthorizationLetterAssetId is not null
        || ProposedCommercialRegistrationAssetId is not null;

    /// <summary>
    /// Transition Draft / Rejected → PendingReview. Spec §7.1. The endpoint
    /// is responsible for the CR-uniqueness pre-flight and history append;
    /// this method just stamps the lifecycle fields.
    /// </summary>
    public void Submit(DateTimeOffset now)
    {
        if (!IsEditableByOwner)
        {
            throw new InvalidOperationException(
                $"Cannot submit from status {Status}; allowed only from Draft or Rejected.");
        }
        if (!HasAnyProposedChange)
        {
            throw new InvalidOperationException(
                "ChangeRequest has no proposed changes to submit.");
        }

        Status = EstablishmentChangeRequestStatus.PendingReview;
        SubmittedAt = now;

        // Clear the prior Rejected* review fields. Audit row keeps the
        // history of the prior rejection.
        ReviewedAt = null;
        ReviewedByAdminId = null;
        ReviewReason = null;
    }

    /// <summary>
    /// Admin approves the change request. The endpoint is responsible for
    /// applying the Proposed* values to the live Establishment + swapping
    /// documents; this method just stamps the lifecycle fields.
    /// </summary>
    public void Approve(DateTimeOffset now, string approvedByAdminId)
    {
        if (string.IsNullOrWhiteSpace(approvedByAdminId))
        {
            throw new ArgumentException("ApprovedByAdminId is required.", nameof(approvedByAdminId));
        }
        if (Status != EstablishmentChangeRequestStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"Cannot approve from status {Status}; only PendingReview is approvable.");
        }

        Status = EstablishmentChangeRequestStatus.Approved;
        ReviewedAt = now;
        ReviewedByAdminId = approvedByAdminId;
        AppliedAt = now;
    }

    /// <summary>Admin rejects the change request with a mandatory reason.</summary>
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
        if (Status != EstablishmentChangeRequestStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"Cannot reject from status {Status}; only PendingReview is rejectable.");
        }

        Status = EstablishmentChangeRequestStatus.Rejected;
        ReviewedAt = now;
        ReviewedByAdminId = rejectedByAdminId;
        ReviewReason = reason.Trim();
    }

    private static string? NormalizeOptional(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
