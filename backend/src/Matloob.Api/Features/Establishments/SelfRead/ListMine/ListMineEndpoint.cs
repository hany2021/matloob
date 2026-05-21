using FastEndpoints;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.SelfRead.ListMine;

/// <summary>
/// <c>GET /api/v1/establishments</c> — every establishment the caller can
/// reach, with action flags suitable for a dashboard card.
///
/// Visibility (spec §10):
/// - Establishments the caller CREATED while status is Draft / PendingReview
///   / Rejected. Once an establishment hits Approved, creator visibility is
///   subsumed by their first Owner membership.
/// - Establishments where the caller has an active <see cref="EstablishmentMember"/>
///   row.
///
/// Admin passthrough is intentionally NOT here — admins have their own
/// queues at <c>/api/v1/admin/establishments/*</c>. If an admin is also a
/// member of an establishment, the membership grant surfaces it.
///
/// The action flags (<c>canEdit</c>, <c>canSubmit</c>, <c>canManageMembers</c>,
/// <c>canCreateChangeRequest</c>) reflect the server-side rules pre-baked
/// for the public frontend so the dashboard doesn't have to duplicate them.
/// They consider both the establishment's status AND the caller's role on
/// it — see <see cref="ListMineItem"/> for the per-flag wire shape.
/// </summary>
public sealed class ListMineEndpoint : EndpointWithoutRequest<ListMineResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListMineEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/v1/establishments");
        // Authentication only — visibility filtering happens in the query.
        Description(b => b
            .Produces<ListMineResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "List establishments the current user can act on.";
            s.Description =
                "Returns rows where the caller is the creator (Draft / " +
                "PendingReview / Rejected) OR an active member. Admin " +
                "passthrough is not included -- admins use the admin queue.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;

        // Two source sets, unioned in memory:
        //   1) rows I created where status is still in the pre-approval phase
        //   2) rows where I have an active membership row
        // We grab them as raw entities + my role + my membership-id, then
        // collapse to one row per Establishment (an active Owner who also
        // created the row would otherwise duplicate).
        var createdRows = await _db.Establishments
            .AsNoTracking()
            .Where(e => e.CreatedByUserId == sub)
            .Where(e => e.Status == EstablishmentStatus.Draft
                     || e.Status == EstablishmentStatus.PendingReview
                     || e.Status == EstablishmentStatus.Rejected)
            .ToListAsync(ct);

        var memberPairs = await _db.EstablishmentMembers
            .AsNoTracking()
            .Where(m => m.UserId == sub && m.IsActive)
            .Select(m => new { m.EstablishmentId, m.Role })
            .ToListAsync(ct);

        var memberRoleByEstablishment = memberPairs
            .ToDictionary(p => p.EstablishmentId, p => p.Role);

        IEnumerable<Establishment> memberRows = Array.Empty<Establishment>();
        if (memberRoleByEstablishment.Count > 0)
        {
            var memberEstablishmentIds = memberRoleByEstablishment.Keys.ToList();
            memberRows = await _db.Establishments
                .AsNoTracking()
                .Where(e => memberEstablishmentIds.Contains(e.Id))
                .ToListAsync(ct);
        }

        var merged = new Dictionary<Guid, Establishment>();
        foreach (var e in createdRows) merged[e.Id] = e;
        foreach (var e in memberRows) merged[e.Id] = e;

        var items = merged.Values
            .OrderByDescending(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .Select(e =>
            {
                memberRoleByEstablishment.TryGetValue(e.Id, out var role);
                var myRole = memberRoleByEstablishment.ContainsKey(e.Id)
                    ? (EstablishmentMemberRole?)role
                    : null;

                return new ListMineItem(
                    Id: e.Id,
                    Name: e.Name,
                    CommercialRegistrationNumber: e.CommercialRegistrationNumber,
                    Status: e.Status,
                    City: e.City,
                    CreatedByUserId: e.CreatedByUserId,
                    SubmittedAt: e.SubmittedAt,
                    ApprovedAt: e.ApprovedAt,
                    RejectedAt: e.RejectedAt,
                    SuspendedAt: e.SuspendedAt,
                    IsLegacyImport: e.IsLegacyImport,
                    MyRole: myRole,
                    CanEdit: CanEdit(e, sub),
                    CanSubmit: CanSubmit(e, sub),
                    CanManageMembers: myRole == EstablishmentMemberRole.Owner && e.Status == EstablishmentStatus.Approved,
                    CanCreateChangeRequest: myRole == EstablishmentMemberRole.Owner && e.Status == EstablishmentStatus.Approved);
            })
            .ToList();

        await Send.OkAsync(new ListMineResponse(items), ct);
    }

    private static bool CanEdit(Establishment e, string sub) =>
        // Only the creator can write basic-info / documents, and only while
        // the row is Draft or Rejected. Approved establishments go through
        // ChangeRequest -- the canCreateChangeRequest flag below.
        e.IsEditableByCreator
        && string.Equals(e.CreatedByUserId, sub, StringComparison.Ordinal);

    private static bool CanSubmit(Establishment e, string sub) =>
        // Same gate as CanEdit -- submitting transitions Draft / Rejected
        // -> PendingReview. The §3.1 completeness + CR-uniqueness gates
        // are evaluated server-side at submission time, so this flag is
        // only the lifecycle-level answer; the dashboard should still
        // surface field errors when the submit endpoint returns 400.
        CanEdit(e, sub);
}

public sealed record ListMineResponse(IReadOnlyList<ListMineItem> Items);

public sealed record ListMineItem(
    Guid Id,
    string Name,
    string CommercialRegistrationNumber,
    EstablishmentStatus Status,
    string City,
    string CreatedByUserId,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    DateTimeOffset? SuspendedAt,
    bool IsLegacyImport,
    EstablishmentMemberRole? MyRole,
    bool CanEdit,
    bool CanSubmit,
    bool CanManageMembers,
    bool CanCreateChangeRequest);
