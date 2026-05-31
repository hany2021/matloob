using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Establishments.Profile.UpdateExperience;

/// <summary>
/// Body of <c>PATCH /api/establishments/me/profile/experience</c>. Mirrors the
/// legacy <c>UpdateProfileExperienceRequest</c> (years_of_experience only).
/// JSON via laravel-precognition.
/// </summary>
public sealed class UpdateExperienceRequest
{
    [JsonPropertyName("years_of_experience")]
    public int? YearsOfExperience { get; init; }
}
