using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Services;

/// <summary>
/// <c>GET /api/establishments/me/services/{id}</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/services/{id}</c>) — fetch one
/// service owned by the resolved establishment. 404 if it belongs to another.
/// </summary>
public sealed class GetServiceEndpoint : EndpointWithoutRequest<ServiceResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetServiceEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/services/{id}",
            "/api/v1/establishments/{establishmentId}/services/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<ServiceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishment Services"));
        Summary(s => s.Summary = "Fetch one of the resolved establishment's services.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForReadAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var row = await _db.Services
            .AsNoTracking()
            .Where(s => s.Id == id && s.EstablishmentId == establishmentId.Value)
            .Select(s => new ServiceResponse(s.Id, s.Name, s.Description))
            .FirstOrDefaultAsync(ct);

        if (row is null) { await Send.NotFoundAsync(ct); return; }
        await Send.OkAsync(row, ct);
    }
}
