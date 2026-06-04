using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Offers.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Offers.Reads;

/// <summary>
/// <c>GET /api/establishments/received-offers</c> + canonical — offers
/// where the resolved establishment is the applicant (i.e. someone sent
/// THEM an offer for an opportunity they applied to).
/// </summary>
public sealed class ListEstablishmentReceivedOffersEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OfferResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ListEstablishmentReceivedOffersEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/received-offers",
            "/api/v1/establishments/{establishmentId}/received-offers");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OfferResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offers"));
        Summary(s => s.Summary = "List offers received by the resolved establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;
        if (!await OfferAccessChecks.IsMemberOrAdminAsync(_db, HttpContext, establishmentId.Value, sub, ct)) return;

        var now = _clock.GetUtcNow();
        var offers = await (
                from o in _db.Offers.AsNoTracking()
                join a in _db.OpportunityApplications.AsNoTracking() on o.ApplicationId equals a.Id
                where a.ApplicantEstablishmentId == establishmentId.Value
                select o)
            .ApplyFilters(_db, HttpContext)
            .OrderByDescending(o => o.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var responses = new List<OfferResponse>(offers.Count);
        foreach (var offer in offers)
        {
            responses.Add(await OfferReadMapper.MapAsync(_db, offer, now, ct));
        }
        await Send.OkAsync(responses, ct);
    }
}

/// <summary>
/// <c>GET /api/establishments/received-offers/{id}</c> + canonical.
/// </summary>
public sealed class GetEstablishmentReceivedOfferEndpoint
    : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public GetEstablishmentReceivedOfferEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/received-offers/{id}",
            "/api/v1/establishments/{establishmentId}/received-offers/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Detail of a received offer by the resolved establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;
        if (!await OfferAccessChecks.IsMemberOrAdminAsync(_db, HttpContext, establishmentId.Value, sub, ct)) return;

        var offerId = Route<Guid>("id");
        var offer = await _db.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var owns = await _db.OpportunityApplications
            .AsNoTracking()
            .AnyAsync(a => a.Id == offer.ApplicationId
                       && a.ApplicantEstablishmentId == establishmentId.Value, ct);
        if (!owns)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var response = await OfferReadMapper.MapAsync(_db, offer, _clock.GetUtcNow(), ct);
        await Send.OkAsync(response, ct);
    }
}

/// <summary>
/// <c>GET /api/establishments/sent-offers</c> + canonical — offers sent
/// BY the resolved establishment to either users or other establishments.
/// </summary>
public sealed class ListEstablishmentSentOffersEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OfferResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ListEstablishmentSentOffersEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/sent-offers",
            "/api/v1/establishments/{establishmentId}/sent-offers");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OfferResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offers"));
        Summary(s => s.Summary = "List offers sent by the resolved establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;
        if (!await OfferAccessChecks.IsMemberOrAdminAsync(_db, HttpContext, establishmentId.Value, sub, ct)) return;

        var now = _clock.GetUtcNow();
        var offers = await _db.Offers
            .AsNoTracking()
            .Where(o => o.SenderEstablishmentId == establishmentId.Value)
            .ApplyFilters(_db, HttpContext)
            .OrderByDescending(o => o.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var responses = new List<OfferResponse>(offers.Count);
        foreach (var offer in offers)
        {
            responses.Add(await OfferReadMapper.MapAsync(_db, offer, now, ct));
        }
        await Send.OkAsync(responses, ct);
    }
}

/// <summary>
/// <c>GET /api/establishments/sent-offers/{id}</c> + canonical.
/// </summary>
public sealed class GetEstablishmentSentOfferEndpoint
    : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public GetEstablishmentSentOfferEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/sent-offers/{id}",
            "/api/v1/establishments/{establishmentId}/sent-offers/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Detail of a sent offer by the resolved establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;
        if (!await OfferAccessChecks.IsMemberOrAdminAsync(_db, HttpContext, establishmentId.Value, sub, ct)) return;

        var offerId = Route<Guid>("id");
        var offer = await _db.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null || offer.SenderEstablishmentId != establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var response = await OfferReadMapper.MapAsync(_db, offer, _clock.GetUtcNow(), ct);
        await Send.OkAsync(response, ct);
    }
}

/// <summary>
/// <c>GET /api/establishments/offers/pending-action</c> + canonical —
/// offers awaiting either a cancellation review or a sponsor approval
/// by the resolved establishment.
/// </summary>
public sealed class ListPendingActionOffersEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OfferResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ListPendingActionOffersEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/offers/pending-action",
            "/api/v1/establishments/{establishmentId}/offers/pending-action");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OfferResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Offers awaiting cancellation review or sponsor approval.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;
        if (!await OfferAccessChecks.IsMemberOrAdminAsync(_db, HttpContext, establishmentId.Value, sub, ct)) return;

        var typeFilter = HttpContext.Request.Query["type"].FirstOrDefault();
        var includeSponsor = typeFilter is null or "sponsor_requests";
        var includeCancellation = typeFilter is null or "cancellation_requests";

        var now = _clock.GetUtcNow();
        var query = _db.Offers.AsNoTracking().Where(o => false); // start empty

        if (includeCancellation)
        {
            // Offers the establishment SENT that now have a cancellation
            // request opened by the applicant.
            var withOpenCancel = _db.OfferCancellationRequests
                .AsNoTracking()
                .Where(c => !c.IsApproved && !c.IsRejected)
                .Select(c => c.OfferId);
            query = query.Concat(_db.Offers
                .AsNoTracking()
                .Where(o => o.SenderEstablishmentId == establishmentId.Value
                         && withOpenCancel.Contains(o.Id)));
        }

        if (includeSponsor)
        {
            // Offers awaiting sponsor approval where this establishment is
            // the sponsor.
            query = query.Concat(_db.Offers
                .AsNoTracking()
                .Where(o => o.SponsorEstablishmentId == establishmentId.Value
                         && (o.Status == OfferStatus.PendingSponsorApproval
                          || o.Status == OfferStatus.PendingSponsorCancellationApproval)));
        }

        var offers = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var responses = new List<OfferResponse>(offers.Count);
        foreach (var offer in offers)
        {
            responses.Add(await OfferReadMapper.MapAsync(_db, offer, now, ct));
        }
        await Send.OkAsync(responses, ct);
    }
}

internal static class OfferAccessChecks
{
    public static async Task<bool> IsMemberOrAdminAsync(
        AppDbContext db,
        HttpContext httpContext,
        Guid establishmentId,
        string subClaim,
        CancellationToken ct)
    {
        var isAdmin = MembershipChecks.IsAdmin(httpContext.User);
        if (isAdmin) return true;
        var isMember = await MembershipChecks.IsActiveMemberAsync(
            db, establishmentId, subClaim, ct);
        if (!isMember)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsync(string.Empty, ct);
            return false;
        }
        return true;
    }
}
