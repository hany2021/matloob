using Matloob.Domain.Offers;

namespace Matloob.Api.Features.Admins.Common;

/// <summary>
/// The offer statuses that count as a "contract" for the admin screens. In the
/// Ajeer-stripped model an accepted <see cref="Matloob.Domain.Offers.Offer"/>
/// IS the contract (there is no separate contracts table), so the set is
/// "Accepted and everything downstream of it" — i.e. an offer that reached a
/// binding state at least once. Shared by the admin contracts list and the
/// establishments list's <c>contracts_count</c>.
/// </summary>
public static class ContractStatuses
{
    public static readonly OfferStatus[] Set =
    [
        OfferStatus.Accepted,
        OfferStatus.CancellationRequested,
        OfferStatus.PendingSponsorCancellationApproval,
        OfferStatus.WaitingForEvaluation,
        OfferStatus.Completed,
    ];
}
