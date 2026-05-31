using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.Products;

/// <summary>
/// <c>POST /api/establishments/me/products</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/products</c>) — create a product
/// for the resolved establishment. The frontend posts multipart/form-data, so
/// form binding is enabled. Returns 201 with the created resource.
/// </summary>
public sealed class CreateProductEndpoint : Endpoint<ProductUpsertRequest, ProductResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CreateProductEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Post(
            "/api/establishments/me/products",
            "/api/v1/establishments/{establishmentId}/products");
        Policies(MatloobPolicies.User);
        AllowFormData();
        Description(b => b
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Products"));
        Summary(s => s.Summary = "Create a product for the resolved establishment.");
    }

    public override async Task HandleAsync(ProductUpsertRequest req, CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var product = new Product(Guid.NewGuid(), establishmentId.Value, req.Name!, req.Description!);
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        var response = new ProductResponse(product.Id, product.Name, product.Description);
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/products/{product.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}
