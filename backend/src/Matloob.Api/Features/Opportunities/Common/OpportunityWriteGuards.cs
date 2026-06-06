using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Common;

/// <summary>
/// Bundles the establishment-context + membership + suspension checks
/// every opportunity mutation endpoint needs. Returns a resolved
/// <see cref="Guid"/> on success; <c>null</c> when one of the checks
/// failed and a response has already been written.
/// </summary>
internal static class OpportunityWriteGuards
{
    /// <summary>
    /// Resolve the establishment context (path / query / header / auto-pick),
    /// confirm the caller is an active member or admin, and reject writes
    /// against a Suspended establishment with 423.
    ///
    /// When <paramref name="permission"/> is supplied, the caller must also
    /// hold that permission slug for their active role (Owner is implicit-all);
    /// a member lacking it gets 403. When it is <c>null</c> the legacy
    /// any-active-member rule applies (used by the establishment-as-applicant
    /// apply endpoint, which the per-role map does not cover).
    /// </summary>
    public static async Task<Guid?> AuthoriseMutationAsync(
        AppDbContext db,
        HttpContext httpContext,
        string subClaim,
        CancellationToken ct,
        string? permission = null)
    {
        var establishmentId = await EstablishmentContextHelper
            .ResolveAsync(db, httpContext, subClaim, ct);
        if (establishmentId is null) return null;

        var isAdmin = MembershipChecks.IsAdmin(httpContext.User);
        if (!isAdmin)
        {
            var isMember = await MembershipChecks.IsActiveMemberAsync(
                db, establishmentId.Value, subClaim, ct);
            if (!isMember)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                await httpContext.Response.WriteAsync(string.Empty, ct);
                return null;
            }

            // Per-role write gate. A member without the required permission is
            // forbidden (403), distinct from the 404 a non-member gets above.
            if (permission is not null &&
                !await MembershipChecks.HasPermissionAsync(
                    db, establishmentId.Value, subClaim, permission, ct))
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsync(string.Empty, ct);
                return null;
            }
        }

        // Establishment must not be suspended for writes. 423 distinguishes
        // it from 4xx state-machine errors so the public frontend can show
        // "ask support" vs "wrong state."
        var status = await db.Establishments
            .AsNoTracking()
            .Where(e => e.Id == establishmentId.Value)
            .Select(e => (EstablishmentStatus?)e.Status)
            .FirstOrDefaultAsync(ct);
        if (status == EstablishmentStatus.Suspended)
        {
            await ProblemWriter.WriteAsync(
                httpContext,
                StatusCodes.Status423Locked,
                OpportunityErrorCodes.EstablishmentSuspended,
                "Establishment is suspended; mutation actions are blocked.",
                ct);
            return null;
        }
        if (status is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsync(string.Empty, ct);
            return null;
        }

        return establishmentId;
    }
}
