using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Establishments.Members.UpdateMember;

/// <summary>
/// <c>PATCH /api/v1/establishments/{id}/members/{memberId}</c> — change a
/// member's role and/or active flag. Both <c>role</c> and <c>isActive</c>
/// are optional; null means "no change."
///
/// Last-Owner protection (spec §6.2): if the row being updated is the
/// only active Owner left AND the change would demote it (different role)
/// or deactivate it (isActive=false), the request is rejected with
/// 409 <c>last_owner_protected</c>. The check uses
/// <see cref="MembershipChecks.WouldDropLastOwnerAsync"/> to keep the rule
/// in one place across PATCH and DELETE.
///
/// Auth: active Owner of the establishment OR matloob_admin. Soft-deleted
/// rows are hidden by the global filter so they surface as 404 here.
/// </summary>
public sealed class UpdateMemberEndpoint : Endpoint<UpdateMemberRequest, UpdateMemberResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateMemberEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Patch("/api/v1/establishments/{id}/members/{memberId}");
        Description(b => b
            .Produces<UpdateMemberResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Change a member's role and/or active flag.";
            s.Description =
                "Active Owner of this establishment OR matloob_admin. " +
                "Cannot demote or deactivate the last active Owner.";
        });
    }

    public override async Task HandleAsync(UpdateMemberRequest req, CancellationToken ct)
    {
        var establishmentId = Route<Guid>("id");
        var memberId = Route<Guid>("memberId");

        if (req.Role is null && req.IsActive is null)
        {
            AddError("No-op: at least one of 'role' or 'isActive' must be provided.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == establishmentId, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var member = await _db.EstablishmentMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.EstablishmentId == establishmentId, ct);
        if (member is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Authorization: Owner OR admin.
        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isOwner = await MembershipChecks.HasPermissionAsync(
                _db, establishmentId, _currentUser.UserId, Infrastructure.Auth.Permissions.Members.Manage, ct);
            if (!isOwner)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }
        }

        // Suspended -> 423 Locked.
        if (await EstablishmentStatusGuards.WriteIfSuspendedAsync(HttpContext, establishment, ct))
        {
            return;
        }

        // Status guard for the remaining non-Approved cases.
        if (establishment.Status != EstablishmentStatus.Approved)
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Members can only be updated in Status=Approved. Current: {establishment.Status}.",
                ct);
            return;
        }

        // Last-Owner check happens against the CURRENT row state (i.e. before
        // we apply the change). If the row is an active Owner today AND the
        // change either drops it from Owner OR deactivates it, refuse unless
        // some OTHER active Owner survives.
        var wouldDemote = req.Role is { } newRole && newRole != EstablishmentMemberRole.Owner
            && member.Role == EstablishmentMemberRole.Owner;
        var wouldDeactivate = req.IsActive is false && member.IsActive;
        if ((wouldDemote || wouldDeactivate)
            && await MembershipChecks.WouldDropLastOwnerAsync(_db, member, ct))
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.LastOwnerProtected,
                "Cannot demote or deactivate the last active Owner of this establishment.",
                ct);
            return;
        }

        // Apply changes through the aggregate's behavior methods.
        if (req.Role is { } r) member.ChangeRole(r);
        if (req.IsActive is { } active)
        {
            if (active) member.Reactivate(); else member.Deactivate();
        }

        await _db.SaveChangesAsync(ct);

        await Send.OkAsync(
            new UpdateMemberResponse(
                Id: member.Id,
                EstablishmentId: member.EstablishmentId,
                UserId: member.UserId,
                Role: member.Role,
                IsActive: member.IsActive,
                UpdatedAt: member.UpdatedAt),
            ct);
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

public sealed class UpdateMemberRequest
{
    /// <summary>New role; null leaves it unchanged.</summary>
    public EstablishmentMemberRole? Role { get; init; }

    /// <summary>New active flag; null leaves it unchanged.</summary>
    public bool? IsActive { get; init; }
}

public sealed record UpdateMemberResponse(
    Guid Id,
    Guid EstablishmentId,
    string UserId,
    EstablishmentMemberRole Role,
    bool IsActive,
    DateTimeOffset? UpdatedAt);
