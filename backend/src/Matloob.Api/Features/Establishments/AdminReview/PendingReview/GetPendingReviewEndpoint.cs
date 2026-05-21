using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.AdminReview.PendingReview;

/// <summary>
/// <c>GET /api/v1/admin/establishments/pending-review</c> — the admin queue.
/// Returns establishments whose <see cref="EstablishmentStatus"/> is
/// PendingReview, paged by SubmittedAt ascending so older requests bubble to
/// the front.
///
/// Auth: <see cref="MatloobPolicies.Admin"/> — matloob_admin role plus the
/// admin audience. The spec §10 also wants an <c>establishments.review</c>
/// permission; that permission gate is intentionally not enforced here yet
/// because permissions don't exist as a discrete concept in v1.
/// </summary>
public sealed class GetPendingReviewEndpoint
    : Endpoint<GetPendingReviewRequest, GetPendingReviewResponse>
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly AppDbContext _db;

    public GetPendingReviewEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/establishments/pending-review");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<GetPendingReviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Admin.Establishments"));
        Summary(s =>
        {
            s.Summary = "List establishments awaiting admin review.";
            s.Description = "Paged, oldest-first by SubmittedAt. Admin-only.";
        });
    }

    public override async Task HandleAsync(GetPendingReviewRequest req, CancellationToken ct)
    {
        var page = req.Page < 1 ? 1 : req.Page;
        var pageSize = req.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => req.PageSize,
        };

        var query = _db.Establishments
            .AsNoTracking()
            .Where(e => e.Status == EstablishmentStatus.PendingReview);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.SubmittedAt)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new PendingReviewItem(
                e.Id,
                e.Name,
                e.CommercialRegistrationNumber,
                e.City,
                e.SubmittedAt!.Value,
                e.CreatedByUserId))
            .ToListAsync(ct);

        await Send.OkAsync(new GetPendingReviewResponse(
            Page: page,
            PageSize: pageSize,
            Total: total,
            Items: items), ct);
    }
}

/// <summary>Query DTO. Bound from query-string by FastEndpoints.</summary>
public sealed class GetPendingReviewRequest
{
    [BindFrom("page")]
    public int Page { get; init; } = 1;

    [BindFrom("pageSize")]
    public int PageSize { get; init; } = 20;
}

public sealed record GetPendingReviewResponse(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<PendingReviewItem> Items);

public sealed record PendingReviewItem(
    Guid Id,
    string Name,
    string CommercialRegistrationNumber,
    string City,
    DateTimeOffset SubmittedAt,
    string CreatedByUserId);
