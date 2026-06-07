using FastEndpoints;
using Matloob.Api.Features.Admins.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity.AdminApi;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Features.Admins.Update;

/// <summary>
/// <c>PUT/PATCH /api/v1/admin/admins/{id}</c> — edit the locally-editable
/// fields (name, is_active). Email is fixed (it keys the IdM identity). Port of
/// the legacy <c>EditAdmin::handleRecordUpdate</c>: if the row is linked to an
/// IdM identity, ensure the <c>matloob_admin</c> role is present (add if
/// missing) BEFORE the local update, with a compensator that reverts IdM if the
/// local write fails. Unlinked legacy rows update locally only.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class UpdateAdminEndpoint
    : Endpoint<UpdateAdminRequest, AdminDetail>
{
    private readonly AppDbContext _db;
    private readonly IIdentityAdminApi _idm;
    private readonly IdentityAdminApiOptions _options;
    private readonly ILogger<UpdateAdminEndpoint> _logger;

    public UpdateAdminEndpoint(
        AppDbContext db,
        IIdentityAdminApi idm,
        IOptions<IdentityAdminApiOptions> options,
        ILogger<UpdateAdminEndpoint> logger)
    {
        _db = db;
        _idm = idm;
        _options = options.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Verbs(Http.PUT, Http.PATCH);
        Routes("/api/v1/admin/admins/{id:guid}");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<AdminDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Admin.Admins"));
        Summary(s => s.Summary = "Update a back-office admin user (name / active).");
    }

    public override async Task HandleAsync(UpdateAdminRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (admin is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var name = req.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            AddError(r => r.Name, "Name is required.");
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        var adminRole = string.IsNullOrWhiteSpace(_options.AdminRole) ? "matloob_admin" : _options.AdminRole;
        Func<CancellationToken, Task>? compensate = null;

        // Linked rows: ensure matloob_admin is present in IdM (saga), then local
        // update. Unlinked legacy rows skip the IdM hop.
        if (!string.IsNullOrWhiteSpace(admin.IdentityId))
        {
            try
            {
                var original = await _idm.FindUserAsync(admin.IdentityId!, ct);
                if (original is null)
                {
                    await AdminProblem.IdmAsync(HttpContext,
                        new IdentityAdminApiException("IdM identity no longer exists for this admin."), ct);
                    return;
                }

                var rolesToAdd = (original.Roles ?? Array.Empty<string>()).Contains(adminRole)
                    ? Array.Empty<string>()
                    : new[] { adminRole };

                if (rolesToAdd.Length > 0)
                {
                    await _idm.UpdateUserRolesAsync(admin.IdentityId!, rolesToAdd, Array.Empty<string>(), ct);
                    compensate = c => _idm.UpdateUserRolesAsync(admin.IdentityId!, Array.Empty<string>(), rolesToAdd, c);
                }
            }
            catch (IdentityAdminApiException ex)
            {
                await AdminProblem.IdmAsync(HttpContext, ex, ct);
                return;
            }
        }

        try
        {
            admin.Rename(name);
            if (req.IsActive) admin.Reactivate(); else admin.Deactivate();
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            if (compensate is not null)
            {
                try { await compensate(ct); }
                catch (Exception compErr)
                {
                    _logger.LogError(compErr,
                        "IdM compensation failed after local admin update failure for {Id}", admin.Id);
                }
            }
            throw;
        }

        await Send.OkAsync(AdminDetail.From(admin), ct);
    }
}

public sealed class UpdateAdminRequest
{
    public string? Name { get; init; }
    public bool IsActive { get; init; }
}
