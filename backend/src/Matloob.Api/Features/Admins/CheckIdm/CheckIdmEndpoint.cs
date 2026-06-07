using FastEndpoints;
using Matloob.Api.Features.Admins.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity.AdminApi;

namespace Matloob.Api.Features.Admins.CheckIdm;

/// <summary>
/// <c>GET /api/v1/admin/admins/check-idm?email=</c> — the create form's
/// "Check IdM" action. Looks the email up in IdM; the UI uses the result to
/// switch between "link existing identity" and "create new" on save. Port of
/// the legacy <c>checkIdm</c> suffix action.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class CheckIdmEndpoint
    : Endpoint<CheckIdmRequest, CheckIdmResult>
{
    private readonly IIdentityAdminApi _idm;

    public CheckIdmEndpoint(IIdentityAdminApi idm)
    {
        _idm = idm;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/admins/check-idm");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<CheckIdmResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Admin.Admins"));
        Summary(s => s.Summary = "Look up an email in IdM before creating an admin.");
    }

    public override async Task HandleAsync(CheckIdmRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
        {
            AddError(r => r.Email, "Email is required.");
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        try
        {
            var user = await _idm.FindUserAsync(req.Email.Trim(), ct);
            if (user is null)
            {
                await Send.OkAsync(new CheckIdmResult(false, null, null, null, Array.Empty<string>(), false), ct);
                return;
            }

            var roles = user.Roles ?? Array.Empty<string>();
            await Send.OkAsync(new CheckIdmResult(
                Found: true,
                Id: user.Id,
                Email: user.Email,
                Name: user.Name,
                Roles: roles,
                AlreadyAdmin: roles.Contains("matloob_admin")), ct);
        }
        catch (IdentityAdminApiException ex)
        {
            await AdminProblem.IdmAsync(HttpContext, ex, ct);
        }
    }
}

public sealed class CheckIdmRequest
{
    [BindFrom("email")]
    public string? Email { get; init; }
}
