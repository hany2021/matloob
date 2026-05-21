namespace Matloob.Api.Features.Establishments.ChangeRequests.UpdateBasicInfo;

/// <summary>
/// PATCH payload for a ChangeRequest's Proposed* mirror of the §3.1 + §3.2
/// fields. Every property is nullable; null on the wire means "leave the
/// Proposed* column alone" (same PATCH semantics as the onboarding
/// UpdateBasicInfoRequest after that endpoint dropped its body-rewind
/// approach). Clearing a column requires sending an empty string for
/// string-typed slots; the domain normalizer turns it into <c>null</c>.
///
/// IsSponsor / CanManageEvents / Status / CreatedByUserId / lifecycle
/// timestamps are intentionally absent (spec §7.5 — admin-only fields are
/// never edited through ChangeRequest).
/// </summary>
public sealed class UpdateChangeRequestBasicInfoRequest
{
    public string? Name { get; init; }
    public string? CommercialRegistrationNumber { get; init; }
    public string? LaborOfficeId { get; init; }
    public string? SequenceNumber { get; init; }
    public string? City { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }

    public DateOnly? CommercialRegistrationExpiry { get; init; }
    public string? EconomicActivity { get; init; }
    public string? SubEconomicActivity { get; init; }
    public string? District { get; init; }
    public string? Area { get; init; }
    public string? Street { get; init; }
    public string? Description { get; init; }
    public string? LocationTitle { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? BuildingNumber { get; init; }
    public string? PostalCode { get; init; }
    public string? AdditionalNumber { get; init; }
    public string? Website { get; init; }
    public int? YearsOfExperience { get; init; }
    public string? EstablishmentSize { get; init; }
    public string? AdditionalContactNumber { get; init; }
}
