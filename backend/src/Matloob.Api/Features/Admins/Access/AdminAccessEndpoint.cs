using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;

namespace Matloob.Api.Features.Admins.Access;

/// <summary>
/// <c>GET /api/v1/admin/access</c> — lightweight "am I an active admin?" probe
/// for the admin SPA's route guard. Returns 200 only when the caller passes the
/// full <see cref="MatloobPolicies.Admin"/> policy, which now includes the
/// <c>ActiveAdminRequirement</c> (is_active check). A deactivated admin gets
/// 403, so the guard can route them to the forbidden page — the same experience
/// a non-<c>matloob_admin</c> user gets.
/// </summary>
public sealed class AdminAccessEndpoint : EndpointWithoutRequest<AdminAccessResponse>
{
    public override void Configure()
    {
        Get("/api/v1/admin/access");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<AdminAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Admin.Admins"));
        Summary(s => s.Summary = "Returns 200 when the caller is an active admin (used by the SPA guard).");
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(new AdminAccessResponse(true), ct);
}

public sealed record AdminAccessResponse(bool Ok);
