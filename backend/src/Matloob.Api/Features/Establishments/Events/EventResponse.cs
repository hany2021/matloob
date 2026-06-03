using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// Wire shape of an establishment event — mirrors the legacy Laravel
/// <c>EventResource</c>. Wrapped in the global <c>{ data }</c> envelope.
/// <c>uploads</c>, <c>success_criteria</c> and <c>opportunities</c> are empty
/// for now (deferred — see docs/SESSION-RESUME.md).
/// </summary>
public sealed class EventResponse
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;

    [JsonPropertyName("size")] public string? Size { get; init; }
    [JsonPropertyName("size_label")] public string? SizeLabel { get; init; }
    [JsonPropertyName("classification")] public string? Classification { get; init; }

    [JsonPropertyName("start_date")] public string? StartDate { get; init; }
    [JsonPropertyName("end_date")] public string? EndDate { get; init; }

    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("status_label")] public string StatusLabel { get; init; } = string.Empty;
    [JsonPropertyName("status_icon")] public string StatusIcon { get; init; } = string.Empty;
    [JsonPropertyName("card_type")] public string CardType { get; init; } = string.Empty;

    [JsonPropertyName("lon")] public string? Lon { get; init; }
    [JsonPropertyName("lat")] public string? Lat { get; init; }
    [JsonPropertyName("location_title")] public string? LocationTitle { get; init; }

    [JsonPropertyName("min_attendees")] public int? MinAttendees { get; init; }
    [JsonPropertyName("max_attendees")] public int? MaxAttendees { get; init; }

    [JsonPropertyName("steps_done")] public int StepsDone { get; init; }
    [JsonPropertyName("can_end")] public bool CanEnd { get; init; }

    [JsonPropertyName("type")] public EventTypeDto? Type { get; init; }
    [JsonPropertyName("season")] public EventSeasonDto? Season { get; init; }

    [JsonPropertyName("opportunity_categories")]
    public IReadOnlyList<EventCategoryDto> OpportunityCategories { get; init; } = [];

    [JsonPropertyName("uploads")] public IReadOnlyList<object> Uploads { get; init; } = [];
    [JsonPropertyName("success_criteria")] public IReadOnlyList<object> SuccessCriteria { get; init; } = [];
    [JsonPropertyName("opportunities")] public IReadOnlyList<object> Opportunities { get; init; } = [];
}

public sealed class EventTypeDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("icon")] public string? Icon { get; init; }
    [JsonPropertyName("background")] public string? Background { get; init; }
}

public sealed class EventSeasonDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
}

public sealed class EventCategoryDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("icon")] public string? Icon { get; init; }
    [JsonPropertyName("for_vacancy")] public bool ForVacancy { get; init; }
    [JsonPropertyName("is_other")] public bool IsOther { get; init; }
}

/// <summary>One status group — Laravel <c>GroupedEventResource</c> inner shape.</summary>
public sealed class EventGroup
{
    [JsonPropertyName("status_label")] public string StatusLabel { get; init; } = string.Empty;
    [JsonPropertyName("status_icon")] public string StatusIcon { get; init; } = string.Empty;
    [JsonPropertyName("card_type")] public string CardType { get; init; } = string.Empty;
    [JsonPropertyName("data")] public IReadOnlyList<EventResponse> Data { get; init; } = [];
}

/// <summary>
/// <c>GET /establishments/events</c> groups the establishment's events by card
/// status, mirroring the legacy <c>GroupedEventResource</c> EXACTLY — each top
/// key wraps a single-entry object re-keyed by the same status, so the public
/// frontend reads <c>data[status][status]</c> (e.g.
/// <c>data.drafted.drafted.{data,status_label,status_icon,card_type}</c>). A bare
/// array here made the frontend's <c>data.drafted.drafted</c> undefined → empty
/// "untitled" tabs (events not shown).
/// </summary>
public sealed class GroupedEventsResponse
{
    [JsonPropertyName("active")] public Dictionary<string, EventGroup> Active { get; init; } = new();
    [JsonPropertyName("upcoming")] public Dictionary<string, EventGroup> Upcoming { get; init; } = new();
    [JsonPropertyName("drafted")] public Dictionary<string, EventGroup> Drafted { get; init; } = new();
    [JsonPropertyName("ended")] public Dictionary<string, EventGroup> Ended { get; init; } = new();
}
