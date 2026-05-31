using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>PATCH /api/establishments/events/{id}/end</c> (+ canonical) — end an
/// active/upcoming event. Returns 400 (<c>event_cannot_be_ended</c>) for events
/// not in an endable state, else 200 with the updated event.
/// </summary>
public sealed class EndEventEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public EndEventEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Verbs(Http.PATCH, Http.POST);
        Routes(
            "/api/establishments/events/{id:guid}/end",
            "/api/v1/establishments/{establishmentId}/events/{id:guid}/end");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EventResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "End an active/upcoming event.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var @event = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == id && e.EstablishmentId == establishmentId.Value, ct);
        if (@event is null) { await Send.NotFoundAsync(ct); return; }

        if (!@event.CanEnd())
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "event_cannot_be_ended",
                "Only active or upcoming events can be ended.",
                ct);
            return;
        }

        @event.End(DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await EventReadMapper.BuildAsync(_db, @event, ct), ct);
    }
}
