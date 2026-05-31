using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Services;

/// <summary>
/// <c>GET /api/establishments/me/services</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/services</c>) — list the
/// resolved establishment's services. Any active member or admin.
/// </summary>
public sealed class ListServicesEndpoint : EndpointWithoutRequest<IReadOnlyList<ServiceResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListServicesEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/services",
            "/api/v1/establishments/{establishmentId}/services");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<ServiceResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishment Services"));
        Summary(s => s.Summary = "List the resolved establishment's services.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForReadAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var rows = await _db.Services
            .AsNoTracking()
            .Where(s => s.EstablishmentId == establishmentId.Value)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ServiceResponse(s.Id, s.Name, s.Description))
            .ToListAsync(ct);

        await Send.OkAsync(rows, ct);
    }
}
