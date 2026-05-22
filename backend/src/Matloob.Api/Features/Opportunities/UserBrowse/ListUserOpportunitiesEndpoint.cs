using FastEndpoints;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.UserBrowse;

/// <summary>
/// <c>GET /api/users/opportunities</c> (Laravel-compat) and
/// <c>GET /api/v1/users/opportunities</c> (canonical) — browse
/// opportunities visible to individual workers.
///
/// <para>
/// Matches the legacy
/// <c>Users\Opportunities\OpportunityController::index</c> filter set
/// minus Ajeer-specific scopes:
/// </para>
///
/// <list type="bullet">
///   <item>Only categories with <c>for_vacancy = true</c> (the
///     individuals flow).</item>
///   <item>Only readable statuses (<c>Upcoming</c>, <c>Active</c>).</item>
///   <item>Soft-deleted rows excluded by the global query filter.</item>
/// </list>
///
/// <para>
/// Query-parameter filters preserved verbatim from Laravel:
/// <c>?name=</c>, <c>?category={uuid}</c>, <c>?city={uuid}</c>. The
/// Laravel <c>?season=</c> + <c>?recommended=</c> filters are NOT
/// ported until the Events / personalisation slices land.
/// </para>
/// </summary>
public sealed class ListUserOpportunitiesEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OpportunityResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListUserOpportunitiesEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/users/opportunities", "/api/v1/users/opportunities");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OpportunityResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Browse opportunities for the individual user.";
            s.Description =
                "Lists Upcoming + Active opportunities in for_vacancy " +
                "categories. Soft-deleted rows hidden globally. Filters: " +
                "?name, ?category, ?city.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;

        var nameFilter = HttpContext.Request.Query["name"].FirstOrDefault();
        var categoryFilter = ParseGuid(HttpContext.Request.Query["category"].FirstOrDefault());
        var cityFilter = ParseGuid(HttpContext.Request.Query["city"].FirstOrDefault());

        // Step 1: opportunity ids visible to the worker flow.
        var vacancyCategoryIds = await _db.OpportunityCategories
            .AsNoTracking()
            .Where(c => c.ForVacancy)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var baseQuery = _db.Opportunities
            .AsNoTracking()
            .Where(o => OpportunityReadQueries.BrowsableStatuses.Contains(o.Status))
            .Where(o => vacancyCategoryIds.Contains(o.OpportunityCategoryId));

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            // Case-insensitive contains. Postgres + InMemory both translate
            // `ToLower().Contains` safely; we avoid EF.Functions.ILike
            // because the InMemory provider used by tests can't translate
            // it.
            var n = nameFilter.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(o => o.Name.ToLower().Contains(n));
        }
        if (categoryFilter is { } cat)
        {
            baseQuery = baseQuery.Where(o => o.OpportunityCategoryId == cat);
        }
        if (cityFilter is { } city)
        {
            baseQuery = baseQuery.Where(o => o.CityId == city);
        }

        var opportunities = await baseQuery
            .OrderByDescending(o => o.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var responses = new List<OpportunityResponse>(opportunities.Count);
        foreach (var opportunity in opportunities)
        {
            var bundle = await OpportunityReadQueries.LoadSidecarAsync(
                _db, opportunity,
                subClaim: sub,
                establishmentApplicantId: null,
                ct);
            responses.Add(OpportunityReadMapper.Map(
                opportunity,
                bundle.Category,
                bundle.Issuer,
                bundle.Nationality,
                bundle.SuccessCriteria,
                bundle.Uploads,
                bundle.ApplicantsCount,
                bundle.IsApplied));
        }

        await Send.OkAsync(responses, ct);
    }

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var g) ? g : null;
}
