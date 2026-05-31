using Matloob.Domain.Establishments;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Shared authorization for establishment-owned profile resources (services,
/// products, …). Resolves the establishment context (route / query / header /
/// auto-pick via <see cref="EstablishmentContextHelper"/>) and confirms the
/// caller is an active member or admin. Returns the resolved establishment id,
/// or <c>null</c> when a check failed and a response has already been written.
/// </summary>
internal static class EstablishmentResourceGuards
{
    /// <summary>
    /// Reads are allowed for any active member/admin, including while the
    /// establishment is suspended.
    /// </summary>
    public static async Task<Guid?> ResolveForReadAsync(
        AppDbContext db, HttpContext http, string subClaim, CancellationToken ct)
    {
        var establishmentId = await EstablishmentContextHelper
            .ResolveAsync(db, http, subClaim, ct);
        if (establishmentId is null) return null;

        var allowed = MembershipChecks.IsAdmin(http.User)
            || await MembershipChecks.IsActiveMemberAsync(db, establishmentId.Value, subClaim, ct);
        if (!allowed)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            await http.Response.WriteAsync(string.Empty, ct);
            return null;
        }

        return establishmentId;
    }

    /// <summary>
    /// Mutations additionally require the establishment not be suspended (423),
    /// matching the opportunities write rules.
    /// </summary>
    public static async Task<Guid?> ResolveForWriteAsync(
        AppDbContext db, HttpContext http, string subClaim, CancellationToken ct)
    {
        var establishmentId = await ResolveForReadAsync(db, http, subClaim, ct);
        if (establishmentId is null) return null;

        var status = await db.Establishments
            .AsNoTracking()
            .Where(e => e.Id == establishmentId.Value)
            .Select(e => (EstablishmentStatus?)e.Status)
            .FirstOrDefaultAsync(ct);
        if (status == EstablishmentStatus.Suspended)
        {
            await ProblemWriter.WriteAsync(
                http,
                StatusCodes.Status423Locked,
                "establishment_suspended",
                "Establishment is suspended; mutation actions are blocked.",
                ct);
            return null;
        }

        return establishmentId;
    }
}
