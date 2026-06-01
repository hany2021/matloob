using System.Text.Json.Serialization;
using Matloob.Api.Features.Common;

namespace Matloob.Api.Features.Reference.InitData;

// All DTOs in this file mirror the legacy Laravel `*Resource::toArray()`
// shapes 1:1. Property names use snake_case via JsonPropertyName so the
// public frontend's parser keeps working unchanged.

public sealed record CityDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record RegionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record LanguageDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record NationalityDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record BankDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record JobTitleDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record OfferCancellationReasonDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("is_other")] bool IsOther);

public sealed record OfferRejectionReasonDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("is_other")] bool IsOther);

public sealed record SuggestedAttendeeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("min")] int Min,
    [property: JsonPropertyName("max")] int Max);

public sealed record SuggestedLocationDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("lat")] decimal Lat,
    [property: JsonPropertyName("lon")] decimal Lon);

public sealed record EventTypeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("background")] string? Background,
    [property: JsonPropertyName("icon")] string? Icon);

public sealed record SeasonDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("image")] string? Image);

/// <summary>
/// Opportunity category in flat-list shape. Used for the top-level
/// <c>opportunity_categories</c> key.
/// </summary>
public sealed record OpportunityCategoryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("for_vacancy")] bool ForVacancy,
    [property: JsonPropertyName("is_other")] bool IsOther);

/// <summary>
/// Opportunity category in tree shape (parent with children). Used for
/// <c>grouped_opportunity_categories</c> and <c>individuals_opportunity_categories</c>.
/// </summary>
public sealed record OpportunityCategoryWithChildrenDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("for_vacancy")] bool ForVacancy,
    [property: JsonPropertyName("is_other")] bool IsOther,
    [property: JsonPropertyName("children")] IReadOnlyList<OpportunityCategoryDto> Children);

/// <summary>
/// Legacy languages wrapper: <c>{data: [...]}</c>. Laravel's
/// <c>Resource::collection(...)->toResponse(...)->getData(true)</c> produces
/// this envelope when applied to a non-paginated Collection. Matched here so
/// the public frontend doesn't have to learn a new shape.
/// </summary>
public sealed record LanguagesWrapperDto(
    [property: JsonPropertyName("data")] IReadOnlyList<LanguageDto> Data);

/// <summary>
/// Top-level init-data response. 17 keys, in the same order Laravel emitted
/// them.
///
/// Marked <see cref="IBypassEnvelope"/> so the global ResponseEnvelopeShim
/// does NOT wrap it in <c>{ data: { ... } }</c>. The legacy Laravel
/// endpoint returned a flat object (controllers built it via
/// <c>response()->json([...])</c>, never APIResource), and the public
/// frontend's <c>useInitData</c> hook reads <c>initData.cities</c>
/// directly — wrapping it breaks every dropdown that consumes a lookup.
/// </summary>
public sealed class InitDataResponse : IBypassEnvelope
{
    [JsonPropertyName("translations")]
    public IReadOnlyDictionary<string, string> Translations { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Setting values are typed on read: bool / int / decimal / JSON / string.
    /// The dictionary value is <c>object?</c> so each entry serializes to its
    /// natural JSON type (true, 42, 1.5, {...}, "raw").
    /// </summary>
    [JsonPropertyName("settings")]
    public IReadOnlyDictionary<string, object?> Settings { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("suggested_attendee")]
    public IReadOnlyList<SuggestedAttendeeDto> SuggestedAttendee { get; init; } = [];

    [JsonPropertyName("suggested_locations")]
    public IReadOnlyList<SuggestedLocationDto> SuggestedLocations { get; init; } = [];

    [JsonPropertyName("event_types")]
    public IReadOnlyList<EventTypeDto> EventTypes { get; init; } = [];

    [JsonPropertyName("opportunity_categories")]
    public IReadOnlyList<OpportunityCategoryDto> OpportunityCategories { get; init; } = [];

    [JsonPropertyName("grouped_opportunity_categories")]
    public IReadOnlyList<OpportunityCategoryWithChildrenDto> GroupedOpportunityCategories { get; init; } = [];

    [JsonPropertyName("cities")]
    public IReadOnlyList<CityDto> Cities { get; init; } = [];

    [JsonPropertyName("regions")]
    public IReadOnlyList<RegionDto> Regions { get; init; } = [];

    [JsonPropertyName("languages")]
    public LanguagesWrapperDto Languages { get; init; } = new(Array.Empty<LanguageDto>());

    [JsonPropertyName("banks")]
    public IReadOnlyList<BankDto> Banks { get; init; } = [];

    [JsonPropertyName("seasons")]
    public IReadOnlyList<SeasonDto> Seasons { get; init; } = [];

    [JsonPropertyName("individuals_opportunity_categories")]
    public IReadOnlyList<OpportunityCategoryWithChildrenDto> IndividualsOpportunityCategories { get; init; } = [];

    [JsonPropertyName("nationalities")]
    public IReadOnlyList<NationalityDto> Nationalities { get; init; } = [];

    [JsonPropertyName("job_titles")]
    public IReadOnlyList<JobTitleDto> JobTitles { get; init; } = [];

    [JsonPropertyName("cancellation_reasons")]
    public IReadOnlyList<OfferCancellationReasonDto> CancellationReasons { get; init; } = [];

    [JsonPropertyName("rejection_reasons")]
    public IReadOnlyList<OfferRejectionReasonDto> RejectionReasons { get; init; } = [];
}
