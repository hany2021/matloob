using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Profile.UpdateExperiences;

/// <summary>
/// Body of <c>PATCH /api/users/profile/user-experiences</c>. Sets
/// <c>years_of_experience</c> and replaces the experience set. Snake_case keys
/// match the legacy Laravel request.
/// </summary>
public sealed class UpdateExperiencesRequest
{
    [JsonPropertyName("years_of_experience")]
    public int? YearsOfExperience { get; init; }

    [JsonPropertyName("experiences")]
    public List<ExperienceItem>? Experiences { get; init; }
}

public sealed class ExperienceItem
{
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    [JsonPropertyName("company")]
    public string? Company { get; init; }

    [JsonPropertyName("position")]
    public string? Position { get; init; }

    /// <summary>Start date, yyyy-MM-dd.</summary>
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>End date, yyyy-MM-dd. Null when <see cref="Current"/> is true.</summary>
    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonPropertyName("current")]
    public bool? Current { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>One of: full_time, part_time, internship, field_internship.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
