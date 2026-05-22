using Matloob.Domain.Common;

namespace Matloob.Domain.Offers;

/// <summary>
/// Offer aggregate root. Mirrors the legacy Laravel <c>offers</c> table
/// with every Ajeer / contract / invoice column removed
/// (see docs/25-ajeer-disposition.md). The state machine is internal-only
/// — no external integrations, no contract issuance.
///
/// <para>
/// <b>Sponsor flow</b> is kept (per Q-SPONSOR-KEEP). When
/// <see cref="SponsorEstablishmentId"/> is non-null on Create, the offer
/// starts in <see cref="OfferStatus.PendingSponsorApproval"/> and must
/// pass through <see cref="SponsorApprove"/> before reaching
/// <see cref="OfferStatus.Pending"/>.
/// </para>
///
/// <para>
/// <b>Two-step cancellation</b> stays (per Q-OFFER-2). Either party opens
/// an <see cref="OfferCancellationRequest"/> via
/// <see cref="RequestCancellation"/>; the other party calls
/// <see cref="ApproveCancellation"/> or <see cref="RejectCancellation"/>.
/// </para>
///
/// <para>
/// <b>Expiry</b> is computed on read (Q-OFFER-EXPIRY default) — there is
/// no background job. <see cref="MarkExpired"/> is provided so a future
/// scheduled sweep can flip the column if Product asks for it later.
/// </para>
/// </summary>
public sealed class Offer : BaseAuditableEntity<Guid>, IAggregateRoot
{
    // --- Required relationships ---------------------------------------------
    public Guid SenderEstablishmentId { get; private set; }
    public Guid OpportunityId { get; private set; }
    public Guid ApplicationId { get; private set; }

    // --- Optional relationships ---------------------------------------------
    public Guid? JobTitleId { get; private set; }
    public Guid? JobTitleCategoryId { get; private set; }
    public Guid? SponsorEstablishmentId { get; private set; }
    public string? AppliedByUserId { get; private set; }
    public string? SentByUserId { get; private set; }
    public Guid? OfferRejectionReasonId { get; private set; }

    // --- Monetary -----------------------------------------------------------
    public int? DailyWage { get; private set; }
    public int? NumberOfWorkingDays { get; private set; }
    public decimal? MonthlySalary { get; private set; }
    public Currency Currency { get; private set; } = Currency.SAR;

