using FastEndpoints;
using Matloob.Api.Features.Admins.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Admins.Get;

/// <summary>
/// <c>GET /api/v1/admin/admins/{id}</c> — single admin for the view/edit
/// screen. Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class GetAdminEndpoint : EndpointWithoutRequest<AdminDetail>
{
    private readonly AppDbContext _db;

    public GetAdminEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/admins/{id:guid}");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<AdminDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Admin.Admins"));
        Summary(s => s.Summary = "Get a single back-office admin user.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var admin = await _db.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (admin is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        await Send.OkAsync(AdminDetail.From(admin), ct);
    }
}
