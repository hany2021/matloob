using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Applications.Common;

/// <summary>
/// Derives the legacy "application status" the Laravel
/// <c>ApplicantSupport::getApplicationStatus</c> emitted. The Laravel
/// version checked whether any <c>Offer</c> row existed for the
/// (applicant, opportunity) pair — if yes it returned "accepted",
/// otherwise "pending".
///
/// <para>
/// Phase OAO-2 only has read endpoints, so the Offer side of the
/// equation is checked structurally even though the Offer slice's
/// endpoints don't exist yet (the table does).
/// </para>
/// </summary>
internal static class ApplicationStatusComputer
{
    public const string Pending  = "pending";
    public const string Accepted = "accepted";

    /// <summary>
    /// Offer statuses that make the application "accepted": the applicant was
    /// actually accepted/hired. A <see cref="OfferStatus.Pending"/> offer (just
    /// sent, awaiting the applicant) or a Rejected/Expired/Canceled one keeps
    /// the application "pending". (The legacy port returned "accepted" for ANY
    /// offer row, which conflated "offer sent" with "accepted" — wrong in the
    /// new model where offers start in <see cref="OfferStatus.Pending"/>.)
    /// </summary>
    private static readonly OfferStatus[] AcceptedOfferStatuses =
    [
        OfferStatus.Accepted,
        OfferStatus.CancellationRequested,
        OfferStatus.PendingSponsorCancellationApproval,
        OfferStatus.WaitingForEvaluation,
        OfferStatus.Completed,
    ];

    public static async Task<IReadOnlyDictionary<Guid, string>> ComputeForApplicationsAsync(
        AppDbContext db,
        IReadOnlyList<Guid> applicationIds,
        CancellationToken ct)
    {
        if (applicationIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var withOffer = await db.Offers
            .AsNoTracking()
            .Where(o => applicationIds.Contains(o.ApplicationId)
                     && AcceptedOfferStatuses.Contains(o.Status))
            .Select(o => o.ApplicationId)
            .Distinct()
            .ToListAsync(ct);

        var set = new HashSet<Guid>(withOffer);
        return applicationIds.ToDictionary(
            id => id,
            id => set.Contains(id) ? Accepted : Pending);
    }

    public static async Task<string> ComputeForOneAsync(
        AppDbContext db,
        Guid applicationId,
        CancellationToken ct)
    {
        var hasAcceptedOffer = await db.Offers
            .AsNoTracking()
            .AnyAsync(o => o.ApplicationId == applicationId
                        && AcceptedOfferStatuses.Contains(o.Status), ct);
        return hasAcceptedOffer ? Accepted : Pending;
    }
}
