using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Centralized "is this establishment locked for writes?" check, used by
/// every mutation endpoint after the auth gate but before the existing
/// per-endpoint status guard. Spec §8: suspended establishments accept
/// reads but reject all writes with <c>423 Locked</c> +
/// <see cref="EstablishmentErrorCodes.EstablishmentSuspended"/>.
///
/// Returning a distinct <c>423</c> code matters because <c>cannot_edit_in_status</c>
/// already exists for the Draft / PendingReview / Rejected cases — the
/// public frontend needs to be able to tell "suspended (admin-driven, ask
/// support)" apart from "wrong state for this action (user-recoverable)."
/// </summary>
internal static class EstablishmentStatusGuards
{
    /// <summary>
    /// Returns <c>true</c> when the establishment is <see cref="EstablishmentStatus.Suspended"/>
    /// AND the 423 response has been written. The caller must <c>return</c>
    /// without further work.
    /// </summary>
    public static async Task<bool> WriteIfSuspendedAsync(
        HttpContext ctx,
        Establishment establishment,
        CancellationToken ct)
    {
        if (establishment.Status != EstablishmentStatus.Suspended)
        {
            return false;
        }

        await ProblemWriter.WriteAsync(ctx, StatusCodes.Status423Locked,
            EstablishmentErrorCodes.EstablishmentSuspended,
            "Establishment is suspended; mutation actions are blocked.",
            ct);
        return true;
    }

    /// <summary>
    /// Lookup-by-id overload for endpoints that don't already have the
    /// <see cref="Establishment"/> in scope (e.g. change-request mutation
    /// endpoints that only loaded the CR). Returns <c>true</c> when 423 has
    /// been written. If the establishment is missing, returns <c>false</c>
    /// — the caller's own 404 path will handle it.
    /// </summary>
    public static async Task<bool> WriteIfSuspendedAsync(
        AppDbContext db,
        Guid establishmentId,
        HttpContext ctx,
        CancellationToken ct)
    {
        var status = await db.Establishments
            .AsNoTracking()
            .Where(e => e.Id == establishmentId)
            .Select(e => (EstablishmentStatus?)e.Status)
            .FirstOrDefaultAsync(ct);

        if (status != EstablishmentStatus.Suspended)
        {
            return false;
        }

        await ProblemWriter.WriteAsync(ctx, StatusCodes.Status423Locked,
            EstablishmentErrorCodes.EstablishmentSuspended,
            "Establishment is suspended; mutation actions are blocked.",
            ct);
        return true;
    }
}
