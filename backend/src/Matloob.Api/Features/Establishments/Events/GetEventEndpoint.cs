using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>GET /api/establishments/events/{id}</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/events/{id}</c>) — fetch one
/// event owned by the resolved establishment. <c>{id:guid}</c> keeps the literal
/// sub-routes (types / joined-events / suggested-*) from colliding.
/// </summary>
public sealed class GetEventEndpoint : EndpointWithoutRequest<EventResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetEventEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/events/{id:guid}",
            "/api/v1/establishments/{establishmentId}/events/{id:guid}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EventResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "Fetch one of the resolved establishment's events.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForReadAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var @event = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.EstablishmentId == establishmentId.Value, ct);
        if (@event is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(await EventReadMapper.BuildAsync(_db, @event, ct), ct);
    }
}
