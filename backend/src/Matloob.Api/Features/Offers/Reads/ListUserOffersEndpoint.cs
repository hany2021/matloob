using FastEndpoints;
using Matloob.Api.Features.Offers.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Offers.Reads;

/// <summary>
/// <c>GET /api/users/offers</c> + canonical alias — list offers the
/// current worker has received as an applicant.
/// </summary>
public sealed class ListUserOffersEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OfferResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ListUserOffersEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Get("/api/users/offers", "/api/v1/users/offers");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OfferResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Offers"));
        Summary(s => s.Summary = "List offers the worker has received.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var now = _clock.GetUtcNow();

        var offers = await (
            from o in _db.Offers.AsNoTracking()
            join a in _db.OpportunityApplications.AsNoTracking() on o.ApplicationId equals a.Id
            where a.ApplicantUserId == sub
            orderby o.CreatedAt descending
            select o).Take(200).ToListAsync(ct);

        var responses = new List<OfferResponse>(offers.Count);
        foreach (var offer in offers)
        {
            responses.Add(await OfferReadMapper.MapAsync(_db, offer, now, ct, viewerUserId: sub));
        }
        await Send.OkAsync(responses, ct);
    }
}
