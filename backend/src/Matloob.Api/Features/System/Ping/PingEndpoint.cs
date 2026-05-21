using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;

namespace Matloob.Api.Features.System.Ping;

/// <summary>
/// Framework smoke endpoint. Confirms FastEndpoints discovery, routing,
/// model binding (response only), OpenAPI document generation, AND now —
/// since Phase 4 commit #4 — the authentication + authorization pipeline.
///
/// Authorized: requires <see cref="MatloobPolicies.User"/> (authenticated
/// bearer + <c>matloob_user</c> role). Anonymous callers receive 401.
/// Authenticated callers without the role receive 403.
///
/// Replace or remove once real business endpoints exist.
/// </summary>
public sealed class PingEndpoint : EndpointWithoutRequest<PingResponse>
{
    public override void Configure()
    {
        Get("/api/v1/system/ping");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<PingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("SystemPing")
            .WithTags("System"));
        Summary(s =>
        {
            s.Summary = "Liveness ping for the API framework.";
            s.Description = "Returns { status: \"ok\" }. Requires a JWT bearer token carrying the `matloob_user` role.";
        });
    }

    public override Task HandleAsync(CancellationToken ct) =>
        Send.OkAsync(new PingResponse("ok"), ct);
}

public sealed record PingResponse(string Status);
