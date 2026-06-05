using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Matloob.Domain.Evaluations;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Evaluations.Common;

internal static class EvaluationReadMapper
{
    public static async Task<EvaluationResponse> MapAsync(
        AppDbContext db,
        Evaluation evaluation,
        CancellationToken ct)
    {
        var opportunity = await db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == evaluation.OpportunityId, ct);

        OpportunityResponse? opportunityResponse = null;
        if (opportunity is not null)
        {
            var bundle = await OpportunityReadQueries.LoadSidecarAsync(
                db, opportunity, subClaim: null, establishmentApplicantId: null, ct);
            opportunityResponse = OpportunityReadMapper.Map(
                opportunity, bundle.Category, bundle.Issuer, bundle.Nationality,
                bundle.SuccessCriteria, bundle.Uploads, bundle.ApplicantsCount,
                bundle.IsApplied, bundle.Event);
        }

        var evaluator = await LoadPartyAsync(db,
            evaluation.EvaluatorUserId, evaluation.EvaluatorEstablishmentId, ct);
        var evaluable = await LoadPartyAsync(db,
            evaluation.EvaluableUserId, evaluation.EvaluableEstablishmentId, ct);

        var uploads = await (
            from ea in db.EvaluationAssets.AsNoTracking()
            join a in db.Assets.AsNoTracking() on ea.AssetId equals a.Id
            where ea.EvaluationId == evaluation.Id
            orderby ea.UploadedAt
            select new EvaluationUploadDto
            {
                Id = a.Id,
                FileName = a.OriginalFileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
            }).ToListAsync(ct);

        return new EvaluationResponse
        {
            Id = evaluation.Id,
            Rating = evaluation.Rating,
            Opportunity = opportunityResponse,
            OfferId = evaluation.OfferId,
            Evaluator = evaluator,
            Evaluable = evaluable,
            RecommendForFutureOpportunities = evaluation.RecommendForFutureOpportunities,
            Comment = evaluation.Comment,
            MatchingPercentage = evaluation.MatchingPercentage,
            SuccessManagementCriteriaComment = evaluation.SuccessManagementCriteriaComment,
            Uploads = uploads,
            MatloobEvaluation = evaluation.MatloobEvaluation,
            CreatedAt = evaluation.CreatedAt.ToString("yyyy-MM-dd"),
        };
    }

    private static async Task<EvaluationPartyDto?> LoadPartyAsync(
        AppDbContext db,
        string? userId,
        Guid? establishmentId,
        CancellationToken ct)
    {
        if (userId is not null)
        {
            var u = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdentityId == userId, ct);
            return new EvaluationPartyDto
            {
                Type = "user",
                Id = userId,
                Name = u?.Name,
                Email = u?.Email,
            };
        }
        if (establishmentId is { } eId)
        {
            var e = await db.Establishments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == eId, ct);
            return new EvaluationPartyDto
            {
                Type = "organization",
                Id = eId.ToString(),
                Name = e?.Name,
                Email = e?.Email,
            };
        }
        return null;
    }
}
