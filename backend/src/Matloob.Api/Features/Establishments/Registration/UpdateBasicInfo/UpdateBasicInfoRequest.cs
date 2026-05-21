namespace Matloob.Api.Features.Establishments.Registration.UpdateBasicInfo;

/// <summary>
/// PATCH-style basic-info payload. Every field is nullable; an omitted key
/// in the incoming JSON leaves the column unchanged, while an explicit
/// <c>null</c> on an optional field clears it (no presence requirement at
/// this stage — SubmitForReview enforces completeness later).
///
/// FastEndpoints' default JSON binding maps absent keys to default values
/// (so all properties land as null when the client omits them). The
/// endpoint translates the request into <see cref="Matloob.Domain.Common.FieldChange{T}"/>
/// instances using <see cref="UpdateBasicInfoRequest.PresentKeys"/> (case-
/// insensitive set of field names the client actually sent) so we can tell
/// "absent" from "explicit null".
/// </summary>
public sealed class UpdateBasicInfoRequest
{
    // §3.1 required-at-submit, but optional here.
    public string? Name { get; init; }
    public string? CommercialRegistrationNumber { get; init; }
    public string? LaborOfficeId { get; init; }
    public string? SequenceNumber { get; init; }
    public string? City { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }

    // §3.2 optional.
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
