using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Establishments.AdminReview.Reject;

/// <summary>
/// <c>POST /api/v1/admin/establishments/{id}/reject</c> — admin transitions
/// PendingReview → Rejected with a mandatory reason.
///
/// On success:
/// - Aggregate's <see cref="Establishment.Reject"/> stamps RejectedAt /
///   RejectedByAdminId / RejectionReason and flips Status.
/// - An <see cref="EstablishmentReviewHistory"/> row is appended with
///   action <c>Rejected</c> and Reason copied from the request.
///
/// The user can immediately edit the rejected row and resubmit; the
/// SubmitForReview endpoint will clear the Rejected* triplet on the next
/// transition (the audit row preserves it).
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class RejectEndpoint : Endpoint<RejectRequest, RejectResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public RejectEndpoint(
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
        Post("/api/v1/admin/establishments/{id}/reject");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<RejectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Reject a PendingReview establishment with a reason.";
            s.Description =
                "Reason is required (1-2000 chars). On success, status " +
                "becomes Rejected; the creator can edit + resubmit.";
        });
    }

    public override async Task HandleAsync(RejectRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");

        if (string.IsNullOrWhiteSpace(req.Reason))
        {
            AddError(r => r.Reason, "Reason is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }
        if (req.Reason.Length > 2000)
        {
            AddError(r => r.Reason, "Reason must be 2000 characters or fewer.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (establishment.Status != EstablishmentStatus.PendingReview)
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot reject in status '{establishment.Status}'. Allowed: PendingReview.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        establishment.Reject(now, _currentUser.UserId, req.Reason);

        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishment.Id,
            action: EstablishmentReviewAction.Rejected,
            occurredAt: now,
            actorAdminId: _currentUser.UserId,
            reason: establishment.RejectionReason));

        _outbox.Enqueue(
            EstablishmentEventTypes.Rejected,
            aggregateType: nameof(Establishment),
            aggregateId: establishment.Id,
            payload: new
            {
                establishmentId = establishment.Id,
                rejectedByAdminId = _currentUser.UserId,
                rejectedAt = now,
                reason = establishment.RejectionReason,
                createdByUserId = establishment.CreatedByUserId,
            });
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(new RejectResponse(
            Id: establishment.Id,
            Status: establishment.Status,
            RejectedAt: establishment.RejectedAt!.Value,
            RejectionReason: establishment.RejectionReason!), ct);
    }

    private async Task WriteConflictAsync(string code, string detail, CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = detail,
            Type = "https://httpstatuses.io/409",
        };
        problem.Extensions["code"] = code;
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        HttpContext.Response.ContentType = "application/problem+json";
        await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
    }
}

public sealed class RejectRequest
{
    public string Reason { get; init; } = string.Empty;
}

public sealed record RejectResponse(
    Guid Id,
    EstablishmentStatus Status,
    DateTimeOffset RejectedAt,
    string RejectionReason);
