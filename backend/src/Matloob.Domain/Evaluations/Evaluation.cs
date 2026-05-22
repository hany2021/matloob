using Matloob.Domain.Common;

namespace Matloob.Domain.Evaluations;

/// <summary>
/// Evaluation aggregate root. Mirrors the legacy Laravel <c>evaluation</c>
/// (singular) table with the following structural changes:
///
/// <list type="bullet">
/// <item>The polymorphic <c>evaluable</c> + <c>evaluator</c> pairs are
///   replaced with 4 explicit FK columns (evaluable_user_id /
///   evaluable_establishment_id, evaluator_user_id /
///   evaluator_establishment_id), with two CHECK constraints enforcing
///   exactly-one per pair.</item>
/// <item>The legacy <c>contract_id</c> FK is REMOVED — Contracts are gone
///   in the new system. Evaluations attach to <see cref="OfferId"/> instead
///   (Q-EVAL-1 in docs/40-api-migration-readiness.md).</item>
/// </list>
///
/// <para>
/// Media uploads (evidence files for establishment evaluations) are
/// captured via <see cref="EvaluationAsset"/> rows, not the legacy Spatie
/// MediaLibrary.
/// </para>
/// </summary>
public sealed class Evaluation : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid OpportunityId { get; private set; }
    public Guid OfferId { get; private set; }

    // Evaluable (who is being evaluated) — split FKs, exactly one.
    public string? EvaluableUserId { get; private set; }
    public Guid? EvaluableEstablishmentId { get; private set; }

    // Evaluator (who is doing the evaluating) — split FKs, exactly one.
    public string? EvaluatorUserId { get; private set; }
    public Guid? EvaluatorEstablishmentId { get; private set; }

    public byte Rating { get; private set; }
    public bool RecommendForFutureOpportunities { get; private set; }
    public byte? MatloobEvaluation { get; private set; }
    public int? MatchingPercentage { get; private set; }
    public string? Comment { get; private set; }
    public string? SuccessManagementCriteriaComment { get; private set; }

    private Evaluation() { }

    public static Evaluation ByUserOfEstablishment(
        Guid id,
        Guid opportunityId,
        Guid offerId,
        string evaluatorUserId,
        Guid evaluableEstablishmentId,
        byte rating,
        bool recommendForFutureOpportunities,
        byte? matloobEvaluation = null,
        int? matchingPercentage = null,
        string? comment = null,
        string? successManagementCriteriaComment = null)
    {
        if (string.IsNullOrWhiteSpace(evaluatorUserId))
        {
            throw new ArgumentException("Evaluator user id is required.", nameof(evaluatorUserId));
        }
        EnsureRatingInRange(rating);
        EnsureMatchingPercentageInRange(matchingPercentage);

        return new Evaluation
        {
            Id = id,
            OpportunityId = opportunityId,
            OfferId = offerId,
            EvaluatorUserId = evaluatorUserId,
            EvaluatorEstablishmentId = null,
            EvaluableUserId = null,
            EvaluableEstablishmentId = evaluableEstablishmentId,
            Rating = rating,
            RecommendForFutureOpportunities = recommendForFutureOpportunities,
            MatloobEvaluation = matloobEvaluation,
            MatchingPercentage = matchingPercentage,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            SuccessManagementCriteriaComment = string.IsNullOrWhiteSpace(successManagementCriteriaComment)
                ? null
                : successManagementCriteriaComment.Trim(),
        };
    }

    public static Evaluation ByEstablishmentOfUser(
        Guid id,
        Guid opportunityId,
        Guid offerId,
        Guid evaluatorEstablishmentId,
        string evaluableUserId,
        byte rating,
        bool recommendForFutureOpportunities,
        byte? matloobEvaluation = null,
        int? matchingPercentage = null,
        string? comment = null,
        string? successManagementCriteriaComment = null)
    {
        if (string.IsNullOrWhiteSpace(evaluableUserId))
        {
            throw new ArgumentException("Evaluable user id is required.", nameof(evaluableUserId));
        }
        EnsureRatingInRange(rating);
        EnsureMatchingPercentageInRange(matchingPercentage);

        return new Evaluation
        {
            Id = id,
            OpportunityId = opportunityId,
            OfferId = offerId,
            EvaluatorUserId = null,
            EvaluatorEstablishmentId = evaluatorEstablishmentId,
            EvaluableUserId = evaluableUserId,
            EvaluableEstablishmentId = null,
            Rating = rating,
            RecommendForFutureOpportunities = recommendForFutureOpportunities,
            MatloobEvaluation = matloobEvaluation,
            MatchingPercentage = matchingPercentage,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            SuccessManagementCriteriaComment = string.IsNullOrWhiteSpace(successManagementCriteriaComment)
                ? null
                : successManagementCriteriaComment.Trim(),
        };
    }

    public static Evaluation ByEstablishmentOfEstablishment(
        Guid id,
        Guid opportunityId,
        Guid offerId,
        Guid evaluatorEstablishmentId,
        Guid evaluableEstablishmentId,
        byte rating,
        bool recommendForFutureOpportunities,
        byte? matloobEvaluation = null,
        int? matchingPercentage = null,
        string? comment = null,
        string? successManagementCriteriaComment = null)
    {
        EnsureRatingInRange(rating);
        EnsureMatchingPercentageInRange(matchingPercentage);

        return new Evaluation
        {
            Id = id,
            OpportunityId = opportunityId,
            OfferId = offerId,
            EvaluatorUserId = null,
            EvaluatorEstablishmentId = evaluatorEstablishmentId,
            EvaluableUserId = null,
            EvaluableEstablishmentId = evaluableEstablishmentId,
            Rating = rating,
            RecommendForFutureOpportunities = recommendForFutureOpportunities,
            MatloobEvaluation = matloobEvaluation,
            MatchingPercentage = matchingPercentage,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            SuccessManagementCriteriaComment = string.IsNullOrWhiteSpace(successManagementCriteriaComment)
                ? null
                : successManagementCriteriaComment.Trim(),
        };
    }

    private static void EnsureRatingInRange(byte rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new ArgumentException("rating must be between 1 and 5.", nameof(rating));
        }
    }

    private static void EnsureMatchingPercentageInRange(int? matchingPercentage)
    {
        if (matchingPercentage is < 0 or > 100)
        {
            throw new ArgumentException(
                "matching_percentage must be between 0 and 100.",
                nameof(matchingPercentage));
        }
    }
}
