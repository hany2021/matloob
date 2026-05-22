using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Shared establishment-context resolver for legacy routes that don't
/// carry the establishment id in the URL path. Picks the establishment
/// via the following priority chain:
///
/// <list type="number">
///   <item>query parameter <c>?establishment_id={guid}</c> (new
///     canonical alias);</item>
///   <item>header <c>X-Establishment-Id: {guid}</c> (new canonical
///     alias);</item>
///   <item>header <c>X-Commissioner-UUID: {guid}</c> — Laravel legacy
///     "commissioner" header. The new system does not have a
///     <i>commissioner</i> table; we treat the GUID as an
///     <see cref="Establishment"/> id directly, exactly like
///     <c>X-Establishment-Id</c>. The endpoint's downstream membership
///     check decides whether the caller can use that id (404 for
///     non-members, 423 for Suspended, etc.);</item>
///   <item>auto-resolution: exactly one active membership in an
///     Approved/Suspended establishment.</item>
/// </list>
///
/// <para>
/// <see cref="ResolveAsync"/> returns one of three outcomes encoded in
/// <see cref="EstablishmentContextResult"/>: Resolved, NotFound, or
/// Ambiguous. The endpoint maps Ambiguous to 400 with code
/// <c>establishment_context_required</c>.
/// </para>
/// </summary>
internal static class EstablishmentContextResolver
{
    public const string QueryKey = "establishment_id";
    public const string HeaderName = "X-Establishment-Id";
    public const string LegacyCommissionerHeaderName = "X-Commissioner-UUID";

    public static async Task<EstablishmentContextResult> ResolveAsync(
        HttpContext httpContext,
        AppDbContext db,
        string subClaim,
        CancellationToken ct)
    {
        // Priority 1+2+3: explicit query / X-Establishment-Id header /
        // X-Commissioner-UUID header. TryReadExplicit returns the first
        // one it finds. Whether the caller actually has membership on
        // that id is enforced by the endpoint's own membership check —
        // not here — matching the existing X-Establishment-Id behavior.
        if (TryReadExplicit(httpContext) is { } explicitGuid)
        {
            return EstablishmentContextResult.Resolved(explicitGuid);
        }

        // Priority 4: auto-resolve from active memberships restricted to
        // readable statuses (Approved + Suspended).
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
        // Legacy Laravel commissioner header — treated as an establishment
        // id alias. The endpoint's downstream membership check enforces
        // permission, so a bogus value falls through to 404 like any
        // other unknown id.
        if (httpContext.Request.Headers.TryGetValue(LegacyCommissionerHeaderName, out var legacyValue)
            && Guid.TryParse(legacyValue.ToString(), out var fromLegacy))
        {
            return fromLegacy;
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
