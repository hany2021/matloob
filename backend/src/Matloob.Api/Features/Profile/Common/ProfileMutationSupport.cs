using System.Text;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.Common;

/// <summary>
/// Shared helpers for the user-profile mutator endpoints.
/// </summary>
public static class ProfileMutationSupport
{
    /// <summary>
    /// Laravel Precognition pre-validation requests carry a <c>Precognition</c>
    /// header. FastEndpoints has already run the request validator by the time
    /// the handler executes, so when this returns true the handler should stop
    /// BEFORE mutating and reply 204 — the client only wanted validation.
    /// </summary>
    public static bool IsPrecognitive(HttpContext ctx)
        => ctx.Request.Headers.ContainsKey("Precognition");

    /// <summary>
    /// Recompute and persist the <c>profile_completed</c> flag from the four
    /// weighted sections (mirrors Laravel <c>UserSupport</c>). Call on the
    /// tracked user before SaveChanges.
    /// </summary>
    public static async Task RecomputeProfileCompletedAsync(
        AppDbContext db, User user, CancellationToken ct)
    {
        var userId = user.Id;
        var hasBank = await db.BankAccounts.AnyAsync(x => x.UserId == userId, ct);
        var hasEducation = await db.UserEducation.AnyAsync(x => x.UserId == userId, ct);
        var hasProfessions = await db.UserProfessions.AnyAsync(x => x.UserId == userId, ct);

        var personalInfoDone = hasBank
            && !string.IsNullOrEmpty(user.Bio)
            && !string.IsNullOrEmpty(user.Phone)
            && !string.IsNullOrEmpty(user.AdditionalPhone)
            && !string.IsNullOrEmpty(user.Email);

        var done = personalInfoDone
            && hasEducation
            && hasProfessions
            && user.YearsOfExperience > 0;

        user.SetProfileCompleted(done);
    }
}

/// <summary>
/// Minimal IBAN validation (ISO 13616 mod-97 check + optional SA-length
/// guard). The legacy Laravel rule also cross-checked the IBAN against the
/// chosen bank's identifier; the new <c>Bank</c> reference table carries no
/// identifier column, so that bank-specific check is dropped — format +
/// checksum is enforced here.
/// </summary>
public static class IbanValidator
{
    public static bool IsValid(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return false;

        var normalized = iban.Replace(" ", string.Empty).ToUpperInvariant();
        if (normalized.Length is < 15 or > 34) return false;
        foreach (var c in normalized)
        {
            if (!char.IsLetterOrDigit(c)) return false;
        }

        // Move the first four chars to the end, then convert to digits.
        var rearranged = normalized[4..] + normalized[..4];
        var sb = new StringBuilder(rearranged.Length * 2);
        foreach (var c in rearranged)
        {
            sb.Append(char.IsDigit(c) ? c.ToString() : (c - 'A' + 10).ToString());
        }

        // mod 97 over the (potentially long) numeric string.
        var remainder = 0;
        foreach (var ch in sb.ToString())
        {
            remainder = (remainder * 10 + (ch - '0')) % 97;
        }

        return remainder == 1;
    }
}
