using System.Text.Json.Serialization;
using Matloob.Api.Features.Applications.Common;
using Matloob.Api.Features.Opportunities.Common;

namespace Matloob.Api.Features.Offers.Common;

/// <summary>
/// Wire shape returned by every offer read / write endpoint. Mirrors the
/// Laravel <c>OfferResource</c> field-for-field MINUS every
/// Ajeer / contract / invoice column (no <c>contract</c>,
/// <c>contract_path</c>, <c>notice_path</c>, <c>show_print_notice</c>,
/// <c>contract_type</c>, <c>contract_type_label</c>,
/// <c>ajeer_*</c>) per <c>docs/25-ajeer-disposition.md</c>.
///
/// <para>
/// New top-level field: <c>accepted_at</c> — replaces the dropped nested
/// <c>contract.created_at</c> so the public frontend can render the
/// "Accepted on …" timestamp without the contract row (Q-OFFER-1).
/// </para>
/// </summary>
public sealed class OfferResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("sender")]
    public OfferSenderDto? Sender { get; init; }

    [JsonPropertyName("applicant")]
    public OpportunityApplicationResponse? Applicant { get; init; }

    [JsonPropertyName("opportunity")]
    public OpportunityResponse? Opportunity { get; init; }

    [JsonPropertyName("job_title")]
    public OfferJobTitleDto? JobTitle { get; init; }

    [JsonPropertyName("monthly_salary")]
    public decimal? MonthlySalary { get; init; }

    [JsonPropertyName("daily_wage")]
    public int? DailyWage { get; init; }

    [JsonPropertyName("number_of_working_days")]
    public int? NumberOfWorkingDays { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "SAR";

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("status_color")]
    public string? StatusColor { get; init; }

    [JsonPropertyName("status_label")]
    public string? StatusLabel { get; init; }

    [JsonPropertyName("offer_validity_from")]
    public string? OfferValidityFrom { get; init; }

    [JsonPropertyName("offer_validity_to")]
    public string? OfferValidityTo { get; init; }

    /// <summary>Alias of <c>offer_validity_to</c>, kept for Laravel parity.</summary>
    [JsonPropertyName("expiry_date")]
    public string? ExpiryDate { get; init; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; init; }

    [JsonPropertyName("end_date")]
    public string? EndDate { get; init; }

    [JsonPropertyName("other_details")]
    public string? OtherDetails { get; init; }

    [JsonPropertyName("laborer_commitments")]
    public string? LaborerCommitments { get; init; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Computed: now &gt; offer_validity_to AND status is non-terminal.</summary>
    [JsonPropertyName("expired")]
    public bool Expired { get; init; }

    /// <summary>True when the applicant + sender both have an Evaluation for this offer.</summary>
    [JsonPropertyName("evaluated")]
    public bool Evaluated { get; init; }

    /// <summary><c>"user"</c> or <c>"organization"</c> when a cancellation request exists, else null.</summary>
    [JsonPropertyName("cancelled_by")]
    public string? CancelledBy { get; init; }

    [JsonPropertyName("cancellation_request")]
    public OfferCancellationRequestDto? CancellationRequest { get; init; }

    [JsonPropertyName("applied_by")]
    public ApplicationAppliedByDto? AppliedBy { get; init; }

    [JsonPropertyName("sent_by")]
    public ApplicationAppliedByDto? SentBy { get; init; }

    [JsonPropertyName("is_pending_sponsor_approval")]
    public bool IsPendingSponsorApproval { get; init; }

    /// <summary>
    /// New top-level Laravel-replacement (Q-OFFER-1) — the timestamp
    /// the offer transitioned to <see cref="Matloob.Domain.Offers.OfferStatus.Accepted"/>.
    /// Null until acceptance.
    /// </summary>
    [JsonPropertyName("accepted_at")]
    public DateTimeOffset? AcceptedAt { get; init; }
}

public sealed class OfferSenderDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

public sealed class OfferJobTitleDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class OfferCancellationRequestDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("requested_by_type")]
    public string RequestedByType { get; init; } = string.Empty;

    [JsonPropertyName("requested_by_id")]
    public string RequestedById { get; init; } = string.Empty;

    [JsonPropertyName("reason_id")]
    public Guid? ReasonId { get; init; }

    [JsonPropertyName("other_reason")]
    public string? OtherReason { get; init; }

    [JsonPropertyName("is_approved")]
    public bool IsApproved { get; init; }

    [JsonPropertyName("is_rejected")]
    public bool IsRejected { get; init; }

    [JsonPropertyName("requested_at")]
    public DateTimeOffset RequestedAt { get; init; }

    [JsonPropertyName("reviewed_at")]
    public DateTimeOffset? ReviewedAt { get; init; }
}
