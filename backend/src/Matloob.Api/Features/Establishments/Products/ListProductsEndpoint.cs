using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Products;

/// <summary>
/// <c>GET /api/establishments/me/products</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/products</c>) — list the
/// resolved establishment's products. Any active member or admin.
/// </summary>
public sealed class ListProductsEndpoint : EndpointWithoutRequest<IReadOnlyList<ProductResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListProductsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/products",
            "/api/v1/establishments/{establishmentId}/products");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<ProductResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishment Products"));
        Summary(s => s.Summary = "List the resolved establishment's products.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForReadAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var rows = await _db.Products
            .AsNoTracking()
            .Where(p => p.EstablishmentId == establishmentId.Value)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Description))
            .ToListAsync(ct);

        await Send.OkAsync(rows, ct);
    }
}
