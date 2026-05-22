using FastEndpoints;
using Matloob.Api.Features.Offers.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Offers.Reads;

/// <summary>
/// <c>GET /api/users/offers/{id}</c> + canonical alias — detail of one
/// offer received by the current worker. 404 when the offer doesn't
/// belong to the caller.
/// </summary>
public sealed class GetUserOfferEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public GetUserOfferEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Get("/api/users/offers/{id}", "/api/v1/users/offers/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Detail of an offer received by the worker.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var oppId = Route<Guid>("id");

        var offer = await _db.Offers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == oppId, ct);
        if (offer is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Caller must be the applicant of this offer.
        var owns = await _db.OpportunityApplications
            .AsNoTracking()
            .AnyAsync(a => a.Id == offer.ApplicationId && a.ApplicantUserId == sub, ct);
        if (!owns)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var response = await OfferReadMapper.MapAsync(_db, offer, _clock.GetUtcNow(), ct);
        await Send.OkAsync(response, ct);
    }
}
