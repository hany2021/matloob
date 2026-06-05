namespace Matloob.Domain.Offers;

/// <summary>
/// Lifecycle of an offer in the Ajeer-stripped model. Matches the Laravel
/// OfferStatus minus the two Ajeer-only states (Processing,
/// PendingPayment). See docs/25-ajeer-disposition.md and Q-OFFER-1 /
/// Q-OFFER-2 in docs/40-api-migration-readiness.md.
/// </summary>
public enum OfferStatus
{
    /// <summary>Just sent; awaiting applicant action.</summary>
    Pending = 0,

    /// <summary>Applicant (and sponsor, if any) accepted. Internal-only — no contract issued.</summary>
    Accepted = 1,

    /// <summary>Applicant or sender rejected.</summary>
    Rejected = 2,

    /// <summary>Cancellation finalised (other party approved).</summary>
    Canceled = 3,

    /// <summary>Cancellation request opened — awaiting the other party's response.</summary>
    CancellationRequested = 4,

    /// <summary>Sponsor must accept before the offer is visible to the applicant.</summary>
    PendingSponsorApproval = 5,

    /// <summary>Sponsor must accept the cancellation request before the offer is canceled.</summary>
    PendingSponsorCancellationApproval = 6,

    /// <summary>Sponsor rejected the offer.</summary>
    SponsorRejected = 7,

    /// <summary>Offer validity window passed without any action — derived/projected status.</summary>
    Expired = 8,

    /// <summary>Job ended; one or both parties still owe an evaluation.</summary>
    WaitingForEvaluation = 9,

    /// <summary>Both parties evaluated; offer is closed for good.</summary>
    Completed = 10,
}

/// <summary>
/// Named status sets. Mirrors the legacy static helpers on the Laravel
/// <c>OfferStatus</c> enum (e.g. <c>activeOfferStatues()</c>).
/// </summary>
public static class OfferStatusSets
{
    /// <summary>
    /// Statuses in which an applicant is considered to "hold" the slot —
    /// legacy <c>OfferStatus::activeOfferStatues()</c> minus the dropped Ajeer
    /// <c>PROCESSING</c>. Used to count filled personnel (opportunity
    /// fulfillment) and to exclude already-served applicants from the
    /// "opportunity fulfilled/expired" fanout.
    /// </summary>
    public static readonly OfferStatus[] Active =
    [
        OfferStatus.Accepted,
        OfferStatus.CancellationRequested,
        OfferStatus.PendingSponsorCancellationApproval,
    ];
}

/// <summary>
/// Maps <see cref="OfferStatus"/> to the lowercase snake_case wire token
/// the public frontend keys its status cards on (e.g.
/// <c>statusConfigs[offer.status]</c>). Mirrors the legacy Laravel
/// <c>OfferStatus</c> backing values — a PascalCase ToString() leaves the
/// applicant with no accept/reject card.
/// </summary>
public static class OfferStatusWire
{
    public static string ToWire(this OfferStatus status) => status switch
    {
        OfferStatus.Pending => "pending",
        OfferStatus.Accepted => "accepted",
        OfferStatus.Rejected => "rejected",
        OfferStatus.Canceled => "canceled",
        OfferStatus.CancellationRequested => "cancellation_requested",
        OfferStatus.PendingSponsorApproval => "pending_sponsor_approval",
        OfferStatus.PendingSponsorCancellationApproval => "pending_sponsor_cancellation_approval",
        OfferStatus.SponsorRejected => "sponsor_rejected",
        OfferStatus.Expired => "expired",
        OfferStatus.WaitingForEvaluation => "waiting_for_evaluation",
        OfferStatus.Completed => "completed",
        _ => "pending",
    };

    /// <summary>
    /// Parses a wire token (from the frontend status filter) back to the
    /// enum. Returns false for unknown tokens so callers can ignore them.
    /// </summary>
    public static bool TryParse(string? token, out OfferStatus status)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "pending": status = OfferStatus.Pending; return true;
            case "accepted": status = OfferStatus.Accepted; return true;
            case "rejected": status = OfferStatus.Rejected; return true;
            case "canceled": status = OfferStatus.Canceled; return true;
            case "cancellation_requested": status = OfferStatus.CancellationRequested; return true;
            case "pending_sponsor_approval": status = OfferStatus.PendingSponsorApproval; return true;
            case "pending_sponsor_cancellation_approval": status = OfferStatus.PendingSponsorCancellationApproval; return true;
            case "sponsor_rejected": status = OfferStatus.SponsorRejected; return true;
            case "expired": status = OfferStatus.Expired; return true;
            case "waiting_for_evaluation": status = OfferStatus.WaitingForEvaluation; return true;
            case "completed": status = OfferStatus.Completed; return true;
            default: status = OfferStatus.Pending; return false;
        }
    }
}
