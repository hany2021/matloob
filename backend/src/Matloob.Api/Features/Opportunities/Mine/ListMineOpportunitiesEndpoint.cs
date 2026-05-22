using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Mine;

/// <summary>
/// <c>GET /api/establishments/me/opportunities</c> (Laravel-compat) and
/// <c>GET /api/v1/establishments/{establishmentId}/opportunities</c>
/// (canonical) — list opportunities ISSUED BY the resolved
/// establishment. Owner view (any active member can list).
///
/// <para>
/// Returns all statuses (Drafted, Upcoming, Active, Ended, Finished) —
/// unlike the browse endpoints, the owner sees everything they have
/// published. Soft-deleted rows hidden globally.
/// </para>
///
/// <para>
/// Laravel returned a <c>GroupedOpportunityResource::collection</c>
/// grouped by status. Q-OAO-GROUPED-RESPONSE (preparation plan default
/// d): emit a flat list for now; clients can group client-side from the
/// <c>status</c> field. Documented in the readiness doc.
/// </para>
/// </summary>
public sealed class ListMineOpportunitiesEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OpportunityResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListMineOpportunitiesEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/opportunities",
            "/api/v1/establishments/{establishmentId}/opportunities");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OpportunityResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "List opportunities issued by the resolved establishment.";
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

        var statusFilter = HttpContext.Request.Query["status"].FirstOrDefault();
        var nameFilter = HttpContext.Request.Query["name"].FirstOrDefault();
        var categoryFilter = ParseGuid(HttpContext.Request.Query["category"].FirstOrDefault());

        var query = _db.Opportunities
            .AsNoTracking()
            .Where(o => o.IssuerEstablishmentId == establishmentId.Value);

        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<Matloob.Domain.Opportunities.OpportunityStatus>(statusFilter, ignoreCase: true, out var parsed))
        {
            query = query.Where(o => o.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var n = nameFilter.Trim().ToLowerInvariant();
            query = query.Where(o => o.Name.ToLower().Contains(n));
        }
        if (categoryFilter is { } cat)
        {
            query = query.Where(o => o.OpportunityCategoryId == cat);
        }

        var opportunities = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(500)
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
