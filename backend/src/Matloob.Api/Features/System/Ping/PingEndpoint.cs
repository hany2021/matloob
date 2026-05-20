using FastEndpoints;

namespace Matloob.Api.Features.System.Ping;

/// <summary>
/// Framework smoke endpoint. Confirms FastEndpoints discovery, routing,
/// model binding (response only), and OpenAPI document generation.
/// Not a business endpoint. Replace or remove once real endpoints exist.
/// </summary>
public sealed class PingEndpoint : EndpointWithoutRequest<PingResponse>
{
    public override void Configure()
    {
        Get("/api/v1/system/ping");
        AllowAnonymous();
        Description(b => b
            .Produces<PingResponse>(StatusCodes.Status200OK)
            .WithName("SystemPing")
            .WithTags("System"));
        Summary(s =>
        {
            s.Summary = "Liveness ping for the API framework.";
            s.Description = "Returns { status: \"ok\" } once routing + FastEndpoints + OpenAPI are wired.";
        });
    }

    public override Task HandleAsync(CancellationToken ct) =>
        Send.OkAsync(new PingResponse("ok"), ct);
}

public sealed record PingResponse(string Status);
