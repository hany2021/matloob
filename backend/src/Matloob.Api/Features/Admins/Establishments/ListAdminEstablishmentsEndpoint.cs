using FastEndpoints;
using Matloob.Api.Features.Admins.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Admins.Establishments;

/// <summary>
/// <c>GET /api/v1/admin/establishments?role=organizer|operator</c> — admin
/// list of establishments. The .NET port of the legacy Filament
/// <c>EstablishmentResource</c> list. There is no organizer/operator TYPE in
/// the data model: an "organizer" is an establishment with
/// <c>can_manage_events = true</c>, and every establishment is an "operator"
/// by default. The optional <c>role</c> filter splits the single table into
/// the two admin screens.
///
/// Columns mirror the legacy list: name, city, economic activity, CR number +
/// expiry, opportunities count, contracts count (accepted offers it sent),
/// status, created-at. Search matches name / CR number / city / economic
/// activity. Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class ListAdminEstablishmentsEndpoint
    : Endpoint<ListAdminEstablishmentsRequest, ListAdminEstablishmentsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly AppDbContext _db;

    public ListAdminEstablishmentsEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/establishments");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ListAdminEstablishmentsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Admin.Establishments"));
        Summary(s => s.Summary = "List / search establishments (organizers or operators).");
    }

    public override async Task HandleAsync(ListAdminEstablishmentsRequest req, CancellationToken ct)
    {
        var page = req.Page < 1 ? 1 : req.Page;
        var pageSize = req.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => req.PageSize,
        };

        var query = _db.Establishments.AsNoTracking();

        // role: organizer => can_manage_events true; operator => false. Anything
        // else (or omitted) lists all establishments.
        var role = req.Role?.Trim().ToLowerInvariant();
        if (role == "organizer")
        {
            query = query.Where(e => e.CanManageEvents);
        }
        else if (role == "operator")
        {
            query = query.Where(e => !e.CanManageEvents);
        }

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim().ToLower();
            query = query.Where(e =>
                e.Name.ToLower().Contains(s)
                || e.CommercialRegistrationNumber.ToLower().Contains(s)
                || e.City.ToLower().Contains(s)
                || (e.EconomicActivity != null && e.EconomicActivity.ToLower().Contains(s)));
        }

        var total = await query.CountAsync(ct);

        var raw = await query
            .OrderBy(e => e.Name)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.City,
                e.EconomicActivity,
                e.CommercialRegistrationNumber,
                e.CommercialRegistrationExpiry,
                e.CanManageEvents,
                Status = e.Status.ToString(),
                e.CreatedAt,
                OpportunitiesCount = _db.Opportunities.Count(o => o.IssuerEstablishmentId == e.Id),
                ContractsCount = _db.Offers.Count(o =>
                    o.SenderEstablishmentId == e.Id && ContractStatuses.Set.Contains(o.Status)),
            })
            .ToListAsync(ct);

        var items = raw
            .Select(e => new AdminEstablishmentListItem(
                e.Id,
                e.Name,
                string.IsNullOrWhiteSpace(e.City) ? null : e.City,
                e.EconomicActivity,
                e.CommercialRegistrationNumber,
                e.CommercialRegistrationExpiry?.ToString("yyyy-MM-dd"),
                e.CanManageEvents,
                e.Status,
                e.OpportunitiesCount,
                e.ContractsCount,
                e.CreatedAt.ToString("yyyy-MM-dd")))
            .ToList();

        await Send.OkAsync(new ListAdminEstablishmentsResponse(page, pageSize, total, items), ct);
    }
}

public sealed class ListAdminEstablishmentsRequest
{
    [BindFrom("page")]
    public int Page { get; init; } = 1;

    [BindFrom("pageSize")]
    public int PageSize { get; init; } = 50;

    [BindFrom("search")]
    public string? Search { get; init; }

    /// <summary><c>organizer</c> (can_manage_events) or <c>operator</c> (the rest).</summary>
    [BindFrom("role")]
    public string? Role { get; init; }
}

public sealed record AdminEstablishmentListItem(
    Guid Id,
    string Name,
    string? City,
    string? EconomicActivity,
    string CrNumber,
    string? CrExpiry,
    bool CanManageEvents,
    string Status,
    int OpportunitiesCount,
    int ContractsCount,
    string CreatedAt);

public sealed record ListAdminEstablishmentsResponse(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<AdminEstablishmentListItem> Items);
