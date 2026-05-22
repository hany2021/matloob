using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Shared establishment-context resolver for legacy routes that don't
/// carry the establishment id in the URL path (the Laravel
/// <c>/api/establishments/*</c> endpoints relied on the
/// <c>X-Commissioner-UUID</c> header). The new API picks the
/// establishment via, in order:
///
/// <list type="number">
///   <item>query parameter <c>?establishment_id={guid}</c></item>
///   <item>header <c>X-Establishment-Id: {guid}</c></item>
///   <item>auto-resolution: exactly one active membership in an
///     Approved/Suspended establishment.</item>
/// </list>
///
/// <para>
/// <see cref="ResolveAsync"/> returns one of three outcomes encoded in
/// <see cref="EstablishmentContextResult"/>: Resolved (single Guid),
/// NotFound (no membership), or Ambiguous (multiple memberships and no
/// explicit disambiguator). The endpoint maps Ambiguous to 400 with code
/// <c>establishment_context_required</c> so the frontend can render a
/// picker.
/// </para>
/// </summary>
internal static class EstablishmentContextResolver
{
    public const string QueryKey = "establishment_id";
    public const string HeaderName = "X-Establishment-Id";

    public static async Task<EstablishmentContextResult> ResolveAsync(
        HttpContext httpContext,
        AppDbContext db,
        string subClaim,
        CancellationToken ct)
    {
        if (TryReadExplicit(httpContext) is { } explicitGuid)
        {
            return EstablishmentContextResult.Resolved(explicitGuid);
        }

        // Auto-resolve from the caller's active memberships restricted to
        // readable establishment statuses (Approved + Suspended).
        var candidates = await db.EstablishmentMembers
            .AsNoTracking()
            .Where(m => m.UserId == subClaim && m.IsActive)
            .Join(db.Establishments.AsNoTracking(),
                m => m.EstablishmentId, e => e.Id,
                (m, e) => new { e.Id, e.Status })
            .Where(x => x.Status == EstablishmentStatus.Approved
                     || x.Status == EstablishmentStatus.Suspended)
            .Select(x => x.Id)
            .Take(2)
            .ToListAsync(ct);

        return candidates.Count switch
        {
            0 => EstablishmentContextResult.NotFound(),
            1 => EstablishmentContextResult.Resolved(candidates[0]),
            _ => EstablishmentContextResult.Ambiguous(),
        };
    }

    public static Guid? TryReadExplicit(HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue(QueryKey, out var qsValue)
            && Guid.TryParse(qsValue.ToString(), out var fromQuery))
        {
            return fromQuery;
        }
        if (httpContext.Request.Headers.TryGetValue(HeaderName, out var hdrValue)
            && Guid.TryParse(hdrValue.ToString(), out var fromHeader))
        {
            return fromHeader;
        }
        return null;
    }
}

internal readonly struct EstablishmentContextResult
{
    public EstablishmentContextOutcome Outcome { get; }
    public Guid EstablishmentId { get; }

    private EstablishmentContextResult(EstablishmentContextOutcome outcome, Guid id)
    {
        Outcome = outcome;
        EstablishmentId = id;
    }

    public static EstablishmentContextResult Resolved(Guid id) =>
        new(EstablishmentContextOutcome.Resolved, id);
    public static EstablishmentContextResult NotFound() =>
        new(EstablishmentContextOutcome.NotFound, Guid.Empty);
    public static EstablishmentContextResult Ambiguous() =>
        new(EstablishmentContextOutcome.Ambiguous, Guid.Empty);
}

internal enum EstablishmentContextOutcome
{
    Resolved,
    NotFound,
    Ambiguous,
}
