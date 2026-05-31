using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Profile.UpdatePersonalInfo;

/// <summary>
/// Body of <c>PATCH /api/users/profile/personal-info</c>. Snake_case keys
/// match the legacy Laravel <c>UpdatePersonalInfoRequest</c>. All fields are
/// required (the legacy validator made them so). The frontend submits this as
/// JSON via laravel-precognition with a spoofed <c>_method=patch</c>; the
/// extra <c>_method</c> / <c>bank_confirmation</c> keys are ignored.
/// </summary>
public sealed class UpdatePersonalInfoRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    [JsonPropertyName("additional_phone_number")]
    public string? AdditionalPhoneNumber { get; init; }

    [JsonPropertyName("city_id")]
    public Guid? CityId { get; init; }

    [JsonPropertyName("region_id")]
    public Guid? RegionId { get; init; }

    [JsonPropertyName("bio")]
    public string? Bio { get; init; }

    [JsonPropertyName("bank_id")]
    public Guid? BankId { get; init; }

    [JsonPropertyName("iban")]
    public string? Iban { get; init; }
}
