using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Services;

/// <summary>
/// <c>DELETE /api/establishments/me/services/{id}</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/services/{id}</c>) — remove a
/// service owned by the resolved establishment (soft-delete). Returns 204.
/// </summary>
public sealed class DeleteServiceEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DeleteServiceEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Delete(
            "/api/establishments/me/services/{id}",
            "/api/v1/establishments/{establishmentId}/services/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Services"));
        Summary(s => s.Summary = "Delete one of the resolved establishment's services.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, Infrastructure.Auth.Permissions.Profile.Edit, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var service = await _db.Services
            .FirstOrDefaultAsync(s => s.Id == id && s.EstablishmentId == establishmentId.Value, ct);
        if (service is null) { await Send.NotFoundAsync(ct); return; }

        _db.Services.Remove(service);
        await _db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
