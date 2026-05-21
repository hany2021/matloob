using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.AdminReview.Reinstate;

/// <summary>
/// <c>POST /api/v1/admin/establishments/{id}/reinstate</c> — admin lifts a
/// suspension and flips Suspended → Approved (spec §8).
///
/// On success:
/// - Aggregate's <see cref="Establishment.Reinstate"/> stamps Status=Approved
///   AND CLEARS the live suspension triplet (SuspendedAt /
///   SuspendedByAdminId / SuspensionReason). The audit row preserves it.
/// - An <see cref="EstablishmentReviewHistory"/> row is appended with
///   action <c>Reinstated</c>.
/// - In-flight ChangeRequests / Members / Offers etc. resume exactly as
///   they were when suspension hit (spec §8). The 423 guard simply stops
///   firing once Status leaves Suspended.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class ReinstateEndpoint : EndpointWithoutRequest<ReinstateResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public ReinstateEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider clock,
        IOutboxWriter outbox)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _outbox = outbox;
    }

    public override void Configure()
    {
        Post("/api/v1/admin/establishments/{id}/reinstate");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ReinstateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Lift the suspension on a Suspended establishment.";
            s.Description =
                "Status returns to Approved and the live suspension fields " +
                "are cleared. The audit row (action=Suspended) stays as the " +
                "durable record. No body required.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (establishment.Status != EstablishmentStatus.Suspended)
        {
            await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot reinstate in status '{establishment.Status}'. Allowed: Suspended.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        establishment.Reinstate(now, _currentUser.UserId);

        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishment.Id,
            action: EstablishmentReviewAction.Reinstated,
            occurredAt: now,
            actorAdminId: _currentUser.UserId));

        _outbox.Enqueue(
            EstablishmentEventTypes.Reinstated,
            aggregateType: nameof(Establishment),
            aggregateId: establishment.Id,
            payload: new
            {
                establishmentId = establishment.Id,
                reinstatedByAdminId = _currentUser.UserId,
                reinstatedAt = now,
            });
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(new ReinstateResponse(
            Id: establishment.Id,
            Status: establishment.Status,
            ReinstatedAt: now), ct);
    }
}

public sealed record ReinstateResponse(
    Guid Id,
    EstablishmentStatus Status,
    DateTimeOffset ReinstatedAt);
