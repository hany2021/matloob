using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Services;

/// <summary>
/// <c>PATCH /api/establishments/me/services/{id}</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/services/{id}</c>) — update a
/// service owned by the resolved establishment. Returns 200 with the resource.
/// </summary>
public sealed class UpdateServiceEndpoint : Endpoint<ServiceUpsertRequest, ServiceResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateServiceEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.PATCH, Http.PUT);
        Routes(
            "/api/establishments/me/services/{id}",
            "/api/v1/establishments/{establishmentId}/services/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<ServiceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Services"));
        Summary(s => s.Summary = "Update one of the resolved establishment's services.");
    }

    public override async Task HandleAsync(ServiceUpsertRequest req, CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, Infrastructure.Auth.Permissions.Profile.Edit, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var service = await _db.Services
            .FirstOrDefaultAsync(s => s.Id == id && s.EstablishmentId == establishmentId.Value, ct);
        if (service is null) { await Send.NotFoundAsync(ct); return; }

        service.Update(req.Name!, req.Description!);
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(new ServiceResponse(service.Id, service.Name, service.Description), ct);
    }
}
