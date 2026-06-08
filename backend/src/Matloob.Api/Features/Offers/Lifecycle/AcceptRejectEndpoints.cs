using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Offers.Common;
using Matloob.Api.Features.Offers.Reads;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Offers.Lifecycle;

// === User-side accept / reject ============================================

/// <summary><c>POST /api/users/offers/{id}/accept</c> + canonical.</summary>
public sealed class UserAcceptOfferEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public UserAcceptOfferEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/users/offers/{id}/accept", "/api/v1/users/offers/{id}/accept");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Worker accepts an offer they received.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var offerId = Route<Guid>("id");
        var offer = await OfferLifecycleQueries.LoadUserOwnedOfferAsync(_db, offerId, sub, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        var now = _clock.GetUtcNow();
        if (!OfferLifecycleQueries.TryTransition(() => offer.Accept(now), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }

        _outbox.Enqueue(OfferEventTypes.Accepted, nameof(Offer), offer.Id,
            new { id = offer.Id, acceptedAt = now, acceptedByUserId = sub });
        await OfferLifecycleQueries.EnqueuePostAcceptEventsAsync(_db, _outbox, offer, now, ct);
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}

/// <summary><c>POST /api/users/offers/{id}/reject</c> + canonical.</summary>
public sealed class UserRejectOfferEndpoint : Endpoint<RejectionRequest, OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public UserRejectOfferEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/users/offers/{id}/reject", "/api/v1/users/offers/{id}/reject");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Worker rejects an offer they received.");
    }

    public override async Task HandleAsync(RejectionRequest req, CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var offerId = Route<Guid>("id");
        var offer = await OfferLifecycleQueries.LoadUserOwnedOfferAsync(_db, offerId, sub, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        if (!OfferLifecycleQueries.TryTransition(
                () => offer.Reject(req.ReasonId, req.OtherReason), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }

        var now = _clock.GetUtcNow();
        _outbox.Enqueue(OfferEventTypes.Rejected, nameof(Offer), offer.Id,
            new { id = offer.Id, reasonId = req.ReasonId, rejectedAt = now, rejectedByUserId = sub });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}

// === Establishment-side accept / reject ==================================

/// <summary><c>POST /api/establishments/offers/{id}/accept</c> + canonical.</summary>
public sealed class EstablishmentAcceptOfferEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public EstablishmentAcceptOfferEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/establishments/offers/{id}/accept",
             "/api/v1/establishments/{establishmentId}/offers/{id}/accept");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Establishment accepts an offer where it is the applicant.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct, Infrastructure.Auth.Permissions.Offers.Respond);
        if (establishmentId is null) return;

        var offerId = Route<Guid>("id");
        var offer = await OfferLifecycleQueries.LoadEstablishmentReceivedOfferAsync(
            _db, offerId, establishmentId.Value, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        var now = _clock.GetUtcNow();
        if (!OfferLifecycleQueries.TryTransition(() => offer.Accept(now), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }

        _outbox.Enqueue(OfferEventTypes.Accepted, nameof(Offer), offer.Id,
            new { id = offer.Id, acceptedAt = now, acceptedByEstablishmentId = establishmentId.Value });
        await OfferLifecycleQueries.EnqueuePostAcceptEventsAsync(_db, _outbox, offer, now, ct);
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}

/// <summary>
/// <c>POST /api/establishments/offers/{id}/reject</c> + canonical.
///
/// <para>
/// Legacy compatibility: the Laravel
/// <c>Establishments\Offers\RejectOfferController</c> was a bare POST
/// with no request body. This endpoint accepts BOTH:
/// </para>
/// <list type="bullet">
///   <item>an empty body (legacy bare POST), in which case rejection is
///     recorded with a null reason;</item>
///   <item><c>{reason_id, other_reason?}</c> (canonical) — matches the
///     user-side reject and sponsor-reject shapes.</item>
/// </list>
/// </summary>
public sealed class EstablishmentRejectOfferEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public EstablishmentRejectOfferEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/establishments/offers/{id}/reject",
             "/api/v1/establishments/{establishmentId}/offers/{id}/reject");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Establishment rejects an offer where it is the applicant.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct, Infrastructure.Auth.Permissions.Offers.Respond);
        if (establishmentId is null) return;

        var offerId = Route<Guid>("id");
        var offer = await OfferLifecycleQueries.LoadEstablishmentReceivedOfferAsync(
            _db, offerId, establishmentId.Value, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        // Body is optional. Try to parse JSON; ignore parse errors.
        var body = await TryReadOptionalRejectionBodyAsync(HttpContext, ct);

        if (!OfferLifecycleQueries.TryTransition(
                () => offer.Reject(body?.ReasonId, body?.OtherReason), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }

        var now = _clock.GetUtcNow();
        _outbox.Enqueue(OfferEventTypes.Rejected, nameof(Offer), offer.Id,
            new { id = offer.Id, reasonId = body?.ReasonId, rejectedAt = now, rejectedByEstablishmentId = establishmentId.Value });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }

    /// <summary>
    /// Best-effort read of an optional JSON body
    /// <c>{reason_id, other_reason?}</c>. Returns null on empty body /
    /// unparseable JSON — the establishment-reject route was a bare
    /// POST in Laravel and clients may not send anything.
    /// </summary>
    private static async Task<RejectionRequest?> TryReadOptionalRejectionBodyAsync(
        HttpContext ctx, CancellationToken ct)
    {
        if (ctx.Request.ContentLength is 0 or null)
        {
            return null;
        }
        try
        {
            return await ctx.Request.ReadFromJsonAsync<RejectionRequest>(ct);
        }
        catch
        {
            return null;
        }
    }
}

