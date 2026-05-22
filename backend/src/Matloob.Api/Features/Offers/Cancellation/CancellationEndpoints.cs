using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
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

namespace Matloob.Api.Features.Offers.Cancellation;

// === Request body =========================================================

public sealed class CancelOfferRequest
{
    [JsonPropertyName("offer_id")]
    public Guid OfferId { get; init; }

    [JsonPropertyName("reason_id")]
    public Guid? ReasonId { get; init; }

    [JsonPropertyName("other_reason")]
    public string? OtherReason { get; init; }
}

public sealed class CancelOfferRequestValidator : Validator<CancelOfferRequest>
{
    public CancelOfferRequestValidator()
    {
        RuleFor(x => x.OfferId).NotEmpty();
        RuleFor(x => x.OtherReason).MaximumLength(500);
    }
}

// === User-side cancellation flow =========================================

/// <summary><c>POST /api/users/offers/cancel</c> + canonical — worker
/// opens a cancellation request on their accepted offer.</summary>
public sealed class UserCancelOfferEndpoint : Endpoint<CancelOfferRequest, OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public UserCancelOfferEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/users/offers/cancel", "/api/v1/users/offers/cancel");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Worker opens a cancellation request on an accepted offer.");
    }

    public override async Task HandleAsync(CancelOfferRequest req, CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var offer = await OfferLifecycleQueries.LoadUserOwnedOfferAsync(_db, req.OfferId, sub, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        if (await HasOpenCancellationRequestAsync(_db, offer.Id, ct))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                OfferErrorCodes.OpenCancellationRequestExists,
                "Another open cancellation request already exists for this offer.", ct);
            return;
        }

        if (!OfferLifecycleQueries.TryTransition(() => offer.RequestCancellation(), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }

        var now = _clock.GetUtcNow();
        var request = OfferCancellationRequest.ByUser(
            id: Guid.NewGuid(),
            offerId: offer.Id,
            requestedByUserId: sub,
            cancellationReasonId: req.ReasonId,
            otherReason: req.OtherReason,
            requestedAt: now);
        _db.OfferCancellationRequests.Add(request);

        _outbox.Enqueue(OfferEventTypes.CancellationRequested, nameof(Offer), offer.Id,
            new { id = offer.Id, cancellationRequestId = request.Id, requestedByUserId = sub });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }

    public static Task<bool> HasOpenCancellationRequestAsync(
        AppDbContext db, Guid offerId, CancellationToken ct)
    {
        return db.OfferCancellationRequests
            .AsNoTracking()
            .AnyAsync(c => c.OfferId == offerId && !c.IsApproved && !c.IsRejected, ct);
    }
}

/// <summary><c>POST /api/users/offers/{id}/approve-cancellation</c> +
/// canonical — worker approves a cancellation opened by the
/// establishment.</summary>
public sealed class UserApproveCancellationEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public UserApproveCancellationEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/users/offers/{id}/approve-cancellation",
             "/api/v1/users/offers/{id}/approve-cancellation");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Worker approves a cancellation opened by the establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var offerId = Route<Guid>("id");
        var offer = await OfferLifecycleQueries.LoadUserOwnedOfferAsync(_db, offerId, sub, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        var request = await LoadOpenRequestAsync(_db, offer.Id, ct);
        if (request is null)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.NoOpenCancellationRequest,
                "No open cancellation request for this offer.", ct);
            return;
        }

        // Cancellation must have been opened by the OTHER party (the
        // establishment) — workers don't approve their own.
        if (request.RequestedByUserId is not null)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.ForbiddenForCaller,
                "Only the counterparty can approve a cancellation request.", ct);
            return;
        }

        var now = _clock.GetUtcNow();
        if (!OfferLifecycleQueries.TryTransition(() => offer.ApproveCancellation(), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }
        request.Approve(sub, now);

        _outbox.Enqueue(OfferEventTypes.CancellationApproved, nameof(Offer), offer.Id,
            new { id = offer.Id, approvedAt = now, approvedByUserId = sub });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }

    internal static Task<OfferCancellationRequest?> LoadOpenRequestAsync(
        AppDbContext db, Guid offerId, CancellationToken ct)
    {
        return db.OfferCancellationRequests
            .Where(c => c.OfferId == offerId && !c.IsApproved && !c.IsRejected)
            .OrderByDescending(c => c.RequestedAt)
            .FirstOrDefaultAsync(ct);
    }
}

/// <summary><c>POST /api/users/offers/{id}/reject-cancellation</c> +
/// canonical — worker rejects a cancellation opened by the establishment.</summary>
public sealed class UserRejectCancellationEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public UserRejectCancellationEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/users/offers/{id}/reject-cancellation",
             "/api/v1/users/offers/{id}/reject-cancellation");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Worker rejects a cancellation opened by the establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var offerId = Route<Guid>("id");
        var offer = await OfferLifecycleQueries.LoadUserOwnedOfferAsync(_db, offerId, sub, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        var request = await UserApproveCancellationEndpoint.LoadOpenRequestAsync(_db, offer.Id, ct);
        if (request is null)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.NoOpenCancellationRequest,
                "No open cancellation request for this offer.", ct);
            return;
        }
        if (request.RequestedByUserId is not null)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.ForbiddenForCaller,
                "Only the counterparty can reject a cancellation request.", ct);
            return;
        }

        var now = _clock.GetUtcNow();
        if (!OfferLifecycleQueries.TryTransition(() => offer.RejectCancellation(), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }
        request.Reject(sub, now);

        _outbox.Enqueue(OfferEventTypes.CancellationRejected, nameof(Offer), offer.Id,
            new { id = offer.Id, rejectedAt = now, rejectedByUserId = sub });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}

