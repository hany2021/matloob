using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.EstablishmentBrowse;

/// <summary>
/// <c>GET /api/establishments/opportunities</c> (Laravel-compat) and
/// <c>GET /api/v1/establishments/{establishmentId}/browse/opportunities</c>
/// (canonical) — browse opportunities visible to ANOTHER establishment
/// (i.e. the calling establishment looking for gigs to apply to).
///
/// <para>
/// Filters mirror the legacy
/// <c>Establishments\Opportunities\OpportunityController::index</c>:
/// </para>
/// <list type="bullet">
///   <item>Browsable statuses (Upcoming, Active).</item>
///   <item>Categories with <c>for_vacancy = false</c> (the
///     establishments flow, opposite of the worker-side).</item>
///   <item>Self-created opportunities are excluded (an establishment
///     cannot apply to its own opportunity).</item>
/// </list>
///
/// <para>
/// Authorisation: active member of the resolved establishment OR
/// matloob_admin. Legacy route resolves establishment id via
/// <see cref="EstablishmentContextResolver"/>; canonical route reads
/// it from the path.
/// </para>
/// </summary>
public sealed class ListEstablishmentBrowseOpportunitiesEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OpportunityResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListEstablishmentBrowseOpportunitiesEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/opportunities",
            "/api/v1/establishments/{establishmentId}/browse/opportunities");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OpportunityResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Browse opportunities visible to an applying establishment.";
            s.Description =
                "Lists Upcoming + Active opportunities in non-vacancy " +
                "categories, excluding ones created by the resolved " +
                "establishment.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper
            .ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return; // helper already wrote the problem response.

        // Membership check: active member OR admin can browse.
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

        var nameFilter = HttpContext.Request.Query["name"].FirstOrDefault();
        var categoryFilter = ParseGuid(HttpContext.Request.Query["category"].FirstOrDefault());

        var nonVacancyCategoryIds = await _db.OpportunityCategories
            .AsNoTracking()
            .Where(c => !c.ForVacancy)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var baseQuery = _db.Opportunities
            .AsNoTracking()
            .Where(o => OpportunityReadQueries.BrowsableStatuses.Contains(o.Status))
            .Where(o => nonVacancyCategoryIds.Contains(o.OpportunityCategoryId))
            .Where(o => o.IssuerEstablishmentId != establishmentId.Value);

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var n = nameFilter.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(o => o.Name.ToLower().Contains(n));
        }
        if (categoryFilter is { } cat)
        {
            baseQuery = baseQuery.Where(o => o.OpportunityCategoryId == cat);
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
                subClaim: null,
                establishmentApplicantId: establishmentId,
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
