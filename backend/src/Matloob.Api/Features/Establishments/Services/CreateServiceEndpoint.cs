using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.Services;

/// <summary>
/// <c>POST /api/establishments/me/services</c> (+ canonical
/// <c>/api/v1/establishments/{establishmentId}/services</c>) — create a service
/// for the resolved establishment. Returns 201 with the created resource.
/// </summary>
public sealed class CreateServiceEndpoint : Endpoint<ServiceUpsertRequest, ServiceResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CreateServiceEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Post(
            "/api/establishments/me/services",
            "/api/v1/establishments/{establishmentId}/services");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<ServiceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Services"));
        Summary(s => s.Summary = "Create a service for the resolved establishment.");
    }

    public override async Task HandleAsync(ServiceUpsertRequest req, CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var service = new Service(Guid.NewGuid(), establishmentId.Value, req.Name!, req.Description!);
        _db.Services.Add(service);
        await _db.SaveChangesAsync(ct);

        var response = new ServiceResponse(service.Id, service.Name, service.Description);
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/services/{service.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}
