using System.Text.Json.Serialization;
using Matloob.Api.Features.Opportunities.Common;

namespace Matloob.Api.Features.Evaluations.Common;

/// <summary>
/// Wire shape for evaluation read + create endpoints. Mirrors the
/// Laravel <c>EvaluationResource</c> field-for-field MINUS the
/// <c>contract</c> nested resource (Contracts are gone — evaluations
/// attach to <c>offer_id</c> per Q-EVAL-1).
/// </summary>
public sealed class EvaluationResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("rating")]
    public byte Rating { get; init; }

    [JsonPropertyName("opportunity")]
    public OpportunityResponse? Opportunity { get; init; }

    [JsonPropertyName("offer_id")]
    public Guid OfferId { get; init; }

    [JsonPropertyName("evaluator")]
    public EvaluationPartyDto? Evaluator { get; init; }

    [JsonPropertyName("evaluable")]
    public EvaluationPartyDto? Evaluable { get; init; }

    [JsonPropertyName("recommend_for_future_opportunities")]
    public bool RecommendForFutureOpportunities { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("matching_percentage")]
    public int? MatchingPercentage { get; init; }

    [JsonPropertyName("success_management_criteria_comment")]
    public string? SuccessManagementCriteriaComment { get; init; }

    [JsonPropertyName("uploads")]
    public IReadOnlyList<EvaluationUploadDto> Uploads { get; init; } = [];

    [JsonPropertyName("matloob_evaluation")]
    public byte? MatloobEvaluation { get; init; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;
}

public sealed class EvaluationPartyDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

public sealed class EvaluationUploadDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }
}
