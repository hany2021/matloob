using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Evaluations.Common;
using Matloob.Api.Features.Offers.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Evaluations.OfferLinks;

// === User-side ===========================================================

/// <summary><c>GET /api/users/offers/unevaluated</c> + canonical —
/// offers awaiting evaluation by the worker.</summary>
public sealed class ListUserUnevaluatedOffersEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OfferResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ListUserUnevaluatedOffersEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c)
    { _db = db; _currentUser = u; _clock = c; }

    public override void Configure()
    {
        Get("/api/users/offers/unevaluated", "/api/v1/users/offers/unevaluated");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OfferResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Offers"));
        Summary(s => s.Summary = "List offers awaiting evaluation by the worker.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var now = _clock.GetUtcNow();

        var evaluatedOfferIds = _db.Evaluations
            .AsNoTracking()
            .Where(e => e.EvaluatorUserId == sub)
            .Select(e => e.OfferId);

        var offers = await (
            from o in _db.Offers.AsNoTracking()
            join a in _db.OpportunityApplications.AsNoTracking() on o.ApplicationId equals a.Id
            where a.ApplicantUserId == sub
               && (o.Status == OfferStatus.WaitingForEvaluation
                || o.Status == OfferStatus.Accepted
                || o.Status == OfferStatus.Completed)
               && !evaluatedOfferIds.Contains(o.Id)
            orderby o.CreatedAt descending
            select o).Take(200).ToListAsync(ct);

        var responses = new List<OfferResponse>(offers.Count);
        foreach (var offer in offers)
        {
            responses.Add(await OfferReadMapper.MapAsync(_db, offer, now, ct));
        }
        await Send.OkAsync(responses, ct);
    }
}

/// <summary><c>GET /api/users/offers/{id}/other-evaluation</c> + canonical
/// — show the counterparty's evaluation of the worker on this offer,
/// but only if the worker has already evaluated.</summary>
public sealed class GetUserOtherEvaluationEndpoint
    : EndpointWithoutRequest<EvaluationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetUserOtherEvaluationEndpoint(AppDbContext db, ICurrentUser u)
    { _db = db; _currentUser = u; }

    public override void Configure()
    {
        Get("/api/users/offers/{id}/other-evaluation",
            "/api/v1/users/offers/{id}/other-evaluation");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EvaluationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Counterparty evaluation of the worker on an offer.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var offerId = Route<Guid>("id");

        var owns = await (
            from o in _db.Offers.AsNoTracking()
            join a in _db.OpportunityApplications.AsNoTracking() on o.ApplicationId equals a.Id
            where o.Id == offerId && a.ApplicantUserId == sub
            select o).AnyAsync(ct);
        if (!owns) { await Send.NotFoundAsync(ct); return; }

        var meEvaluated = await _db.Evaluations
            .AsNoTracking()
            .AnyAsync(e => e.OfferId == offerId && e.EvaluatorUserId == sub, ct);
        if (!meEvaluated)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                EvaluationErrorCodes.CounterpartyNotEvaluated,
                "You must submit your own evaluation before viewing the counterparty's.",
                ct);
            return;
        }

        var other = await _db.Evaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.OfferId == offerId
                                   && e.EvaluatorEstablishmentId != null, ct);
        if (other is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(await EvaluationReadMapper.MapAsync(_db, other, ct), ct);
    }
}

// === Establishment-side ==================================================

/// <summary><c>GET /api/establishments/offers/unevaluated</c> + canonical.</summary>
public sealed class ListEstablishmentUnevaluatedOffersEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OfferResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ListEstablishmentUnevaluatedOffersEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c)
    { _db = db; _currentUser = u; _clock = c; }

    public override void Configure()
    {
        Get("/api/establishments/offers/unevaluated",
            "/api/v1/establishments/{establishmentId}/offers/unevaluated");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OfferResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offers"));
        Summary(s => s.Summary = "List offers awaiting evaluation by the establishment.");
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

        var evaluated = _db.Evaluations
            .AsNoTracking()
            .Where(e => e.EvaluatorEstablishmentId == establishmentId.Value)
            .Select(e => e.OfferId);

        var offers = await _db.Offers
            .AsNoTracking()
            .Where(o => (o.SenderEstablishmentId == establishmentId.Value
                     || _db.OpportunityApplications.Any(a =>
                            a.Id == o.ApplicationId
                         && a.ApplicantEstablishmentId == establishmentId.Value))
                     && (o.Status == OfferStatus.WaitingForEvaluation
                      || o.Status == OfferStatus.Accepted
                      || o.Status == OfferStatus.Completed)
                     && !evaluated.Contains(o.Id))
            .OrderByDescending(o => o.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var now = _clock.GetUtcNow();
        var responses = new List<OfferResponse>(offers.Count);
        foreach (var offer in offers)
        {
            responses.Add(await OfferReadMapper.MapAsync(_db, offer, now, ct));
        }
        await Send.OkAsync(responses, ct);
    }
}

/// <summary><c>GET /api/establishments/offers/{id}/other-evaluation</c> +
/// canonical — counterparty evaluation visible only after the
/// establishment has evaluated.</summary>
public sealed class GetEstablishmentOtherEvaluationEndpoint
    : EndpointWithoutRequest<EvaluationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetEstablishmentOtherEvaluationEndpoint(AppDbContext db, ICurrentUser u)
    { _db = db; _currentUser = u; }

    public override void Configure()
    {
        Get("/api/establishments/offers/{id}/other-evaluation",
            "/api/v1/establishments/{establishmentId}/offers/{id}/other-evaluation");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EvaluationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Counterparty evaluation visible to the establishment.");
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

        var offerId = Route<Guid>("id");
        var offer = await _db.Offers.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        var isParty = offer.SenderEstablishmentId == establishmentId.Value
            || await _db.OpportunityApplications.AsNoTracking()
                .AnyAsync(a => a.Id == offer.ApplicationId
                            && a.ApplicantEstablishmentId == establishmentId.Value, ct);
        if (!isParty) { await Send.NotFoundAsync(ct); return; }

        var meEvaluated = await _db.Evaluations
            .AsNoTracking()
            .AnyAsync(e => e.OfferId == offerId
                       && e.EvaluatorEstablishmentId == establishmentId.Value, ct);
        if (!meEvaluated)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                EvaluationErrorCodes.CounterpartyNotEvaluated,
                "You must submit your own evaluation before viewing the counterparty's.",
                ct);
            return;
        }

        // Find the OTHER side's evaluation. Could be a user-evaluator OR
        // a different establishment-evaluator (org-to-org case).
        var other = await _db.Evaluations
            .AsNoTracking()
            .Where(e => e.OfferId == offerId
                     && e.EvaluatorEstablishmentId != establishmentId.Value)
            .FirstOrDefaultAsync(ct);
        if (other is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(await EvaluationReadMapper.MapAsync(_db, other, ct), ct);
    }
}
