using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>GET /api/establishments/events/types</c> — event-type reference list the
/// event-creation form renders. Global reference data (no establishment
/// context); also available via init-data.
/// </summary>
public sealed class ListEventTypesEndpoint : EndpointWithoutRequest<IReadOnlyList<EventTypeDto>>
{
    private readonly AppDbContext _db;
    public ListEventTypesEndpoint(AppDbContext db) => _db = db;

    public override void Configure()
    {
        Get(
            "/api/establishments/events/types",
            "/api/v1/establishments/{establishmentId}/events/types");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<EventTypeDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "List active event types.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var rows = await _db.EventTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new EventTypeDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Icon = t.Icon,
                Background = t.Background,
            })
            .ToListAsync(ct);
        await Send.OkAsync(rows, ct);
    }
}

/// <summary><c>GET /api/establishments/events/suggested-locations</c>.</summary>
public sealed class ListSuggestedLocationsEndpoint
    : EndpointWithoutRequest<IReadOnlyList<SuggestedLocationDto>>
{
    private readonly AppDbContext _db;
    public ListSuggestedLocationsEndpoint(AppDbContext db) => _db = db;

    public override void Configure()
    {
        Get(
            "/api/establishments/events/suggested-locations",
            "/api/v1/establishments/{establishmentId}/events/suggested-locations");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<SuggestedLocationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "List suggested event locations.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var rows = await _db.SuggestedLocations
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Title)
            .Select(l => new SuggestedLocationDto
            {
                Id = l.Id,
                Title = l.Title,
                Lat = l.Latitude,
                Lon = l.Longitude,
            })
            .ToListAsync(ct);
        await Send.OkAsync(rows, ct);
    }
}

/// <summary><c>GET /api/establishments/events/suggested-attendees</c>.</summary>
public sealed class ListSuggestedAttendeesEndpoint
    : EndpointWithoutRequest<IReadOnlyList<SuggestedAttendeeDto>>
{
    private readonly AppDbContext _db;
    public ListSuggestedAttendeesEndpoint(AppDbContext db) => _db = db;

    public override void Configure()
    {
        Get(
            "/api/establishments/events/suggested-attendees",
            "/api/v1/establishments/{establishmentId}/events/suggested-attendees");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<SuggestedAttendeeDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "List suggested attendee ranges.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var rows = await _db.SuggestedAttendees
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Min)
            .Select(a => new SuggestedAttendeeDto { Id = a.Id, Min = a.Min, Max = a.Max })
            .ToListAsync(ct);
        await Send.OkAsync(rows, ct);
    }
}

public sealed class SuggestedLocationDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("lat")] public decimal Lat { get; init; }
    [JsonPropertyName("lon")] public decimal Lon { get; init; }
}

public sealed class SuggestedAttendeeDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("min")] public int Min { get; init; }
    [JsonPropertyName("max")] public int Max { get; init; }
}
