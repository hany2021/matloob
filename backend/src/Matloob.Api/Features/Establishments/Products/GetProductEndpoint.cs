using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Products;

/// <summary>
/// <c>GET /api/establishments/me/products/{id}</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/products/{id}</c>) — fetch one
/// product owned by the resolved establishment. 404 if it belongs to another.
/// </summary>
public sealed class GetProductEndpoint : EndpointWithoutRequest<ProductResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetProductEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/products/{id}",
            "/api/v1/establishments/{establishmentId}/products/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<ProductResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishment Products"));
        Summary(s => s.Summary = "Fetch one of the resolved establishment's products.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForReadAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var row = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id && p.EstablishmentId == establishmentId.Value)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Description))
            .FirstOrDefaultAsync(ct);

        if (row is null) { await Send.NotFoundAsync(ct); return; }
        await Send.OkAsync(row, ct);
    }
}
