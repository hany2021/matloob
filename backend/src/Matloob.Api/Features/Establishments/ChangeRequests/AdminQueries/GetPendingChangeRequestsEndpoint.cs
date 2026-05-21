using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.ChangeRequests.AdminQueries;

/// <summary>
/// <c>GET /api/v1/admin/establishments/change-requests/pending</c> — the
/// admin queue of change requests awaiting review. Paged by SubmittedAt
/// ascending so older requests bubble to the front.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class GetPendingChangeRequestsEndpoint
    : Endpoint<GetPendingChangeRequestsRequest, GetPendingChangeRequestsResponse>
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly AppDbContext _db;

    public GetPendingChangeRequestsEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/establishments/change-requests/pending");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<GetPendingChangeRequestsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "List change requests awaiting admin review.";
            s.Description = "Paged, oldest-first by SubmittedAt. Admin-only.";
        });
    }

    public override async Task HandleAsync(GetPendingChangeRequestsRequest req, CancellationToken ct)
    {
        var page = req.Page < 1 ? 1 : req.Page;
        var pageSize = req.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => req.PageSize,
        };

        // Join via in-memory mapping rather than a navigation property --
        // EstablishmentChangeRequest has no navigation to Establishment in
        // the EF model (deliberate, see Phase 7 commits). One small join
        // per page is fine.
        var baseQuery = _db.EstablishmentChangeRequests
            .AsNoTracking()
            .Where(cr => cr.Status == EstablishmentChangeRequestStatus.PendingReview);

        var total = await baseQuery.CountAsync(ct);

        var page1 = await baseQuery
            .OrderBy(cr => cr.SubmittedAt)
            .ThenBy(cr => cr.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var establishmentIds = page1.Select(cr => cr.EstablishmentId).ToList();
        var establishments = await _db.Establishments
            .AsNoTracking()
            .Where(e => establishmentIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.CommercialRegistrationNumber })
            .ToListAsync(ct);
        var byId = establishments.ToDictionary(e => e.Id);

        var items = page1.Select(cr =>
        {
            byId.TryGetValue(cr.EstablishmentId, out var est);
            return new PendingChangeRequestItem(
                Id: cr.Id,
                EstablishmentId: cr.EstablishmentId,
                EstablishmentName: est?.Name ?? string.Empty,
                CommercialRegistrationNumber: est?.CommercialRegistrationNumber ?? string.Empty,
                SubmittedAt: cr.SubmittedAt!.Value,
                CreatedByUserId: cr.CreatedByUserId);
        }).ToList();

        await Send.OkAsync(new GetPendingChangeRequestsResponse(
            Page: page,
            PageSize: pageSize,
            Total: total,
            Items: items), ct);
    }
}

public sealed class GetPendingChangeRequestsRequest
{
    [BindFrom("page")]
    public int Page { get; init; } = 1;
    [BindFrom("pageSize")]
    public int PageSize { get; init; } = 20;
}

public sealed record GetPendingChangeRequestsResponse(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<PendingChangeRequestItem> Items);

public sealed record PendingChangeRequestItem(
    Guid Id,
    Guid EstablishmentId,
    string EstablishmentName,
    string CommercialRegistrationNumber,
    DateTimeOffset SubmittedAt,
    string CreatedByUserId);
