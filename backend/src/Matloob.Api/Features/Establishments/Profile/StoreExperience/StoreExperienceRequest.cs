using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Establishments.Profile.StoreExperience;

/// <summary>
/// Body of <c>POST /api/establishments/me/profile/experience</c> — add a
/// portfolio experience. Mirrors the legacy <c>StoreProfileExperienceRequest</c>
/// (type / name / category / from / to / description). JSON via
/// laravel-precognition. <c>category</c> is an event-type or opportunity-category
/// uuid (per <c>type</c>); bound as a string so an empty precognition draft does
/// not fail model binding.
/// </summary>
public sealed class StoreExperienceRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
