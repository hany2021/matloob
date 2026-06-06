using System.Security.Cryptography;
using System.Text;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Members.Common;

/// <summary>
/// Shared helpers for the invite-by-email endpoints: raw-token generation +
/// hashing, and the "caller may manage members AND the establishment is
/// Approved" gate that every invitation-management endpoint runs.
/// </summary>
internal static class InvitationSupport
{
    /// <summary>A URL-safe base64 token from 32 random bytes. Never persisted.</summary>
    public static string NewRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>SHA-256 hex digest (64 lower-case chars) of the raw token.</summary>
    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>
    /// Resolve the establishment for the current request, require the caller
    /// to hold <see cref="Permissions.Members.Manage"/> (Owner) or be an
    /// admin, and require <see cref="EstablishmentStatus.Approved"/>. On any
    /// failure the problem response is written and <c>null</c> is returned;
    /// the caller short-circuits. Returns the tracked establishment on success
    /// (callers read <see cref="Establishment.Name"/> for the invite email).
    /// </summary>
    public static async Task<Establishment?> ResolveApprovedForManageAsync(
        AppDbContext db,
        HttpContext http,
        string sub,
        CancellationToken ct)
    {
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(db, http, sub, ct);
        if (establishmentId is null)
        {
            return null;
        }

        if (!MembershipChecks.IsAdmin(http.User) &&
            !await MembershipChecks.HasPermissionAsync(
                db, establishmentId.Value, sub, Permissions.Members.Manage, ct))
        {
            await WriteEmptyAsync(http, StatusCodes.Status403Forbidden, ct);
            return null;
        }

        var establishment = await db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == establishmentId.Value, ct);
        if (establishment is null)
        {
            await WriteEmptyAsync(http, StatusCodes.Status404NotFound, ct);
            return null;
        }

        if (await EstablishmentStatusGuards.WriteIfSuspendedAsync(http, establishment, ct))
        {
            return null;
        }

        if (establishment.Status != EstablishmentStatus.Approved)
        {
            await ProblemWriter.WriteAsync(http, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Invitations require Status=Approved. Current: {establishment.Status}.",
                ct);
            return null;
        }

        return establishment;
    }

    private static async Task WriteEmptyAsync(HttpContext http, int statusCode, CancellationToken ct)
    {
        http.Response.StatusCode = statusCode;
        await http.Response.WriteAsync(string.Empty, ct);
    }
}
