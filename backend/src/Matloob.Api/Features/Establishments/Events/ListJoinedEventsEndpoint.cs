using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Events;

/// <summary>
/// <c>GET /api/establishments/events/joined-events</c> (+ canonical) — events the
/// resolved establishment is involved in: ones it created, plus ones it
/// participated in by winning an offer on one of the event's opportunities.
///
/// <para>Legacy gated participation on an offer's <c>contract</c>; contracts are
/// dropped, so an accepted/awaiting-evaluation/completed offer counts. Optional
/// <c>?status[]=</c> filter by card status.</para>
/// </summary>
public sealed class ListJoinedEventsEndpoint : EndpointWithoutRequest<IReadOnlyList<EventResponse>>
{
    private static readonly OfferStatus[] JoinedStatuses =
        [OfferStatus.Accepted, OfferStatus.WaitingForEvaluation, OfferStatus.Completed];

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListJoinedEventsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/events/joined-events",
            "/api/v1/establishments/{establishmentId}/events/joined-events");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<EventResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishment Events"));
        Summary(s => s.Summary = "List events the resolved establishment created or joined.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForReadAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        var estId = establishmentId.Value;

        var participatedEventIds = await (
            from app in _db.OpportunityApplications.AsNoTracking()
            where app.ApplicantEstablishmentId == estId
            join off in _db.Offers.AsNoTracking() on app.Id equals off.ApplicationId
            where JoinedStatuses.Contains(off.Status)
            join opp in _db.Opportunities.AsNoTracking() on app.OpportunityId equals opp.Id
            select opp.EventId).Distinct().ToListAsync(ct);

        var events = await _db.Events
            .AsNoTracking()
            .Where(e => e.EstablishmentId == estId || participatedEventIds.Contains(e.Id))
            .OrderByDescending(e => e.CreatedAt)
            .Take(1000)
            .ToListAsync(ct);

        var mapped = await EventReadMapper.BuildManyAsync(_db, events, ct);

        var statusFilter = HttpContext.Request.Query["status"]
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim().ToLowerInvariant())
            .ToHashSet();
        if (statusFilter.Count > 0)
        {
            mapped = mapped.Where(m => statusFilter.Contains(m.CardType)).ToList();
        }

        await Send.OkAsync(mapped, ct);
    }
}
