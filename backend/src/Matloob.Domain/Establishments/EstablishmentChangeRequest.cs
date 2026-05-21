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
}
