using Matloob.Domain.Common;

namespace Matloob.Domain.Offers;

/// <summary>
/// Child of the <see cref="Offer"/> aggregate. Represents one
/// cancellation request opened on an Accepted offer; the other party
/// approves (-> Offer.Canceled) or rejects (-> Offer.Accepted, allowing
/// a fresh request later).
///
/// <para>
/// Inherits <see cref="BaseEntity{TId}"/> (not Auditable) — the row is
/// append-only after Approve/Reject and does not participate in the
/// soft-delete sweep. The parent Offer carries the audit.
/// </para>
///
/// <para>
/// "Requester" is split-FK (user / establishment) like
/// <see cref="Matloob.Domain.Applications.OpportunityApplication"/> —
/// see the CHECK constraint in the EF config. The Laravel morph
/// (<c>requested_by_type</c> / <c>requested_by_id</c>) is replaced.
/// </para>
/// </summary>
public sealed class OfferCancellationRequest : BaseEntity<Guid>
{
    public Guid OfferId { get; private set; }

    /// <summary>IdM sub claim — non-null when a worker opened the request.</summary>
    public string? RequestedByUserId { get; private set; }

    /// <summary>Establishment id — non-null when an organization opened the request.</summary>
    public Guid? RequestedByEstablishmentId { get; private set; }

    public Guid? OfferCancellationReasonId { get; private set; }
    public string? OtherReason { get; private set; }
    public bool IsApproved { get; private set; }
    public bool IsRejected { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewedByUserId { get; private set; }

    private OfferCancellationRequest() { }

    public static OfferCancellationRequest ByUser(
        Guid id,
        Guid offerId,
        string requestedByUserId,
        Guid? cancellationReasonId,
        string? otherReason,
        DateTimeOffset requestedAt)
    {
        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            throw new ArgumentException("Requester user id is required.", nameof(requestedByUserId));
        }
        return new OfferCancellationRequest
        {
            Id = id,
            OfferId = offerId,
            RequestedByUserId = requestedByUserId,
            RequestedByEstablishmentId = null,
            OfferCancellationReasonId = cancellationReasonId,
            OtherReason = string.IsNullOrWhiteSpace(otherReason) ? null : otherReason.Trim(),
            IsApproved = false,
            IsRejected = false,
            RequestedAt = requestedAt,
        };
    }

    public static OfferCancellationRequest ByEstablishment(
        Guid id,
        Guid offerId,
        Guid requestedByEstablishmentId,
        Guid? cancellationReasonId,
        string? otherReason,
        DateTimeOffset requestedAt)
    {
        return new OfferCancellationRequest
        {
            Id = id,
            OfferId = offerId,
            RequestedByUserId = null,
            RequestedByEstablishmentId = requestedByEstablishmentId,
            OfferCancellationReasonId = cancellationReasonId,
            OtherReason = string.IsNullOrWhiteSpace(otherReason) ? null : otherReason.Trim(),
            IsApproved = false,
            IsRejected = false,
            RequestedAt = requestedAt,
        };
    }

    public void Approve(string reviewedByUserId, DateTimeOffset at)
    {
        if (IsApproved || IsRejected)
        {
            throw new InvalidOperationException("Cancellation request is already closed.");
        }
        IsApproved = true;
        ReviewedAt = at;
        ReviewedByUserId = reviewedByUserId;
    }

    public void Reject(string reviewedByUserId, DateTimeOffset at)
    {
        if (IsApproved || IsRejected)
        {
            throw new InvalidOperationException("Cancellation request is already closed.");
        }
        IsRejected = true;
        ReviewedAt = at;
        ReviewedByUserId = reviewedByUserId;
    }
}
