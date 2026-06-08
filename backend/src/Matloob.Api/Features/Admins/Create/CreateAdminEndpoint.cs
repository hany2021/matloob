using FastEndpoints;
using Matloob.Api.Features.Admins.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity.AdminApi;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Admins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Features.Admins.Create;

/// <summary>
/// <c>POST /api/v1/admin/admins</c> — create (or link) an admin user. Faithful
/// port of the legacy <c>CreateAdmin::handleRecordCreation</c> saga:
///
/// <list type="number">
///   <item>Validate email (domain allow-list) + name, and that no local admin
///     already uses the email.</item>
///   <item>Resolve the IdM identity: by the id the "Check IdM" step found, else
///     by email. If it exists, add the <c>matloob_admin</c> role (if missing);
///     otherwise create a fresh AD-login IdM identity (UserName
///     <c>{sam}@{ad-domain}</c>, no password) with the <c>matloob_admin</c> role.</item>
///   <item>Do the IdM mutation FIRST and capture a compensator, then insert the
///     local row. If the local insert fails, run the compensator to revert IdM
///     before surfacing the error.</item>
/// </list>
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class CreateAdminEndpoint
    : Endpoint<CreateAdminRequest, AdminDetail>
{
    private readonly AppDbContext _db;
    private readonly IIdentityAdminApi _idm;
    private readonly IdentityAdminApiOptions _options;
    private readonly ILogger<CreateAdminEndpoint> _logger;

    public CreateAdminEndpoint(
        AppDbContext db,
        IIdentityAdminApi idm,
        IOptions<IdentityAdminApiOptions> options,
        ILogger<CreateAdminEndpoint> logger)
    {
        _db = db;
        _idm = idm;
        _options = options.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/api/v1/admin/admins");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<AdminDetail>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Admin.Admins"));
        Summary(s => s.Summary = "Create or link a back-office admin user (IdM-backed).");
    }

    public override async Task HandleAsync(CreateAdminRequest req, CancellationToken ct)
    {
        var email = req.Email?.Trim() ?? string.Empty;
        var name = req.Name?.Trim() ?? string.Empty;

        // --- Validation (422, mirrors the legacy form rules) -----------------
        if (string.IsNullOrWhiteSpace(email))
            AddError(r => r.Email, "Email is required.");
        else if (!AdminEmailDomain.IsAllowed(email))
            AddError(r => r.Email, $"Email must be on an allowed domain: @{AdminEmailDomain.AllowedDisplay}.");

        if (string.IsNullOrWhiteSpace(name))
            AddError(r => r.Name, "Name is required.");

        // No email-uniqueness check here: the identity (sub) is the key, and the
        // local upsert below promotes an existing row rather than duplicating it.

        if (ValidationFailures.Count > 0)
        {
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        var adminRole = string.IsNullOrWhiteSpace(_options.AdminRole) ? "matloob_admin" : _options.AdminRole;

        // --- Resolve IdM identity + perform the IdM-side mutation ------------
        string identityId;
        Func<CancellationToken, Task>? compensate;
        try
        {
            var existing = !string.IsNullOrWhiteSpace(req.IdmExistingUserId)
                ? await _idm.FindUserAsync(req.IdmExistingUserId!, ct)
                : await _idm.FindUserAsync(email, ct);

            if (existing is not null)
            {
                var rolesToAdd = (existing.Roles ?? Array.Empty<string>()).Contains(adminRole)
                    ? Array.Empty<string>()
                    : new[] { adminRole };

                if (rolesToAdd.Length > 0)
                {
                    await _idm.UpdateUserRolesAsync(existing.Id, rolesToAdd, Array.Empty<string>(), ct);
                }

                identityId = existing.Id;
                // Trust IdM's canonical email/name.
                if (!string.IsNullOrWhiteSpace(existing.Email)) email = existing.Email!;
                if (!string.IsNullOrWhiteSpace(existing.Name)) name = existing.Name!;

                compensate = rolesToAdd.Length == 0
                    ? null
                    : c => _idm.UpdateUserRolesAsync(existing.Id, Array.Empty<string>(), rolesToAdd, c);
            }
            else
            {
                // New identity → create it as an AD-login user: UserName must be
                // {sam}@{ad-domain} (the AD marker) and NO local password. The
                // email stays {sam}@nec.gov.sa (already validated above).
                var sam = LocalPart(email);
                var adDomain = string.IsNullOrWhiteSpace(_options.ActiveDirectoryDomain)
                    ? "nec.lcl"
                    : _options.ActiveDirectoryDomain;
                var adUserName = $"{sam}@{adDomain}";

                var created = await _idm.CreateUserAsync(new CreateIdmUserPayload(
                    Email: email,
                    UserName: adUserName,
                    Name: name,
                    Roles: new[] { adminRole },
                    Password: null,              // AD users authenticate against the domain — no password
                    IsActiveDirectory: true,
                    SamAccountName: sam), ct);

                identityId = created.Id;
                compensate = c => _idm.DeleteUserAsync(created.Id, c);
            }
        }
        catch (IdentityAdminApiException ex)
        {
            await AdminProblem.IdmAsync(HttpContext, ex, ct);
            return;
        }

        // --- Local upsert (TPH): promote an existing users row to Admin, else
        //     insert a fresh Admin row. Revert the IdM change on failure. -------
        try
        {
            var existingId = await _db.Users.AsNoTracking().IgnoreQueryFilters()
                .Where(u => u.IdentityId == identityId)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(ct);

            if (existingId is null)
            {
                var admin = new Admin(Guid.NewGuid(), identityId, email, name);
                _db.Admins.Add(admin);
                await _db.SaveChangesAsync(ct);
                await Send.ResponseAsync(AdminDetail.From(admin), StatusCodes.Status201Created, ct);
                return;
            }

            // A row already exists for this identity (a public user, or an admin
            // auto-synced on login). Promote it to Admin. TPH discriminator
            // changes aren't supported on tracked entities, so do it via SQL;
            // also reactivate + un-soft-delete and adopt the canonical name/email.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE users SET user_type = 'Admin', is_active = true, is_deleted = false, name = {name}, email = {email} WHERE identity_id = {identityId}",
                ct);

            await Send.OkAsync(new AdminDetail(existingId.Value, name, email, true, identityId), ct);
        }
        catch (Exception)
        {
            if (compensate is not null)
            {
                try { await compensate(ct); }
                catch (Exception compErr)
                {
                    _logger.LogError(compErr,
                        "IdM compensation failed after local admin upsert failure for {Email}", email);
                }
            }
            throw;
        }
    }

    /// <summary>The sam-account-name = the local part of the email (before '@').</summary>
    private static string LocalPart(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }
}

public sealed class CreateAdminRequest
{
    public string? Email { get; init; }
    public string? Name { get; init; }

    /// <summary>Optional IdM id from a prior "Check IdM" lookup; if absent the
    /// server resolves the identity by email.</summary>
    public string? IdmExistingUserId { get; init; }
}
