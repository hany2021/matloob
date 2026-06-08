using FastEndpoints;
using Matloob.Api.Features.Admins.Common;
using Matloob.Api.Features.Offers.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Admins.Contracts;

/// <summary>
/// <c>GET /api/v1/admin/contracts</c> — admin list of contracts. In the
/// Ajeer-stripped model an accepted <see cref="Matloob.Domain.Offers.Offer"/>
/// IS the contract (no separate contracts table, no contract documents), so
/// this lists offers whose status is in <see cref="ContractStatuses.Set"/>.
/// Mirrors the legacy Filament contract resources (minus the dropped
/// document-viewer actions).
///
/// Columns: applicant (user or establishment), provider (sender
/// establishment), opportunity, status, start date, accepted date, salary.
/// Search matches the provider or opportunity name. Auth:
/// <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class ListAdminContractsEndpoint
    : Endpoint<ListAdminContractsRequest, ListAdminContractsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly AppDbContext _db;

    public ListAdminContractsEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/contracts");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ListAdminContractsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Admin.Contracts"));
        Summary(s => s.Summary = "List / search contracts (accepted offers).");
    }

    public override async Task HandleAsync(ListAdminContractsRequest req, CancellationToken ct)
    {
        var page = req.Page < 1 ? 1 : req.Page;
        var pageSize = req.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => req.PageSize,
        };

        var query = _db.Offers.AsNoTracking()
            .Where(o => ContractStatuses.Set.Contains(o.Status));

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim().ToLower();
            query = query.Where(o =>
                _db.Establishments.Any(e => e.Id == o.SenderEstablishmentId && e.Name.ToLower().Contains(s))
                || _db.Opportunities.Any(op => op.Id == o.OpportunityId && op.Name.ToLower().Contains(s)));
        }

        var total = await query.CountAsync(ct);

        var raw = await query
            .OrderByDescending(o => o.AcceptedAt ?? o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.Status,
                o.MonthlySalary,
                o.StartDate,
                o.EndDate,
                o.AcceptedAt,
                o.CreatedAt,
                ProviderName = _db.Establishments
                    .Where(e => e.Id == o.SenderEstablishmentId).Select(e => e.Name).FirstOrDefault(),
                OpportunityName = _db.Opportunities
                    .Where(op => op.Id == o.OpportunityId).Select(op => op.Name).FirstOrDefault(),
                ApplicantUserSub = _db.OpportunityApplications
                    .Where(a => a.Id == o.ApplicationId).Select(a => a.ApplicantUserId).FirstOrDefault(),
                ApplicantEstId = _db.OpportunityApplications
                    .Where(a => a.Id == o.ApplicationId).Select(a => a.ApplicantEstablishmentId).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Batch-resolve applicant display names (user sub -> name, est id -> name)
        // to avoid an N+1 over the page.
        var userSubs = raw.Where(r => r.ApplicantUserSub != null)
            .Select(r => r.ApplicantUserSub!).Distinct().ToList();
        var estIds = raw.Where(r => r.ApplicantEstId != null)
            .Select(r => r.ApplicantEstId!.Value).Distinct().ToList();

        var userNames = userSubs.Count == 0
            ? new Dictionary<string, string?>()
            : await _db.Users.AsNoTracking()
                .Where(u => userSubs.Contains(u.IdentityId))
                .ToDictionaryAsync(u => u.IdentityId, u => u.Name, ct);

        var estNames = estIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Establishments.AsNoTracking()
                .Where(e => estIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.Name, ct);

        var items = raw.Select(r =>
        {
            string? applicantName;
            string? applicantType;
            if (r.ApplicantUserSub != null)
            {
                applicantName = userNames.GetValueOrDefault(r.ApplicantUserSub);
                applicantType = "user";
            }
            else if (r.ApplicantEstId is { } estId)
            {
                applicantName = estNames.GetValueOrDefault(estId);
                applicantType = "establishment";
            }
            else
            {
                applicantName = null;
                applicantType = null;
            }

            var (statusLabel, statusColor) = OfferStatusPresentation.ForStatus(r.Status);

            return new AdminContractListItem(
                r.Id,
                applicantName,
                applicantType,
                r.ProviderName,
                r.OpportunityName,
                r.Status.ToWire(),
                statusLabel,
                statusColor,
                r.StartDate?.ToString("yyyy-MM-dd"),
                r.EndDate?.ToString("yyyy-MM-dd"),
                r.AcceptedAt?.ToString("yyyy-MM-dd"),
                r.MonthlySalary,
                r.CreatedAt.ToString("yyyy-MM-dd"));
        }).ToList();

        await Send.OkAsync(new ListAdminContractsResponse(page, pageSize, total, items), ct);
    }
}

public sealed class ListAdminContractsRequest
{
    [BindFrom("page")]
    public int Page { get; init; } = 1;

    [BindFrom("pageSize")]
    public int PageSize { get; init; } = 50;

    [BindFrom("search")]
    public string? Search { get; init; }
}

public sealed record AdminContractListItem(
    Guid Id,
    string? ApplicantName,
    string? ApplicantType,
    string? ProviderName,
    string? OpportunityName,
    string Status,
    string StatusLabel,
    string StatusColor,
    string? StartDate,
    string? EndDate,
    string? AcceptedAt,
    decimal? MonthlySalary,
    string CreatedAt);

public sealed record ListAdminContractsResponse(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<AdminContractListItem> Items);
