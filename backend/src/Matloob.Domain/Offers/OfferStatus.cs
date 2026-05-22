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