// === Shared types =========================================================

/// <summary>
/// Standard rejection / cancellation payload shared by offer + sponsor
/// reject endpoints. Mirrors Laravel <c>Base/RejectionRequest</c>.
/// </summary>
public sealed class RejectionRequest
{
    [JsonPropertyName("reason_id")]
    public Guid? ReasonId { get; init; }

    [JsonPropertyName("other_reason")]
    public string? OtherReason { get; init; }
}

public sealed class RejectionRequestValidator : Validator<RejectionRequest>
{
    public RejectionRequestValidator()
    {
        RuleFor(x => x.ReasonId).NotEmpty();
        RuleFor(x => x.OtherReason).MaximumLength(500);
    }
}

internal static class OfferLifecycleQueries
{
    public static async Task<Offer?> LoadUserOwnedOfferAsync(
        AppDbContext db, Guid offerId, string sub, CancellationToken ct)
    {
        var offer = await db.Offers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) return null;
        var owns = await db.OpportunityApplications
            .AsNoTracking()
            .AnyAsync(a => a.Id == offer.ApplicationId && a.ApplicantUserId == sub, ct);
        return owns ? offer : null;
    }

    public static async Task<Offer?> LoadEstablishmentReceivedOfferAsync(
        AppDbContext db, Guid offerId, Guid establishmentId, CancellationToken ct)
    {
        var offer = await db.Offers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) return null;
        var receives = await db.OpportunityApplications
            .AsNoTracking()
            .AnyAsync(a => a.Id == offer.ApplicationId
                       && a.ApplicantEstablishmentId == establishmentId, ct);
        return receives ? offer : null;
    }

    public static async Task<Offer?> LoadEstablishmentSentOfferAsync(
        AppDbContext db, Guid offerId, Guid establishmentId, CancellationToken ct)
    {
        var offer = await db.Offers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null || offer.SenderEstablishmentId != establishmentId) return null;
        return offer;
    }

    /// <summary>
    /// Emits the FYI outbox events that follow an offer acceptance (call after
    /// the <c>offer.accepted</c> enqueue, before <c>Flush</c>):
    /// <list type="bullet">
    ///   <item><c>offer.is_active</c> — always, to the applicant (legacy
    ///     <c>OfferIsActiveNotification</c>; the contractless model treats
    ///     accept as "active").</item>
    ///   <item><c>opportunity.fulfilled</c> — only when this acceptance fills
    ///     the opportunity's required personnel (legacy event-driven
    ///     <c>OpportunityFulfilled</c>, not time-based).</item>
    /// </list>
    /// </summary>
    public static async Task EnqueuePostAcceptEventsAsync(
        AppDbContext db, IOutboxWriter outbox, Offer offer, DateTimeOffset now, CancellationToken ct)
    {
        outbox.Enqueue(OfferEventTypes.IsActive, nameof(Offer), offer.Id, new { id = offer.Id, at = now });

        var opp = await db.Opportunities.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offer.OpportunityId, ct);
        if (opp is null || opp.RequiredPersonnel <= 0) return;

        // This offer just transitioned to Accepted in-memory (not yet saved),
        // so count the OTHER active-status offers and add it back in — matches
        // legacy OpportunitySupport::reachedRequiredPersonnel.
        var otherActive = await db.Offers.AsNoTracking()
            .CountAsync(o => o.OpportunityId == offer.OpportunityId
                          && o.Id != offer.Id
                          && OfferStatusSets.Active.Contains(o.Status), ct);
        if (otherActive + 1 >= opp.RequiredPersonnel)
        {
            outbox.Enqueue(
                OpportunityEventTypes.Fulfilled, nameof(Opportunity), opp.Id,
                new { id = opp.Id, at = now });
        }
    }

    public static bool TryTransition(Action transition, out string error)
    {
        try
        {
            transition();
            error = string.Empty;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
