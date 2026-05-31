using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Profile.UpdateLanguagesSkills;

/// <summary>
/// Body of <c>PATCH /api/users/profile/languages-skills</c>. Skills are
/// full-replaced; languages are synced to the provided set (mirrors the
/// legacy controller). Snake_case keys match the Laravel request.
/// </summary>
public sealed class UpdateLanguagesSkillsRequest
{
    [JsonPropertyName("skills")]
    public List<SkillItem>? Skills { get; init; }

    [JsonPropertyName("languages")]
    public List<LanguageItem>? Languages { get; init; }
}

public sealed class SkillItem
{
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>One of: beginner, intermediate, expert.</summary>
    [JsonPropertyName("level")]
    public string? Level { get; init; }
}

public sealed class LanguageItem
{
    /// <summary>Reference language id (must exist in the languages table).</summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    /// <summary>One of: beginner, intermediate, expert.</summary>
    [JsonPropertyName("level")]
    public string? Level { get; init; }
}
