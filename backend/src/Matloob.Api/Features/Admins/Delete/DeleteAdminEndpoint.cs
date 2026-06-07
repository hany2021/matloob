using FastEndpoints;
using Matloob.Api.Features.Admins.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity.AdminApi;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Features.Admins.Delete;

/// <summary>
/// <c>DELETE /api/v1/admin/admins/{id}</c> — remove an admin. Removes the
/// <c>matloob_admin</c> role in IdM FIRST (so they lose admin SSO immediately);
/// only if that succeeds do we soft-delete the local row (sets is_deleted).
/// If IdM refuses, the local delete is blocked and the error surfaces.
///
/// Note (TPH): admins share the <c>users</c> table, so the soft-delete hides
/// the row from all queries. The IdM identity itself is NOT deleted/deactivated
/// — only its admin role is revoked.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class DeleteAdminEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly IIdentityAdminApi _idm;
    private readonly IdentityAdminApiOptions _options;

    public DeleteAdminEndpoint(AppDbContext db, IIdentityAdminApi idm, IOptions<IdentityAdminApiOptions> options)
    {
        _db = db;
        _idm = idm;
        _options = options.Value;
    }

    public override void Configure()
    {
        Delete("/api/v1/admin/admins/{id:guid}");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Admin.Admins"));
        Summary(s => s.Summary = "Remove an admin (revokes matloob_admin in IdM, then soft-deletes the row).");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (admin is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var adminRole = string.IsNullOrWhiteSpace(_options.AdminRole) ? "matloob_admin" : _options.AdminRole;

        if (!string.IsNullOrWhiteSpace(admin.IdentityId))
        {
            try
            {
                // Revoke only the admin role — the identity (and any user role) stays.
                await _idm.UpdateUserRolesAsync(admin.IdentityId!, Array.Empty<string>(), new[] { adminRole }, ct);
            }
            catch (IdentityAdminApiException ex)
            {
                // IdM refused — block the local delete so the two stay in sync.
                await AdminProblem.IdmAsync(HttpContext, ex, ct);
                return;
            }
        }

        _db.Admins.Remove(admin); // soft-delete (is_deleted = true) via the global interceptor
        await _db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
