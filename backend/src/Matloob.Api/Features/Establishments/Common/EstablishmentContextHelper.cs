using Matloob.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Glue between an endpoint's <see cref="HttpContext"/> and
/// <see cref="EstablishmentContextResolver"/>. Handles the three
/// outcomes (Resolved, NotFound, Ambiguous) by either returning the id
/// or writing a problem response and returning null. Endpoints call
/// this once and short-circuit when null is returned.
///
/// <para>
/// Canonical routes (with <c>{establishmentId}</c> in the path) read
/// the id from the route directly via <see cref="ResolveAsync"/>'s
/// optional override; legacy routes (no id in path) fall through to
/// the resolver chain (query → header → auto-pick).
/// </para>
/// </summary>
internal static class EstablishmentContextHelper
{
    /// <summary>
    /// Stable problem-code for the ambiguous-membership 400 response.
    /// The public frontend keys off this constant; DO NOT rename.
    /// </summary>
    public const string EstablishmentContextRequiredCode = "establishment_context_required";


    /// <summary>
    /// Resolve the establishment id for the current request. Returns
    /// null and writes a problem response when the resolution failed
    /// (400 ambiguous, 404 not-found, 401 unauthenticated). Caller
    /// short-circuits when null.
    /// </summary>
    public static async Task<Guid?> ResolveAsync(
        AppDbContext db,
        HttpContext httpContext,
        string? subClaim,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(subClaim))
        {
            await WriteEmptyAsync(httpContext, StatusCodes.Status401Unauthorized, ct);
            return null;
        }

        // Canonical route: id is in the path.
        if (httpContext.Request.RouteValues.TryGetValue("establishmentId", out var routeVal)
            && Guid.TryParse(routeVal?.ToString(), out var fromRoute))
        {
            return fromRoute;
        }

        // Legacy route: query → header → auto-pick.
        var result = await EstablishmentContextResolver.ResolveAsync(
            httpContext, db, subClaim, ct);

        switch (result.Outcome)
        {
            case EstablishmentContextOutcome.Resolved:
                return result.EstablishmentId;

            case EstablishmentContextOutcome.NotFound:
                await WriteEmptyAsync(httpContext, StatusCodes.Status404NotFound, ct);
                return null;

            case EstablishmentContextOutcome.Ambiguous:
                await WriteAmbiguousProblemAsync(httpContext, ct);
                return null;

            default:
                await WriteEmptyAsync(httpContext, StatusCodes.Status500InternalServerError, ct);
                return null;
        }
    }

    private static async Task WriteEmptyAsync(HttpContext httpContext, int statusCode, CancellationToken ct)
    {
        httpContext.Response.StatusCode = statusCode;
        // Force the response to be considered "started" so FastEndpoints'
        // post-handler conventions don't substitute a different code.
        await httpContext.Response.WriteAsync(string.Empty, ct);
    }

    private static async Task WriteAmbiguousProblemAsync(HttpContext httpContext, CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = "The caller is a member of multiple establishments. " +
                     "Specify ?establishment_id={guid} or the X-Establishment-Id header.",
            Type = "https://httpstatuses.io/400",
        };
        problem.Extensions["code"] = EstablishmentContextRequiredCode;
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
    }
}
