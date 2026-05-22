using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Evaluations.Common;
using Matloob.Api.Features.Evaluations.UserSide;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Evaluations;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Evaluations.EstablishmentSide;

/// <summary><c>GET /api/establishments/evaluations</c> + canonical.</summary>
public sealed class ListEstablishmentEvaluationsEndpoint
    : EndpointWithoutRequest<IReadOnlyList<EvaluationResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListEstablishmentEvaluationsEndpoint(AppDbContext db, ICurrentUser u)
    { _db = db; _currentUser = u; }

    public override void Configure()
    {
        Get("/api/establishments/evaluations",
            "/api/v1/establishments/{establishmentId}/evaluations");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<EvaluationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "List evaluations authored by the establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isMember = await MembershipChecks.IsActiveMemberAsync(_db, establishmentId.Value, sub, ct);
            if (!isMember) { await Send.NotFoundAsync(ct); return; }
        }

        var evals = await _db.Evaluations
            .AsNoTracking()
            .Where(e => e.EvaluatorEstablishmentId == establishmentId.Value)
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var responses = new List<EvaluationResponse>(evals.Count);
        foreach (var ev in evals)
        {
            responses.Add(await EvaluationReadMapper.MapAsync(_db, ev, ct));
        }
        await Send.OkAsync(responses, ct);
    }
}

/// <summary><c>GET /api/establishments/evaluations/{id}</c> + canonical.</summary>
public sealed class GetEstablishmentEvaluationEndpoint
    : EndpointWithoutRequest<EvaluationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetEstablishmentEvaluationEndpoint(AppDbContext db, ICurrentUser u)
    { _db = db; _currentUser = u; }

    public override void Configure()
    {
        Get("/api/establishments/evaluations/{id}",
            "/api/v1/establishments/{establishmentId}/evaluations/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EvaluationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "Detail of one evaluation authored by the establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isMember = await MembershipChecks.IsActiveMemberAsync(_db, establishmentId.Value, sub, ct);
            if (!isMember) { await Send.NotFoundAsync(ct); return; }
        }

        var id = Route<Guid>("id");
        var ev = await _db.Evaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id
                && e.EvaluatorEstablishmentId == establishmentId.Value, ct);
        if (ev is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(await EvaluationReadMapper.MapAsync(_db, ev, ct), ct);
    }
}

