using Matloob.Api.Infrastructure.Identity.AdminApi;
using Microsoft.AspNetCore.Http;

namespace Matloob.Api.Features.Admins.Common;

/// <summary>
/// Turns an <see cref="IdentityAdminApiException"/> into a non-2xx JSON body so
/// a transient IdM outage (or a missing config) never surfaces as a 500 or a
/// validation error. 503 when the API isn't configured; 502 when the upstream
/// IdM call failed. Written straight to the response so the global
/// <c>{ data }</c> envelope shim (2xx-only) leaves it untouched.
/// </summary>
internal static class AdminProblem
{
    public static Task IdmAsync(HttpContext ctx, IdentityAdminApiException ex, CancellationToken ct)
    {
        var status = ex.StatusCode is null
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status502BadGateway;
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(
            new { title = "IdM admin API error", detail = ex.Message, status }, ct);
    }
}
