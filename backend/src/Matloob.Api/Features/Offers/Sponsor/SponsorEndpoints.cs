using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Offers.Common;
using Matloob.Api.Features.Offers.Lifecycle;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Offers.Sponsor;

/// <summary>
/// <c>GET /api/establishments/offers/{id}/pending-sponsor-approval</c>
/// + canonical — detail of an offer awaiting sponsor approval, viewable
/// only by the sponsor establishment.
/// </summary>
public sealed class GetPendingSponsorApprovalOfferEndpoint
    : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public GetPendingSponsorApprovalOfferEndpoint(
        AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public override void Configure()
    {
        Get("/api/establishments/offers/{id}/pending-sponsor-approval",
            "/api/v1/establishments/{establishmentId}/offers/{id}/pending-sponsor-approval");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Sponsor sees the offer they need to approve.");
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
        if (offer is null
            || offer.SponsorEstablishmentId != establishmentId.Value
            || offer.Status != OfferStatus.PendingSponsorApproval)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var response = await OfferReadMapper.MapAsync(_db, offer, _clock.GetUtcNow(), ct);
        await Send.OkAsync(response, ct);
    }
}

/// <summary><c>POST /api/establishments/offers/{id}/sponsor/accept</c>
/// + canonical — sponsor approves the offer (PendingSponsorApproval ->
/// Pending).</summary>
public sealed class SponsorAcceptOfferEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public SponsorAcceptOfferEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/establishments/offers/{id}/sponsor/accept",
             "/api/v1/establishments/{establishmentId}/offers/{id}/sponsor/accept");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Sponsor approves a pending-sponsor-approval offer.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var offerId = Route<Guid>("id");
        var offer = await _db.Offers
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null || offer.SponsorEstablishmentId != establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var now = _clock.GetUtcNow();
        if (!OfferLifecycleQueries.TryTransition(() => offer.SponsorApprove(), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }

        _outbox.Enqueue(OfferEventTypes.SponsorApproved, nameof(Offer), offer.Id,
            new { id = offer.Id, approvedAt = now, sponsorEstablishmentId = establishmentId.Value });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}

/// <summary><c>POST /api/establishments/offers/{id}/sponsor/reject</c>
/// + canonical — sponsor rejects (PendingSponsorApproval -> SponsorRejected).</summary>
public sealed class SponsorRejectOfferEndpoint
    : Endpoint<RejectionRequest, OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public SponsorRejectOfferEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/establishments/offers/{id}/sponsor/reject",
             "/api/v1/establishments/{establishmentId}/offers/{id}/sponsor/reject");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Sponsor rejects a pending-sponsor-approval offer.");
    }

    public override async Task HandleAsync(RejectionRequest req, CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var offerId = Route<Guid>("id");
        var offer = await _db.Offers
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null || offer.SponsorEstablishmentId != establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!OfferLifecycleQueries.TryTransition(
                () => offer.SponsorReject(req.ReasonId, req.OtherReason), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }

        var now = _clock.GetUtcNow();
        _outbox.Enqueue(OfferEventTypes.SponsorRejected, nameof(Offer), offer.Id,
            new { id = offer.Id, rejectedAt = now, sponsorEstablishmentId = establishmentId.Value });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}
