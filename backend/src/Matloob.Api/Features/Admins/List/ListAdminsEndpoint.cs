using FastEndpoints;
using Matloob.Api.Features.Admins.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Admins.List;

/// <summary>
/// <c>GET /api/v1/admin/admins</c> — the admin list/search screen. Reads the
/// local <c>admins</c> table, ordered by name. Mirrors the legacy
/// <c>ListAdmins</c> search over name / email.
///
/// Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class ListAdminsEndpoint
    : Endpoint<ListAdminsRequest, ListAdminsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly AppDbContext _db;

    public ListAdminsEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/admins");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ListAdminsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Admin.Admins"));
        Summary(s => s.Summary = "List / search back-office admin users.");
    }

    public override async Task HandleAsync(ListAdminsRequest req, CancellationToken ct)
    {
        var page = req.Page < 1 ? 1 : req.Page;
        var pageSize = req.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => req.PageSize,
        };

        var query = _db.Admins.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim().ToLower();
            query = query.Where(a =>
                (a.Name != null && a.Name.ToLower().Contains(s))
                || (a.Email != null && a.Email.ToLower().Contains(s)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(a => a.Name)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminListItem(a.Id, a.Name ?? string.Empty, a.Email ?? string.Empty, a.IsActive, a.IdentityId))
            .ToListAsync(ct);

        await Send.OkAsync(new ListAdminsResponse(page, pageSize, total, items), ct);
    }
}

public sealed class ListAdminsRequest
{
    [BindFrom("page")]
    public int Page { get; init; } = 1;

    [BindFrom("pageSize")]
    public int PageSize { get; init; } = 50;

    [BindFrom("search")]
    public string? Search { get; init; }
}

public sealed record ListAdminsResponse(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<AdminListItem> Items);
