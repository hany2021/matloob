namespace Matloob.Domain.Offers;

/// <summary>
/// Canonical outbox <c>event_type</c> string constants for the Offer
/// aggregate. Wire these into <c>IOutboxWriter.Enqueue</c> when a
/// lifecycle transition happens. DO NOT rename — downstream subscribers
/// key off the literal value.
/// </summary>
public static class OfferEventTypes
{
    public const string Created                     = "offer.created";
    public const string Accepted                    = "offer.accepted";
    public const string Rejected                    = "offer.rejected";
    public const string CancellationRequested       = "offer.cancellation_requested";
    public const string CancellationApproved        = "offer.cancellation_approved";
    public const string CancellationRejected        = "offer.cancellation_rejected";
    public const string SponsorApprovalPending      = "offer.sponsor_approval_pending";
    public const string SponsorApproved             = "offer.sponsor_approved";
    public const string SponsorRejected             = "offer.sponsor_rejected";
    public const string Expired                     = "offer.expired";
    public const string Completed                   = "offer.completed";
}
