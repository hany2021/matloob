using System.Globalization;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Events;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// Projects <see cref="Event"/> aggregates to <see cref="EventResponse"/>,
/// batch-loading the referenced event type / season / opportunity categories to
/// avoid N+1 queries across a grouped list.
/// </summary>
public static class EventReadMapper
{
    public static async Task<List<EventResponse>> BuildManyAsync(
        AppDbContext db, IReadOnlyList<Event> events, CancellationToken ct)
    {
        if (events.Count == 0) return new List<EventResponse>();

        var eventIds = events.Select(e => e.Id).ToList();
        var typeIds = events.Select(e => e.EventTypeId).Distinct().ToList();
        var seasonIds = events.Where(e => e.SeasonId.HasValue)
            .Select(e => e.SeasonId!.Value).Distinct().ToList();

        var types = await db.EventTypes.AsNoTracking()
            .Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        var seasons = seasonIds.Count == 0
            ? new Dictionary<Guid, Season>()
            : await db.Seasons.AsNoTracking()
                .Where(s => seasonIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

        var pivots = await db.EventOpportunityCategories.AsNoTracking()
            .Where(p => eventIds.Contains(p.EventId))
            .ToListAsync(ct);
        var catIds = pivots.Select(p => p.OpportunityCategoryId).Distinct().ToList();
        var cats = catIds.Count == 0
            ? new Dictionary<Guid, OpportunityCategory>()
            : await db.OpportunityCategories.AsNoTracking()
                .Where(c => catIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);
        var catsByEvent = pivots
            .GroupBy(p => p.EventId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => cats.GetValueOrDefault(p.OpportunityCategoryId))
                      .Where(c => c is not null)
                      .Select(c => c!)
                      .ToList());

        return events.Select(e => Map(
            e,
            types.GetValueOrDefault(e.EventTypeId),
            e.SeasonId.HasValue ? seasons.GetValueOrDefault(e.SeasonId.Value) : null,
            catsByEvent.GetValueOrDefault(e.Id) ?? new List<OpportunityCategory>())).ToList();
    }

    public static async Task<EventResponse> BuildAsync(
        AppDbContext db, Event @event, CancellationToken ct)
        => (await BuildManyAsync(db, new[] { @event }, ct))[0];

    private static EventResponse Map(
        Event e, EventType? type, Season? season, IReadOnlyList<OpportunityCategory> cats) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        Size = e.Size,
        SizeLabel = e.Size,
        Classification = e.Classification,
        StartDate = e.StartDate?.ToString("yyyy-MM-dd"),
        EndDate = e.EndDate?.ToString("yyyy-MM-dd"),
        Status = e.Status.ToWire(),
        StatusLabel = e.Status.Label(),
        StatusIcon = string.Empty,
        CardType = e.Status.CardType(),
        Lat = e.Latitude?.ToString(CultureInfo.InvariantCulture),
        Lon = e.Longitude?.ToString(CultureInfo.InvariantCulture),
        LocationTitle = e.LocationTitle,
        MinAttendees = e.MinAttendees,
        MaxAttendees = e.MaxAttendees,
        StepsDone = e.StepsDone,
        CanEnd = e.CanEnd(),
        Type = type is null ? null : new EventTypeDto
        {
            Id = type.Id,
            Name = type.Name,
            Description = type.Description,
            Icon = type.Icon,
            Background = type.Background,
        },
        Season = season is null ? null : new EventSeasonDto { Id = season.Id, Name = season.Name },
        OpportunityCategories = cats.Select(c => new EventCategoryDto
        {
            Id = c.Id,
            Title = c.Title,
            Description = c.Description,
            Icon = c.Icon,
            ForVacancy = c.ForVacancy,
            IsOther = c.IsOther,
        }).ToList(),
    };
}
