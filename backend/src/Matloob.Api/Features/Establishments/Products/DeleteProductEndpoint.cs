using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Products;

/// <summary>
/// <c>DELETE /api/establishments/me/products/{id}</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/products/{id}</c>) — remove a
/// product owned by the resolved establishment (soft-delete). Returns 204.
/// </summary>
public sealed class DeleteProductEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DeleteProductEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Delete(
            "/api/establishments/me/products/{id}",
            "/api/v1/establishments/{establishmentId}/products/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Products"));
        Summary(s => s.Summary = "Delete one of the resolved establishment's products.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, Infrastructure.Auth.Permissions.Profile.Edit, ct);
        if (establishmentId is null) return;

        var id = Route<Guid>("id");
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.EstablishmentId == establishmentId.Value, ct);
        if (product is null) { await Send.NotFoundAsync(ct); return; }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
