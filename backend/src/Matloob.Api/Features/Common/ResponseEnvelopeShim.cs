using System.Collections;
using Microsoft.AspNetCore.Http;

namespace Matloob.Api.Features.Common;

/// <summary>
/// Global response-envelope shim. The public Next.js frontend is typed
/// <c>ApiResponse&lt;T&gt; = { data: T }</c> (single) and
/// <c>ApiResponseWithPagination&lt;T&gt; = { data, meta, links }</c> (lists),
/// but the migrated API mostly returns bare bodies. Wired as the FastEndpoints
/// global <c>ResponseSerializer</c> (see <c>Program.cs</c>), this wraps 2xx JSON
/// bodies on the public-frontend route families so every read parses on the
/// frontend without touching individual endpoints.
///
/// Scope (user-approved): the entire API surface (every <c>/api/*</c> route —
/// legacy <c>/api/users/*</c> + <c>/api/establishments/*</c>, canonical
/// <c>/api/v1/*</c> including admin, system, assets metadata and init-data) is
/// wrapped uniformly, so the resource shape is identical everywhere. DTOs
/// marked <see cref="IBypassEnvelope"/> (already-enveloped or
/// intentionally-bare, e.g. notifications <c>unread-count</c>) are never
/// touched, and binary downloads bypass this serializer entirely.
/// </summary>
public static class ResponseEnvelopeShim
{
    /// <summary>
    /// Returns the object to serialize: either <paramref name="dto"/> unchanged
    /// (passthrough) or wrapped in a <c>{ data }</c> / <c>{ data, meta, links }</c>
    /// envelope.
    /// </summary>
    public static object? Wrap(HttpContext ctx, object? dto)
    {
        if (dto is null)
            return null;

        // Only successful bodies; errors flow through ProblemDetails untouched.
        var status = ctx.Response.StatusCode;
        if (status is < 200 or >= 300)
            return dto;

        // Already-enveloped or intentionally-bare DTOs opt out.
        if (dto is IBypassEnvelope)
            return dto;

        // Wrap the whole API surface uniformly. (All FastEndpoints routes live
        // under /api/; the guard keeps any stray non-API responses untouched.)
        if (!ShouldWrap(ctx.Request.Path.Value))
            return dto;

        // A collection becomes a paginated list envelope; anything else a
        // single-resource { data } envelope. Strings are scalars, not lists.
        if (dto is IEnumerable and not string)
            return BuildListEnvelope(ctx, dto);

        return new DataEnvelope<object>(dto);
    }

    private static bool ShouldWrap(string? path)
        => !string.IsNullOrEmpty(path)
           && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

    private static PaginationEnvelope BuildListEnvelope(HttpContext ctx, object collection)
    {
        var total = Count(collection);
        var page = ParsePage(ctx);
        var path = ctx.Request.Path.Value ?? string.Empty;

        // The migrated list endpoints return the full result set in one page, so
        // a single-page meta is truthful and the frontend's
        // current_page < last_page infinite-scroll guard terminates correctly.
        return new PaginationEnvelope
        {
            Data = collection,
            Meta = new PaginationMeta
            {
                CurrentPage = page,
                From = total == 0 ? 0 : 1,
                LastPage = 1,
                Links = [],
                Path = path,
                PerPage = total == 0 ? 15 : total,
                To = total,
                Total = total,
            },
            Links = new PaginationLinks
            {
                First = $"{path}?page=1",
                Last = $"{path}?page=1",
                Prev = null,
                Next = null,
            },
        };
    }

    private static int Count(object collection)
    {
        if (collection is ICollection c)
            return c.Count;

        var n = 0;
        foreach (var _ in (IEnumerable)collection)
            n++;
        return n;
    }

    private static int ParsePage(HttpContext ctx)
        => int.TryParse(ctx.Request.Query["page"], out var p) && p > 0 ? p : 1;
}
