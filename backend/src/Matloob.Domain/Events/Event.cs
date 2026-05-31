using Matloob.Domain.Common;

namespace Matloob.Domain.Events;

/// <summary>
/// Establishment event aggregate. Mirrors the legacy Laravel <c>events</c>
/// table. Created progressively via a multi-step draft (the public frontend's
/// 5-step wizard): the first submit creates a Drafted event from step-one
/// fields, subsequent submits fill schedule/categories, and publish flips the
/// status to Upcoming/Active.
///
/// <para>
/// Scope note (core slice): event-level success criteria, media uploads, and
/// the step-4 nested opportunity creation are deferred — see
/// docs/SESSION-RESUME.md. <see cref="EventTypeId"/>/<see cref="SeasonId"/>/
/// <see cref="CityId"/> FK the existing reference tables (declared in the EF
/// configuration to keep the domain free of EF-isms).
/// </para>
/// </summary>
public sealed class Event : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid EstablishmentId { get; private set; }
    public Guid EventTypeId { get; private set; }
    public Guid? SeasonId { get; private set; }
    public Guid? CityId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    public EventStatus Status { get; private set; }

    public string? LocationTitle { get; private set; }

    /// <summary>Latitude — Postgres precision <c>numeric(8,6)</c>.</summary>
    public decimal? Latitude { get; private set; }

    /// <summary>Longitude — Postgres precision <c>numeric(9,6)</c>.</summary>
    public decimal? Longitude { get; private set; }

    public int? MinAttendees { get; private set; }
    public int? MaxAttendees { get; private set; }

    public string? Size { get; private set; }
    public string? Classification { get; private set; }

    /// <summary>How many wizard steps the draft has completed (0-5).</summary>
    public int StepsDone { get; private set; }

    private Event() { }

    public Event(
        Guid id,
        Guid establishmentId,
        Guid eventTypeId,
        string name,
        string description,
        Guid? seasonId)
    {
        Id = id;
        EstablishmentId = establishmentId;
        EventTypeId = eventTypeId;
        Name = Normalize(name);
        Description = Normalize(description);
        SeasonId = seasonId;
        Status = EventStatus.Drafted;
        StepsDone = 1;
    }

    /// <summary>Apply step-one (basics) fields.</summary>
    public void ApplyBasics(
        Guid eventTypeId, string name, string description,
        Guid? seasonId, string? size, string? classification)
    {
        EventTypeId = eventTypeId;
        Name = Normalize(name);
        Description = Normalize(description);
        SeasonId = seasonId;
        Size = Trim(size);
        Classification = Trim(classification);
    }

    /// <summary>Apply step-two (schedule + location + attendees) fields.</summary>
    public void ApplySchedule(
        DateOnly? startDate, DateOnly? endDate, string? locationTitle,
        decimal? latitude, decimal? longitude, int? minAttendees, int? maxAttendees,
        Guid? cityId)
    {
        StartDate = startDate;
        EndDate = endDate;
        LocationTitle = Trim(locationTitle);
        Latitude = latitude;
        Longitude = longitude;
        MinAttendees = minAttendees;
        MaxAttendees = maxAttendees;
        CityId = cityId;
    }

    public void SetStepsDone(int steps)
    {
        if (steps > StepsDone) StepsDone = steps;
    }

    /// <summary>Publish: future start → Upcoming, otherwise Active.</summary>
    public void Publish(DateOnly today)
    {
        Status = StartDate.HasValue && StartDate.Value > today
            ? EventStatus.Upcoming
            : EventStatus.Active;
        SetStepsDone(5);
    }

    public bool CanEnd() => Status is EventStatus.Active or EventStatus.Upcoming;

    public void End(DateOnly today)
    {
        Status = EventStatus.Ended;
        EndDate = today;
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
