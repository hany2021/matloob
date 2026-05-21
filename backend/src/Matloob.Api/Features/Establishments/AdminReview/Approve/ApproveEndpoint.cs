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

namespace Matloob.Api.Features.Establishments.AdminReview.Approve;

/// <summary>
/// <c>POST /api/v1/admin/establishments/{id}/approve</c> — admin transitions
/// PendingReview → Approved.
///
/// On success:
/// - Aggregate's <see cref="Establishment.Approve"/> stamps ApprovedAt /
///   ApprovedByAdminId and flips Status.
/// - The creator becomes the first <see cref="EstablishmentMember"/> with
///   role Owner (spec §6.2). Idempotent: if an active Owner row for that
///   user already exists (e.g. legacy import + manual approval), we do not
///   insert a duplicate.
/// - An <see cref="EstablishmentReviewHistory"/> row is appended with
///   action <c>Approved</c>.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class ApproveEndpoint : EndpointWithoutRequest<ApproveResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public ApproveEndpoint(
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
        Post("/api/v1/admin/establishments/{id}/approve");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ApproveResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "Approve a PendingReview establishment.";
            s.Description =
                "Creates the first Owner member from CreatedByUserId, " +
                "appends a Review row, transitions Status to Approved.";
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

        if (establishment.Status != EstablishmentStatus.PendingReview)
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot approve in status '{establishment.Status}'. Allowed: PendingReview.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        establishment.Approve(now, _currentUser.UserId);

        // Idempotency: insert the Owner member only if one doesn't already
        // exist. The partial unique index on (establishment_id, user_id)
        // would block a duplicate active row anyway, but checking first lets
        // a retry of /approve succeed cleanly (e.g. after a network hiccup).
        var hasOwner = await _db.EstablishmentMembers
            .AnyAsync(m =>
                m.EstablishmentId == establishment.Id
                && m.UserId == establishment.CreatedByUserId,
                ct);
        if (!hasOwner)
        {
            _db.EstablishmentMembers.Add(new EstablishmentMember(
                id: Guid.NewGuid(),
                establishmentId: establishment.Id,
                userId: establishment.CreatedByUserId,
                role: EstablishmentMemberRole.Owner,
                addedByUserId: string.Empty, // system-driven on approval; no human Owner exists yet.
                addedAt: now,
                isActive: true));
        }

        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishment.Id,
            action: EstablishmentReviewAction.Approved,
            occurredAt: now,
            actorAdminId: _currentUser.UserId));

        _outbox.Enqueue(
            EstablishmentEventTypes.Approved,
            aggregateType: nameof(Establishment),
            aggregateId: establishment.Id,
            payload: new
            {
                establishmentId = establishment.Id,
                approvedByAdminId = _currentUser.UserId,
                approvedAt = now,
                ownerUserId = establishment.CreatedByUserId,
            });
        _outbox.Flush();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueConstraintTranslator.TryTranslate(ex) is { } conflict)
        {
            // Two admins racing the same approve could both try to insert
            // a first-Owner member row; ux_establishment_members_pair_active
            // settles it.
            await WriteConflictAsync(conflict.Code, conflict.Detail, ct);
            return;
        }

        await Send.OkAsync(new ApproveResponse(
            Id: establishment.Id,
            Status: establishment.Status,
            ApprovedAt: establishment.ApprovedAt!.Value), ct);
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

public sealed record ApproveResponse(
    Guid Id,
    EstablishmentStatus Status,
    DateTimeOffset ApprovedAt);
