using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>DELETE /api/establishments/events/{id}</c> (+ canonical) — soft-delete an
/// event owned by the resolved establishment. Returns 204.
/// </summary>
public sealed class DeleteEventEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DeleteEventEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Delete(
            "/api/establishments/events/{id:guid}",
            "/api/v1/establishments/{establishmentId}/events/{id:guid}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "Delete one of the resolved establishment's events.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, Infrastructure.Auth.Permissions.Events.Manage, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var @event = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == id && e.EstablishmentId == establishmentId.Value, ct);
        if (@event is null) { await Send.NotFoundAsync(ct); return; }

        _db.Events.Remove(@event);
        await _db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
