using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Establishments.Profile.UpdateContactInfo;

/// <summary>
/// Body of <c>PATCH /api/establishments/me/profile/contact-info</c>. Snake_case
/// keys mirror the legacy <c>UpdateProfileContactInfoRequest</c>. The frontend
/// submits this as JSON via laravel-precognition; all three fields are required.
/// </summary>
public sealed class UpdateContactInfoRequest
{
    [JsonPropertyName("contact_number")]
    public string? ContactNumber { get; init; }

    [JsonPropertyName("additional_contact_number")]
    public string? AdditionalContactNumber { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}
