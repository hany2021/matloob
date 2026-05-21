using Microsoft.AspNetCore.Http;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Tiny helper that writes an application/problem+json response with a
/// machine-readable <c>code</c> extension. Duplicated across every
/// establishment endpoint that returns 409 — keeping it here means a future
/// change to the ProblemDetails shape (e.g. switching the type URI) lands
/// in one place.
/// </summary>
internal static class ProblemWriter
{
    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string detail,
        CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonFor(statusCode),
            Detail = detail,
            Type = $"https://httpstatuses.io/{statusCode}",
        };
        problem.Extensions["code"] = code;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
    }

    private static string ReasonFor(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        409 => "Conflict",
        423 => "Locked",
        _ => "Error",
    };
}