/// <summary><c>POST /api/establishments/evaluations</c> + canonical.</summary>
public sealed class CreateEstablishmentEvaluationEndpoint
    : Endpoint<CreateEstablishmentEvaluationRequest, EvaluationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public CreateEstablishmentEvaluationEndpoint(
        AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/establishments/evaluations",
             "/api/v1/establishments/{establishmentId}/evaluations");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EvaluationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "Establishment submits an evaluation on an offer.");
    }

    public override async Task HandleAsync(CreateEstablishmentEvaluationRequest req, CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var offer = await _db.Offers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == req.OfferId, ct);
        if (offer is null)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status404NotFound,
                EvaluationErrorCodes.OfferNotFound,
                "Offer does not exist.", ct);
            return;
        }

        // The establishment must be either the sender (if applicant is a
        // worker) OR the applicant (if it applied to another opp). Either
        // way, determine the evaluable other-party.
        var application = await _db.OpportunityApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == offer.ApplicationId, ct);
        if (application is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var isSender = offer.SenderEstablishmentId == establishmentId.Value;
        var isApplicantEstablishment = application.ApplicantEstablishmentId == establishmentId.Value;
        if (!isSender && !isApplicantEstablishment)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status404NotFound,
                EvaluationErrorCodes.NotPartyToOffer,
                "Establishment is not a party to this offer.", ct);
            return;
        }

        if (offer.Status is not (OfferStatus.Accepted
            or OfferStatus.WaitingForEvaluation
            or OfferStatus.Completed))
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                EvaluationErrorCodes.OfferNotEvaluable,
                "Offer is not in an evaluable state.", ct);
            return;
        }

        var already = await _db.Evaluations
            .AsNoTracking()
            .AnyAsync(e => e.OfferId == offer.Id
                       && e.EvaluatorEstablishmentId == establishmentId.Value, ct);
        if (already)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status409Conflict,
                EvaluationErrorCodes.AlreadyEvaluated,
                "Establishment has already evaluated this offer.", ct);
            return;
        }

        // Construct the right factory based on who the other party is.
        Evaluation evaluation;
        if (isSender)
        {
            // Establishment evaluates the applicant.
            if (application.ApplicantUserId is { } applicantUser)
            {
                evaluation = Evaluation.ByEstablishmentOfUser(
                    id: Guid.NewGuid(),
                    opportunityId: offer.OpportunityId,
                    offerId: offer.Id,
                    evaluatorEstablishmentId: establishmentId.Value,
                    evaluableUserId: applicantUser,
                    rating: req.Rating,
                    recommendForFutureOpportunities: req.RecommendForFutureOpportunities,
                    matloobEvaluation: req.MatloobEvaluation,
                    matchingPercentage: req.MatchingPercentage,
                    comment: req.Comment,
                    successManagementCriteriaComment: req.SuccessManagementCriteriaComment);
            }
            else
            {
                evaluation = Evaluation.ByEstablishmentOfEstablishment(
                    id: Guid.NewGuid(),
                    opportunityId: offer.OpportunityId,
                    offerId: offer.Id,
                    evaluatorEstablishmentId: establishmentId.Value,
                    evaluableEstablishmentId: application.ApplicantEstablishmentId!.Value,
                    rating: req.Rating,
                    recommendForFutureOpportunities: req.RecommendForFutureOpportunities,
                    matloobEvaluation: req.MatloobEvaluation,
                    matchingPercentage: req.MatchingPercentage,
                    comment: req.Comment,
                    successManagementCriteriaComment: req.SuccessManagementCriteriaComment);
            }
        }
        else
        {
            // Applicant establishment evaluates the sender establishment.
            evaluation = Evaluation.ByEstablishmentOfEstablishment(
                id: Guid.NewGuid(),
                opportunityId: offer.OpportunityId,
                offerId: offer.Id,
                evaluatorEstablishmentId: establishmentId.Value,
                evaluableEstablishmentId: offer.SenderEstablishmentId,
                rating: req.Rating,
                recommendForFutureOpportunities: req.RecommendForFutureOpportunities,
                matloobEvaluation: req.MatloobEvaluation,
                matchingPercentage: req.MatchingPercentage,
                comment: req.Comment,
                successManagementCriteriaComment: req.SuccessManagementCriteriaComment);
        }

        _db.Evaluations.Add(evaluation);

        await CreateUserEvaluationEndpoint.MaybeMarkCompleted(_db, offer, ct);

        _outbox.Enqueue(EvaluationEventTypes.Submitted, nameof(Evaluation), evaluation.Id,
            new
            {
                id = evaluation.Id,
                offerId = offer.Id,
                evaluatorEstablishmentId = establishmentId.Value,
            });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        var response = await EvaluationReadMapper.MapAsync(_db, evaluation, ct);
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/evaluations/{evaluation.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}

public sealed class CreateEstablishmentEvaluationRequest
{
    [JsonPropertyName("offer_id")]
    public Guid OfferId { get; init; }

    [JsonPropertyName("rating")]
    public byte Rating { get; init; }

    [JsonPropertyName("recommend_for_future_opportunities")]
    public bool RecommendForFutureOpportunities { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("matching_percentage")]
    public int? MatchingPercentage { get; init; }

    [JsonPropertyName("success_management_criteria_comment")]
    public string? SuccessManagementCriteriaComment { get; init; }

    [JsonPropertyName("matloob_evaluation")]
    public byte? MatloobEvaluation { get; init; }
}

public sealed class CreateEstablishmentEvaluationRequestValidator
    : Validator<CreateEstablishmentEvaluationRequest>
{
    public CreateEstablishmentEvaluationRequestValidator()
    {
        RuleFor(x => x.OfferId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween((byte)1, (byte)5);
        RuleFor(x => x.Comment).MaximumLength(500);
        RuleFor(x => x.MatchingPercentage).InclusiveBetween(0, 100)
            .When(x => x.MatchingPercentage.HasValue);
        RuleFor(x => x.MatloobEvaluation).InclusiveBetween((byte)1, (byte)5)
            .When(x => x.MatloobEvaluation.HasValue);
        RuleFor(x => x.SuccessManagementCriteriaComment).MaximumLength(500);
    }
}
