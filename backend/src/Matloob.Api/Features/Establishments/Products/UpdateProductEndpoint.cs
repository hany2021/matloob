using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Products;

/// <summary>
/// <c>PATCH /api/establishments/me/products/{id}</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/products/{id}</c>) — update a
/// product owned by the resolved establishment. Returns 200 with the resource.
/// </summary>
public sealed class UpdateProductEndpoint : Endpoint<ProductUpsertRequest, ProductResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateProductEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.PATCH, Http.PUT);
        Routes(
            "/api/establishments/me/products/{id}",
            "/api/v1/establishments/{establishmentId}/products/{id}");
        Policies(MatloobPolicies.User);
        AllowFormData();
        Description(b => b
            .Produces<ProductResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Products"));
        Summary(s => s.Summary = "Update one of the resolved establishment's products.");
    }

    public override async Task HandleAsync(ProductUpsertRequest req, CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.EstablishmentId == establishmentId.Value, ct);
        if (product is null) { await Send.NotFoundAsync(ct); return; }

        product.Update(req.Name!, req.Description!);
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(new ProductResponse(product.Id, product.Name, product.Description), ct);
    }
}