    // --- Time windows -------------------------------------------------------
    public DateTimeOffset? OfferValidityFrom { get; private set; }
    public DateTimeOffset? OfferValidityTo { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    // --- Content ------------------------------------------------------------
    public string? LaborerCommitments { get; private set; }
    public string? OtherDetails { get; private set; }
    public string? OtherRejectionReason { get; private set; }

    // --- Lifecycle ----------------------------------------------------------
    public OfferStatus Status { get; private set; }

    /// <summary>
    /// New column (Q-OFFER-1): timestamp of the final acceptance — set
    /// when the offer reaches <see cref="OfferStatus.Accepted"/> from
    /// either the applicant accept path or the sponsor-then-applicant
    /// path. Replaces the nested <c>contract.created_at</c> the public
    /// frontend used to read.
    /// </summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    private Offer() { }

    /// <summary>
    /// Factory used by the send-offer endpoint. Auto-computes the
    /// initial status from whether a sponsor is involved.
    /// </summary>
    public static Offer Create(
        Guid id,
        Guid senderEstablishmentId,
        Guid opportunityId,
        Guid applicationId,
        string sentByUserId,
        DateTimeOffset offerValidityFrom,
        DateTimeOffset offerValidityTo,
        DateOnly startDate,
        DateOnly endDate,
        decimal monthlySalary,
        Currency currency = Currency.SAR,
        Guid? jobTitleId = null,
        Guid? jobTitleCategoryId = null,
        Guid? sponsorEstablishmentId = null,
        string? appliedByUserId = null,
        int? dailyWage = null,
        int? numberOfWorkingDays = null,
        string? laborerCommitments = null,
        string? otherDetails = null)
    {
        if (string.IsNullOrWhiteSpace(sentByUserId))
        {
            throw new ArgumentException("Sender user id is required.", nameof(sentByUserId));
        }
        if (offerValidityTo <= offerValidityFrom)
        {
            throw new ArgumentException("offer_validity_to must be after offer_validity_from.", nameof(offerValidityTo));
        }
        if (endDate < startDate)
        {
            throw new ArgumentException("end_date must be on or after start_date.", nameof(endDate));
        }
        if (monthlySalary < 0)
        {
            throw new ArgumentException("monthly_salary must be non-negative.", nameof(monthlySalary));
        }

        var initialStatus = sponsorEstablishmentId is not null
            ? OfferStatus.PendingSponsorApproval
            : OfferStatus.Pending;

        return new Offer
        {
            Id = id,
            SenderEstablishmentId = senderEstablishmentId,
            OpportunityId = opportunityId,
            ApplicationId = applicationId,
            JobTitleId = jobTitleId,
            JobTitleCategoryId = jobTitleCategoryId,
            SponsorEstablishmentId = sponsorEstablishmentId,
            AppliedByUserId = appliedByUserId,
            SentByUserId = sentByUserId,
            DailyWage = dailyWage,
            NumberOfWorkingDays = numberOfWorkingDays,
            MonthlySalary = monthlySalary,
            Currency = currency,
            OfferValidityFrom = offerValidityFrom,
            OfferValidityTo = offerValidityTo,
            StartDate = startDate,
            EndDate = endDate,
            LaborerCommitments = laborerCommitments,
            OtherDetails = otherDetails,
            Status = initialStatus,
        };
    }

    // --- State transitions --------------------------------------------------

    public void Accept(DateTimeOffset at)
    {
        if (Status != OfferStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot accept an offer in status {Status}.");
        }
        Status = OfferStatus.Accepted;
        AcceptedAt = at;
    }

    public void Reject(Guid rejectionReasonId, string? otherReason = null)
    {
        if (Status != OfferStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot reject an offer in status {Status}.");
        }
        Status = OfferStatus.Rejected;
        OfferRejectionReasonId = rejectionReasonId;
        OtherRejectionReason = string.IsNullOrWhiteSpace(otherReason) ? null : otherReason.Trim();
    }

    /// <summary>
    /// Opens a cancellation request on an already-Accepted offer. The
    /// child <see cref="OfferCancellationRequest"/> row is created by the
    /// endpoint after this method returns. Sponsor-aware: when a sponsor
    /// is attached, the request transitions to
    /// <see cref="OfferStatus.PendingSponsorCancellationApproval"/> first.
    /// </summary>
    public void RequestCancellation()
    {
        if (Status != OfferStatus.Accepted)
        {
            throw new InvalidOperationException(
                $"Only Accepted offers can be cancelled (currently {Status}).");
        }
        Status = SponsorEstablishmentId is not null
            ? OfferStatus.PendingSponsorCancellationApproval
            : OfferStatus.CancellationRequested;
    }

    public void ApproveCancellation()
    {
        if (Status != OfferStatus.CancellationRequested
         && Status != OfferStatus.PendingSponsorCancellationApproval)
        {
            throw new InvalidOperationException(
                $"Cannot approve cancellation in status {Status}.");
        }
        Status = OfferStatus.Canceled;
    }

    public void RejectCancellation()
    {
        if (Status != OfferStatus.CancellationRequested
         && Status != OfferStatus.PendingSponsorCancellationApproval)
        {
            throw new InvalidOperationException(
                $"Cannot reject cancellation in status {Status}.");
        }
        // Cancellation rejected — return to Accepted; a fresh request can
        // be opened later.
        Status = OfferStatus.Accepted;
    }

    public void SponsorApprove()
    {
        if (Status != OfferStatus.PendingSponsorApproval)
        {
            throw new InvalidOperationException(
                $"Cannot sponsor-approve an offer in status {Status}.");
        }
        Status = OfferStatus.Pending;
    }

    public void SponsorReject(Guid rejectionReasonId, string? otherReason = null)
    {
        if (Status != OfferStatus.PendingSponsorApproval)
        {
            throw new InvalidOperationException(
                $"Cannot sponsor-reject an offer in status {Status}.");
        }
        Status = OfferStatus.SponsorRejected;
        OfferRejectionReasonId = rejectionReasonId;
        OtherRejectionReason = string.IsNullOrWhiteSpace(otherReason) ? null : otherReason.Trim();
    }

    /// <summary>
    /// Manual expiry hook for a future background sweep
    /// (Q-OFFER-EXPIRY). Today expiry is computed on read; this method
    /// exists so the slot is reserved.
    /// </summary>
    public void MarkExpired()
    {
        if (Status != OfferStatus.Pending && Status != OfferStatus.PendingSponsorApproval)
        {
            throw new InvalidOperationException(
                $"Cannot expire an offer in status {Status}.");
        }
        Status = OfferStatus.Expired;
    }

    /// <summary>
    /// Move from <see cref="OfferStatus.WaitingForEvaluation"/> to
    /// <see cref="OfferStatus.Completed"/> once both parties have
    /// evaluated each other. Caller is responsible for verifying the
    /// preconditions; this just flips the column.
    /// </summary>
    public void MarkCompleted()
    {
        if (Status != OfferStatus.WaitingForEvaluation)
        {
            throw new InvalidOperationException(
                $"Cannot complete an offer in status {Status}.");
        }
        Status = OfferStatus.Completed;
    }
}
