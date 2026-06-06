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

namespace Matloob.Api.Features.Establishments.Members.AddMember;

/// <summary>
/// <c>POST /api/v1/establishments/{id}/members</c> — adds a new active
/// member to an Approved establishment.
///
/// Authorization (spec §10):
/// - Active Owner of this establishment, OR
/// - matloob_admin.
///
/// Status guard: Establishment must be in <see cref="EstablishmentStatus.Approved"/>.
/// PendingReview / Draft / Rejected return 409 with code
/// <c>cannot_edit_in_status</c>. Suspended will return 423 once that flow
/// lands; for now the same 409 catches it.
///
/// Validation:
/// - <c>userId</c> required (non-empty). The local <c>users</c> table does
///   not exist yet (a later phase), so we accept any string that isn't
///   blank. The seam is documented so the future
///   "user_not_found_in_system" 422 lands here without restructuring.
/// - <c>role</c> must be a defined <see cref="EstablishmentMemberRole"/>
///   value; the JSON enum converter rejects unknown strings with a 400
///   automatically.
/// - The pair (establishmentId, userId) must not already have an active
///   row — 409 if it does. Inactive rows are NOT reactivated by add;
///   PATCH does that explicitly.
///
/// Response: 201 with the new <see cref="EstablishmentMember"/> shape.
/// </summary>
public sealed class AddMemberEndpoint : Endpoint<AddMemberRequest, AddMemberResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public AddMemberEndpoint(
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
        Post("/api/v1/establishments/{id}/members");
        Description(b => b
            .Produces<AddMemberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Add a member to an Approved establishment.";
            s.Description =
                "Active Owner of this establishment OR matloob_admin. " +
                "Establishment must be Approved. Duplicate active membership " +
                "returns 409.";
        });
    }

    public override async Task HandleAsync(AddMemberRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");

        if (string.IsNullOrWhiteSpace(req.UserId))
        {
            AddError(r => r.UserId, "UserId is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Authorization: Owner OR admin.
        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isOwner = await MembershipChecks.HasPermissionAsync(
                _db, id, _currentUser.UserId, Infrastructure.Auth.Permissions.Members.Manage, ct);
            if (!isOwner)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }
        }

        // Suspended -> 423 Locked. Distinct from cannot_edit_in_status so
        // the public frontend can tell "ask support" from "wrong state."
        if (await EstablishmentStatusGuards.WriteIfSuspendedAsync(HttpContext, establishment, ct))
        {
            return;
        }

        // Status guard for the remaining non-Approved cases.
        if (establishment.Status != EstablishmentStatus.Approved)
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Members can only be added in Status=Approved. Current: {establishment.Status}.",
                ct);
            return;
        }

        // Duplicate-membership guard.
        var alreadyMember = await _db.EstablishmentMembers
            .AsNoTracking()
            .AnyAsync(m =>
                m.EstablishmentId == id &&
                m.UserId == req.UserId &&
                m.IsActive,
                ct);
        if (alreadyMember)
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.MemberAlreadyExists,
                "User is already an active member of this establishment.",
                ct);
            return;
        }

        // Spec §6.3: the user being added must already exist in the local
        // users table. The CurrentUserSyncMiddleware populates rows on the
        // first authenticated request a user makes, so a user that has
        // never logged in won't have a local row -- 422 in that case.
        // An IsActive=false row also fails: that account has been blocked.
        var targetUserId = req.UserId.Trim();
        var targetIsActive = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.IdentityId == targetUserId && u.IsActive, ct);
        if (!targetIsActive)
        {
            await WriteProblemAsync(
                StatusCodes.Status422UnprocessableEntity,
                EstablishmentErrorCodes.UserNotFoundInSystem,
                "User has never logged in or is inactive. Ask them to log in once before being added.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        var member = new EstablishmentMember(
            id: Guid.NewGuid(),
            establishmentId: id,
            userId: req.UserId.Trim(),
            role: req.Role,
            addedByUserId: _currentUser.UserId,
            addedAt: now,
            isActive: true);
        _db.EstablishmentMembers.Add(member);

        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: id,
            action: EstablishmentReviewAction.MemberAdded,
            occurredAt: now,
            actorUserId: isAdmin ? null : _currentUser.UserId,
            actorAdminId: isAdmin ? _currentUser.UserId : null,
            snapshotJson: $"{{\"memberId\":\"{member.Id}\",\"userId\":\"{member.UserId}\",\"role\":\"{member.Role}\"}}"));

        _outbox.Enqueue(
            EstablishmentEventTypes.MemberAdded,
            aggregateType: nameof(Establishment),
            aggregateId: id,
            payload: new
            {
                establishmentId = id,
                memberId = member.Id,
                userId = member.UserId,
                role = member.Role.ToString(),
                addedAt = now,
                addedByUserId = _currentUser.UserId,
                addedByAdmin = isAdmin,
            });
        _outbox.Flush();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueConstraintTranslator.TryTranslate(ex) is { } conflict)
        {
            // Racing AddMember calls slip past the application-side
            // duplicate check and hit ux_establishment_members_pair_active.
            await WriteConflictAsync(conflict.Code, conflict.Detail, ct);
            return;
        }

        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{id}/members/{member.Id}";
        await Send.ResponseAsync(
            new AddMemberResponse(
                Id: member.Id,
                EstablishmentId: id,
                UserId: member.UserId,
                Role: member.Role,
                IsActive: member.IsActive,
                AddedAt: member.AddedAt),
            StatusCodes.Status201Created,
            ct);
    }

    private Task WriteConflictAsync(string code, string detail, CancellationToken ct) =>
        WriteProblemAsync(StatusCodes.Status409Conflict, code, detail, ct);

    private async Task WriteProblemAsync(int status, string code, string detail, CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status409Conflict => "Conflict",
                StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
                _ => "Error",
            },
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["code"] = code;
        HttpContext.Response.StatusCode = status;
        HttpContext.Response.ContentType = "application/problem+json";
        await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
    }
}

public sealed class AddMemberRequest
{
    public string UserId { get; init; } = string.Empty;
    public EstablishmentMemberRole Role { get; init; } = EstablishmentMemberRole.Other;
}

public sealed record AddMemberResponse(
    Guid Id,
    Guid EstablishmentId,
    string UserId,
    EstablishmentMemberRole Role,
    bool IsActive,
    DateTimeOffset AddedAt);
