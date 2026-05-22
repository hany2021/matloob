using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Evaluations.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Evaluations;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Evaluations.UserSide;

/// <summary><c>GET /api/users/evaluations</c> + canonical — list
/// evaluations authored BY the current worker.</summary>
public sealed class ListUserEvaluationsEndpoint
    : EndpointWithoutRequest<IReadOnlyList<EvaluationResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListUserEvaluationsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/users/evaluations", "/api/v1/users/evaluations");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<EvaluationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "List evaluations authored by the current worker.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var evals = await _db.Evaluations
            .AsNoTracking()
            .Where(e => e.EvaluatorUserId == sub)
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

/// <summary><c>GET /api/users/evaluations/{id}</c> + canonical.</summary>
public sealed class GetUserEvaluationEndpoint
    : EndpointWithoutRequest<EvaluationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetUserEvaluationEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/users/evaluations/{id}", "/api/v1/users/evaluations/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EvaluationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "Detail of one evaluation authored by the worker.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var id = Route<Guid>("id");
        var ev = await _db.Evaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.EvaluatorUserId == sub, ct);
        if (ev is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(await EvaluationReadMapper.MapAsync(_db, ev, ct), ct);
    }
}

/// <summary><c>POST /api/users/evaluations</c> + canonical — worker
/// submits an evaluation of the establishment they accepted an offer
/// from.</summary>
public sealed class CreateUserEvaluationEndpoint
    : Endpoint<CreateUserEvaluationRequest, EvaluationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public CreateUserEvaluationEndpoint(
        AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/users/evaluations", "/api/v1/users/evaluations");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EvaluationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "Worker submits an evaluation on an offer.");
    }

    public override async Task HandleAsync(CreateUserEvaluationRequest req, CancellationToken ct)
    {
        var sub = _currentUser.UserId;

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

        // Worker must be the applicant on this offer.
        var owns = await _db.OpportunityApplications
            .AsNoTracking()
            .AnyAsync(a => a.Id == offer.ApplicationId && a.ApplicantUserId == sub, ct);
        if (!owns)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status404NotFound,
                EvaluationErrorCodes.NotPartyToOffer,
                "Worker is not the applicant on this offer.", ct);
            return;
        }

        // Status must be Accepted / WaitingForEvaluation / Completed for
        // evaluations to make sense.
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
            .AnyAsync(e => e.OfferId == offer.Id && e.EvaluatorUserId == sub, ct);
        if (already)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status409Conflict,
                EvaluationErrorCodes.AlreadyEvaluated,
                "Worker has already evaluated this offer.", ct);
            return;
        }

        var evaluation = Evaluation.ByUserOfEstablishment(
            id: Guid.NewGuid(),
            opportunityId: offer.OpportunityId,
            offerId: offer.Id,
            evaluatorUserId: sub,
            evaluableEstablishmentId: offer.SenderEstablishmentId,
            rating: req.Rating,
            recommendForFutureOpportunities: req.RecommendForFutureOpportunities,
            matloobEvaluation: req.MatloobEvaluation,
            comment: req.Comment);
        _db.Evaluations.Add(evaluation);

        // If the other side already evaluated, mark the offer Completed.
        await MaybeMarkCompleted(_db, offer, ct);

        _outbox.Enqueue(EvaluationEventTypes.Submitted, nameof(Evaluation), evaluation.Id,
            new
            {
                id = evaluation.Id,
                offerId = offer.Id,
                evaluatorUserId = sub,
                evaluableEstablishmentId = offer.SenderEstablishmentId,
            });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        var response = await EvaluationReadMapper.MapAsync(_db, evaluation, ct);
        HttpContext.Response.Headers.Location = $"/api/v1/users/evaluations/{evaluation.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }

    /// <summary>
    /// When BOTH sides of an offer have an Evaluation row, the offer
    /// transitions to <see cref="OfferStatus.Completed"/>. The transition
    /// itself goes through the domain method's guard, which only accepts
    /// it from <see cref="OfferStatus.WaitingForEvaluation"/>; we set
    /// WaitingForEvaluation first if needed to keep the guard happy.
    /// </summary>
    internal static async Task MaybeMarkCompleted(
        AppDbContext db, Offer offer, CancellationToken ct)
    {
        var counterpartyEvaluated = await db.Evaluations
            .AsNoTracking()
            .AnyAsync(e => e.OfferId == offer.Id
                       && (e.EvaluatorUserId != null || e.EvaluatorEstablishmentId != null), ct);
        // The current evaluation about to be saved counts as "this side";
        // we look for an evaluation from the OTHER side. ChangeTracker
        // already has the new one added, so check by both columns.
        var thisSideUser = db.ChangeTracker.Entries<Evaluation>()
            .Where(e => e.Entity.OfferId == offer.Id)
            .Any(e => e.Entity.EvaluatorUserId != null);
        var thisSideEstablishment = db.ChangeTracker.Entries<Evaluation>()
            .Where(e => e.Entity.OfferId == offer.Id)
            .Any(e => e.Entity.EvaluatorEstablishmentId != null);

        var otherSideExists = await db.Evaluations
            .AsNoTracking()
            .AnyAsync(e => e.OfferId == offer.Id
                       && (thisSideUser ? e.EvaluatorEstablishmentId != null
                                        : e.EvaluatorUserId != null), ct);

        if (!otherSideExists) return;

        // Both sides — move offer to Completed via the domain method.
        // If status is Accepted we first flip to WaitingForEvaluation
        // (domain method requires it).
        var tracked = await db.Offers.FirstOrDefaultAsync(o => o.Id == offer.Id, ct);
        if (tracked is null) return;

        if (tracked.Status == OfferStatus.Accepted)
        {
            typeof(Offer)
                .GetProperty(nameof(Offer.Status))!
                .SetValue(tracked, OfferStatus.WaitingForEvaluation);
        }
        if (tracked.Status == OfferStatus.WaitingForEvaluation)
        {
            tracked.MarkCompleted();
        }
    }
}

public sealed class CreateUserEvaluationRequest
{
    [JsonPropertyName("offer_id")]
    public Guid OfferId { get; init; }

    [JsonPropertyName("rating")]
    public byte Rating { get; init; }

    [JsonPropertyName("recommend_for_future_opportunities")]
    public bool RecommendForFutureOpportunities { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("matloob_evaluation")]
    public byte? MatloobEvaluation { get; init; }
}

public sealed class CreateUserEvaluationRequestValidator
    : Validator<CreateUserEvaluationRequest>
{
    public CreateUserEvaluationRequestValidator()
    {
        RuleFor(x => x.OfferId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween((byte)1, (byte)5);
        RuleFor(x => x.Comment).MaximumLength(500);
        RuleFor(x => x.MatloobEvaluation)
            .InclusiveBetween((byte)1, (byte)5)
            .When(x => x.MatloobEvaluation.HasValue);
    }
}
