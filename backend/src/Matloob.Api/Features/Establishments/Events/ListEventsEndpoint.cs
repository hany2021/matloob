using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>GET /api/establishments/events</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/events</c>) — the resolved
/// establishment's events grouped by card status (active / upcoming / drafted /
/// ended), mirroring the legacy <c>GroupedEventResource</c>.
/// </summary>
public sealed class ListEventsEndpoint : EndpointWithoutRequest<GroupedEventsResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListEventsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/events",
            "/api/v1/establishments/{establishmentId}/events");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<GroupedEventsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "List the resolved establishment's events grouped by status.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForReadAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var events = await _db.Events
            .AsNoTracking()
            .Where(e => e.EstablishmentId == establishmentId.Value)
            .OrderByDescending(e => e.CreatedAt)
            .Take(1000)
            .ToListAsync(ct);

        var mapped = await EventReadMapper.BuildManyAsync(_db, events, ct);

        // Group by card_type (Finished collapses onto "ended"), then wrap each in
        // the legacy double-nested shape `{ status: { status: { …, data } } }` the
        // frontend reads as data[status][status].
        var byCard = mapped.ToLookup(m => m.CardType);
        var grouped = new GroupedEventsResponse
        {
            Active = Group(EventStatus.Active, "active", byCard),
            Upcoming = Group(EventStatus.Upcoming, "upcoming", byCard),
            Drafted = Group(EventStatus.Drafted, "drafted", byCard),
            Ended = Group(EventStatus.Ended, "ended", byCard),
        };

        await Send.OkAsync(grouped, ct);
    }

    private static Dictionary<string, EventGroup> Group(
        EventStatus status, string cardType, ILookup<string, EventResponse> byCard)
        => new()
        {
            [cardType] = new EventGroup
            {
                StatusLabel = status.Label(),
                StatusIcon = string.Empty,
                CardType = cardType,
                Data = byCard[cardType].ToList(),
            },
        };
}
