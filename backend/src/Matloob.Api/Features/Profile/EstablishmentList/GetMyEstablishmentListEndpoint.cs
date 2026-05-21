using FastEndpoints;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.EstablishmentList;

/// <summary>
/// <c>GET /api/users/profile/establishment-list</c> (Laravel-compat) +
/// <c>GET /api/v1/users/profile/establishment-list</c> (versioned) — list
/// the current user's establishments.
///
/// Compatibility matrix disposition (docs/20-api-compatibility-matrix.md):
/// "Currently calls QiwaApi; lists establishments linked to the user.
/// **PG-only.** Returns the user's active memberships in Approved/Suspended
/// establishments. Response shape preserved."
///
/// Auth: any authenticated principal. Anonymous -> 401.
///
/// Response shape (mirrors Laravel <c>EstablishmentResource</c>):
/// <code>
/// [
///   { id, name, type: "establishment", logo: null,
///     labor_office_id, sequence_number }
/// ]
/// </code>
/// - <c>id</c> is the establishment's GUID (Laravel returned the
///   commissioner-uuid; we use the canonical establishment id because
///   "commissioner" doesn't exist as an entity in the new system).
/// - <c>type</c> is always <c>"establishment"</c> for forward-compatibility
///   with the legacy frontend's switch.
/// - <c>logo</c> stays null until the logo-asset feature lands (the
///   matrix already calls this out as a follow-on).
///
/// PG-only: NO QiwaApi call. We surface only establishments where the
/// caller has an active <see cref="EstablishmentMember"/> row AND the
/// establishment status is Approved or Suspended (spec §10 read rules:
/// Suspended is read-allowed).
/// </summary>
public sealed class GetMyEstablishmentListEndpoint
    : EndpointWithoutRequest<IReadOnlyList<MyEstablishmentListItem>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetMyEstablishmentListEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/users/profile/establishment-list",
            "/api/v1/users/profile/establishment-list");
        Description(b => b
            .Produces<IReadOnlyList<MyEstablishmentListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Profile"));
        Summary(s =>
        {
            s.Summary = "Establishments the current user is an active member of.";
            s.Description =
                "PG-only -- no Qiwa call. Returns Approved or Suspended " +
                "establishments. Empty array if the user has no memberships.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var sub = _currentUser.UserId;

        // Active memberships -> their establishments where status is in
        // the read-allowed set (Approved + Suspended; Suspended still
        // permits reads per spec §8).
        var rows = await (
            from m in _db.EstablishmentMembers.AsNoTracking()
            join e in _db.Establishments.AsNoTracking() on m.EstablishmentId equals e.Id
            where m.UserId == sub
               && m.IsActive
               && (e.Status == EstablishmentStatus.Approved
                || e.Status == EstablishmentStatus.Suspended)
            orderby e.Name
            select new MyEstablishmentListItem(
                Id: e.Id,
                Name: e.Name,
                Type: "establishment",
                Logo: null,
                LaborOfficeId: e.LaborOfficeId,
                SequenceNumber: e.SequenceNumber,
                Status: e.Status,
                Role: m.Role)).ToListAsync(ct);

        await Send.OkAsync(rows, ct);
    }
}

public sealed record MyEstablishmentListItem(
    Guid Id,
    string Name,
    string Type,
    string? Logo,
    string LaborOfficeId,
    string SequenceNumber,
    EstablishmentStatus Status,
    EstablishmentMemberRole Role);
