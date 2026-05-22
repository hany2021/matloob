using System.Text.Json.Serialization;
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
/// snake_case via <see cref="JsonPropertyNameAttribute"/>. <c>status</c> and
/// <c>role</c> are returned as new-client extension fields (Laravel parsers
/// ignore unknown keys).
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
            select new MyEstablishmentListItem
            {
                Id = e.Id,
                Name = e.Name,
                Type = "establishment",
                Logo = null,
                LaborOfficeId = e.LaborOfficeId,
                SequenceNumber = e.SequenceNumber,
                Status = e.Status.ToString(),
                Role = m.Role.ToString(),
            }).ToListAsync(ct);

        await Send.OkAsync(rows, ct);
    }
}

/// <summary>
/// Wire shape returned by the establishment-list endpoints. Mirrors the
/// Laravel <c>Users/Me/Profiles/EstablishmentResource::toArray()</c> field
/// set (id, name, type, logo, labor_office_id, sequence_number) and adds
/// new-client extensions (status, role) that Laravel parsers ignore.
/// </summary>
public sealed class MyEstablishmentListItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "establishment";

    [JsonPropertyName("logo")]
    public string? Logo { get; init; }

    [JsonPropertyName("labor_office_id")]
    public string LaborOfficeId { get; init; } = string.Empty;

    [JsonPropertyName("sequence_number")]
    public string SequenceNumber { get; init; } = string.Empty;

    /// <summary>New-client extension: establishment status (Approved/Suspended).</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>New-client extension: the caller's role in this establishment.</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;
}
