using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Admins;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Admins.Individuals;

/// <summary>
/// <c>GET /api/v1/admin/individuals</c> — admin list of individual user
/// accounts. The .NET port of the legacy Filament <c>UserResource</c> list
/// (view-only). Reads the TPH <c>users</c> table filtered to the base
/// <c>User</c> rows (discriminator <c>user_type = 'User'</c>) — back-office
/// admins (the derived <see cref="Admin"/>) are excluded, they have their own
/// screen.
///
/// Search matches name / email / id-number / phone (case-insensitive). City
/// and nationality are resolved to their display names via the reference
/// lookups. Auth: <see cref="MatloobPolicies.Admin"/>.
/// </summary>
public sealed class ListIndividualsEndpoint
    : Endpoint<ListIndividualsRequest, ListIndividualsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly AppDbContext _db;

    public ListIndividualsEndpoint(AppDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/v1/admin/individuals");
        Policies(MatloobPolicies.Admin);
        Description(b => b
            .Produces<ListIndividualsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Admin.Individuals"));
        Summary(s => s.Summary = "List / search individual user accounts.");
    }

    public override async Task HandleAsync(ListIndividualsRequest req, CancellationToken ct)
    {
        var page = req.Page < 1 ? 1 : req.Page;
        var pageSize = req.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => req.PageSize,
        };

        // Base User rows only (exclude the TPH Admin subtype).
        var query = _db.Users.AsNoTracking().Where(u => !(u is Admin));

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim().ToLower();
            query = query.Where(u =>
                (u.Name != null && u.Name.ToLower().Contains(s))
                || (u.Email != null && u.Email.ToLower().Contains(s))
                || (u.IdNumber != null && u.IdNumber.ToLower().Contains(s))
                || (u.Phone != null && u.Phone.ToLower().Contains(s)));
        }

        var total = await query.CountAsync(ct);

        var raw = await query
            .OrderBy(u => u.Name)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.IdNumber,
                u.Name,
                u.Email,
                u.Phone,
                u.Gender,
                u.Age,
                u.YearsOfExperience,
                City = _db.Cities.Where(c => c.Id == u.CityId).Select(c => c.Name).FirstOrDefault(),
                Nationality = _db.Nationalities.Where(n => n.Id == u.NationalityId).Select(n => n.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = raw
            .Select(r => new IndividualListItem(
                r.Id,
                r.IdNumber,
                r.Name ?? string.Empty,
                r.Email ?? string.Empty,
                r.Phone,
                GenderLabel(r.Gender),
                r.Age,
                r.City,
                r.Nationality,
                r.YearsOfExperience))
            .ToList();

        await Send.OkAsync(new ListIndividualsResponse(page, pageSize, total, items), ct);
    }

    private static string? GenderLabel(Gender? gender) => gender switch
    {
        Gender.Male => "ذكر",
        Gender.Female => "أنثى",
        _ => null,
    };
}

public sealed class ListIndividualsRequest
{
    [BindFrom("page")]
    public int Page { get; init; } = 1;

    [BindFrom("pageSize")]
    public int PageSize { get; init; } = 50;

    [BindFrom("search")]
    public string? Search { get; init; }
}

public sealed record IndividualListItem(
    Guid Id,
    string? IdNumber,
    string Name,
    string Email,
    string? Phone,
    string? Gender,
    int? Age,
    string? City,
    string? Nationality,
    int YearsOfExperience);

public sealed record ListIndividualsResponse(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<IndividualListItem> Items);
