using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Members.ListMembers;

/// <summary>
/// <c>GET /api/v1/establishments/{id}/members</c> — returns every active
/// <see cref="EstablishmentMember"/> row for the establishment.
///
/// Auth: authenticated user; allowed if the caller is an active member of
/// the establishment OR a <c>matloob_admin</c>. Non-member non-admin
/// returns 403; anonymous returns 401.
///
/// Soft-deleted rows are hidden by the global query filter. Inactive rows
/// (IsActive=false) are not soft-deleted but ARE excluded from this read
/// — the response shape promises "the people who can currently act." The
/// PATCH endpoint can reactivate a deactivated row by Id; admins who need
/// to see deactivated rows will get a dedicated <c>?includeInactive=true</c>
/// flag once that requirement lands.
/// </summary>
public sealed class ListMembersEndpoint : EndpointWithoutRequest<ListMembersResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListMembersEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/v1/establishments/{id}/members");
        // No Policies(...) here -- authentication only; per-row authorization
        // happens inside HandleAsync because membership is a DB lookup.
        Description(b => b
            .Produces<ListMembersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "List active members of an establishment.";
            s.Description =
                "Active member or matloob_admin only. Soft-deleted and " +
                "inactive members are excluded.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        // Confirm the establishment row exists before any authorization
        // check. A 404 here applies regardless of caller role.
        var exists = await _db.Establishments
            .AsNoTracking()
            .AnyAsync(e => e.Id == id, ct);
        if (!exists)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var principal = HttpContext.User;
        var isAdmin = MembershipChecks.IsAdmin(principal);
        if (!isAdmin)
        {
            var isMember = await MembershipChecks.IsActiveMemberAsync(
                _db, id, _currentUser.UserId, ct);
            if (!isMember)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }
        }

        var rows = await _db.EstablishmentMembers
            .AsNoTracking()
            .Where(m => m.EstablishmentId == id && m.IsActive)
            .OrderBy(m => m.AddedAt)
            .ThenBy(m => m.Id)
            .Select(m => new MemberSummary(
                m.Id,
                m.UserId,
                m.Role,
                m.IsActive,
                m.AddedAt,
                m.AddedByUserId))
            .ToListAsync(ct);

        await Send.OkAsync(new ListMembersResponse(id, rows), ct);
    }
}

public sealed record ListMembersResponse(
    Guid EstablishmentId,
    IReadOnlyList<MemberSummary> Members);

public sealed record MemberSummary(
    Guid Id,
    string UserId,
    EstablishmentMemberRole Role,
    bool IsActive,
    DateTimeOffset AddedAt,
    string AddedByUserId);