// === Establishment-side cancellation flow ================================

/// <summary><c>POST /api/establishments/offers/cancel</c> + canonical —
/// establishment (the sender) opens a cancellation request on an
/// accepted offer.</summary>
public sealed class EstablishmentCancelOfferEndpoint : Endpoint<CancelOfferRequest, OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public EstablishmentCancelOfferEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/establishments/offers/cancel",
             "/api/v1/establishments/{establishmentId}/offers/cancel");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Establishment opens a cancellation request on an offer it sent.");
    }

    public override async Task HandleAsync(CancelOfferRequest req, CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var offer = await OfferLifecycleQueries.LoadEstablishmentSentOfferAsync(
            _db, req.OfferId, establishmentId.Value, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        if (await UserCancelOfferEndpoint.HasOpenCancellationRequestAsync(_db, offer.Id, ct))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                OfferErrorCodes.OpenCancellationRequestExists,
                "Another open cancellation request already exists for this offer.", ct);
            return;
        }

        if (!OfferLifecycleQueries.TryTransition(() => offer.RequestCancellation(), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }

        var now = _clock.GetUtcNow();
        var request = OfferCancellationRequest.ByEstablishment(
            id: Guid.NewGuid(),
            offerId: offer.Id,
            requestedByEstablishmentId: establishmentId.Value,
            cancellationReasonId: req.ReasonId,
            otherReason: req.OtherReason,
            requestedAt: now);
        _db.OfferCancellationRequests.Add(request);

        _outbox.Enqueue(OfferEventTypes.CancellationRequested, nameof(Offer), offer.Id,
            new { id = offer.Id, cancellationRequestId = request.Id, requestedByEstablishmentId = establishmentId.Value });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}

/// <summary><c>POST /api/establishments/offers/{id}/approve-cancellation</c>
/// + canonical — establishment approves a cancellation opened by the
/// applicant.</summary>
public sealed class EstablishmentApproveCancellationEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public EstablishmentApproveCancellationEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/establishments/offers/{id}/approve-cancellation",
             "/api/v1/establishments/{establishmentId}/offers/{id}/approve-cancellation");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Establishment approves a cancellation opened by the applicant.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var offerId = Route<Guid>("id");
        var offer = await OfferLifecycleQueries.LoadEstablishmentSentOfferAsync(
            _db, offerId, establishmentId.Value, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        var request = await UserApproveCancellationEndpoint.LoadOpenRequestAsync(_db, offer.Id, ct);
        if (request is null)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.NoOpenCancellationRequest,
                "No open cancellation request for this offer.", ct);
            return;
        }
        if (request.RequestedByEstablishmentId is not null)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.ForbiddenForCaller,
                "Only the counterparty can approve a cancellation request.", ct);
            return;
        }

        var now = _clock.GetUtcNow();
        if (!OfferLifecycleQueries.TryTransition(() => offer.ApproveCancellation(), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }
        request.Approve(sub, now);

        _outbox.Enqueue(OfferEventTypes.CancellationApproved, nameof(Offer), offer.Id,
            new { id = offer.Id, approvedAt = now, approvedByEstablishmentId = establishmentId.Value });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}

/// <summary><c>POST /api/establishments/offers/{id}/reject-cancellation</c>
/// + canonical.</summary>
public sealed class EstablishmentRejectCancellationEndpoint : EndpointWithoutRequest<OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public EstablishmentRejectCancellationEndpoint(AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; }

    public override void Configure()
    {
        Post("/api/establishments/offers/{id}/reject-cancellation",
             "/api/v1/establishments/{establishmentId}/offers/{id}/reject-cancellation");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Establishment rejects a cancellation opened by the applicant.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var offerId = Route<Guid>("id");
        var offer = await OfferLifecycleQueries.LoadEstablishmentSentOfferAsync(
            _db, offerId, establishmentId.Value, ct);
        if (offer is null) { await Send.NotFoundAsync(ct); return; }

        var request = await UserApproveCancellationEndpoint.LoadOpenRequestAsync(_db, offer.Id, ct);
        if (request is null)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.NoOpenCancellationRequest,
                "No open cancellation request for this offer.", ct);
            return;
        }
        if (request.RequestedByEstablishmentId is not null)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.ForbiddenForCaller,
                "Only the counterparty can reject a cancellation request.", ct);
            return;
        }

        var now = _clock.GetUtcNow();
        if (!OfferLifecycleQueries.TryTransition(() => offer.RejectCancellation(), out var err))
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition, err, ct);
            return;
        }
        request.Reject(sub, now);

        _outbox.Enqueue(OfferEventTypes.CancellationRejected, nameof(Offer), offer.Id,
            new { id = offer.Id, rejectedAt = now, rejectedByEstablishmentId = establishmentId.Value });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(await OfferReadMapper.MapAsync(_db, offer, now, ct), ct);
    }
}
