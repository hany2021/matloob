using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.EstablishmentBrowse;

/// <summary>
/// <c>GET /api/establishments/opportunities/categories</c>
/// (Laravel-compat) and
/// <c>GET /api/v1/establishments/{establishmentId}/browse/opportunity-categories</c>
/// (canonical) — top-level (parent_id IS NULL) opportunity categories
/// for the establishment-browse picker.
///
/// <para>
/// Excludes the "Other" catch-all (<c>is_other = true</c>) and
/// soft-deleted rows. Children are nested.
/// </para>
///
/// <para>
/// Authorisation: active member of the resolved establishment OR
/// matloob_admin. Legacy route resolves establishment id via
/// <see cref="EstablishmentContextResolver"/>.
/// </para>
/// </summary>
public sealed class ListOpportunityCategoriesEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OpportunityCategoryTreeDto>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListOpportunityCategoriesEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/opportunities/categories",
            "/api/v1/establishments/{establishmentId}/browse/opportunity-categories");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OpportunityCategoryTreeDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Top-level opportunity categories with children for the browse picker.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper
            .ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isMember = await MembershipChecks.IsActiveMemberAsync(
                _db, establishmentId.Value, sub, ct);
            if (!isMember)
            {
                await Send.NotFoundAsync(ct);
                return;
            }
        }

        var roots = await _db.OpportunityCategories
            .AsNoTracking()
            .Where(c => c.ParentId == null && !c.IsOther)
            .OrderBy(c => c.Title)
            .Select(c => new OpportunityCategoryTreeDto
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                Icon = c.Icon,
                ForVacancy = c.ForVacancy,
                IsOther = c.IsOther,
            })
            .ToListAsync(ct);

        // Hydrate children with one extra query — much simpler than a recursive
        // self-join in EF and keeps the response shape one level deep, which
        // matches Laravel's apiResource output.
        var rootIds = roots.Select(r => r.Id).ToList();
        var children = await _db.OpportunityCategories
            .AsNoTracking()
            .Where(c => c.ParentId != null && rootIds.Contains(c.ParentId!.Value))
            .OrderBy(c => c.Title)
            .Select(c => new
            {
                ParentId = c.ParentId!.Value,
                Item = new OpportunityCategoryTreeDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Description = c.Description,
                    Icon = c.Icon,
                    ForVacancy = c.ForVacancy,
                    IsOther = c.IsOther,
                },
            })
            .ToListAsync(ct);

        var byParent = children
            .GroupBy(x => x.ParentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OpportunityCategoryTreeDto>)g.Select(x => x.Item).ToList());

        foreach (var root in roots)
        {
            root.Children = byParent.GetValueOrDefault(root.Id, []);
        }

        await Send.OkAsync(roots, ct);
    }
}

public sealed class OpportunityCategoryTreeDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("for_vacancy")]
    public bool ForVacancy { get; init; }

    [JsonPropertyName("is_other")]
    public bool IsOther { get; init; }

    [JsonPropertyName("children")]
    public IReadOnlyList<OpportunityCategoryTreeDto> Children { get; set; } = [];
}
