using FastEndpoints;
using Matloob.Api.Features.Applications.Common;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Applications.EstablishmentReads;

/// <summary>
/// <c>GET /api/establishments/opportunities/applications</c>
/// (Laravel-compat) and
/// <c>GET /api/v1/establishments/{establishmentId}/browse/applications</c>
/// (canonical) — list applications SUBMITTED BY the resolved
/// establishment (i.e. the establishment applied to OTHERS'
/// opportunities). Mirrors the legacy
/// <c>Establishments\Opportunities\Applications\IndexOpportunityApplicationController</c>.
/// </summary>
public sealed class ListEstablishmentApplicationsEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OpportunityApplicationResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListEstablishmentApplicationsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/opportunities/applications",
            "/api/v1/establishments/{establishmentId}/browse/applications");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OpportunityApplicationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Applications"));
        Summary(s =>
        {
            s.Summary = "Applications submitted by the resolved establishment.";
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

        var applications = await _db.OpportunityApplications
            .AsNoTracking()
            .Where(a => a.ApplicantEstablishmentId == establishmentId.Value)
            .OrderByDescending(a => a.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var responses = new List<OpportunityApplicationResponse>(applications.Count);
        foreach (var application in applications)
        {
            var opportunity = await LoadOpportunityAsync(application, establishmentId, ct);
            var mapped = await ApplicationReadMapper.MapAsync(_db, application, opportunity, ct);
            responses.Add(mapped);
        }

        await Send.OkAsync(responses, ct);
    }

    private async Task<OpportunityResponse?> LoadOpportunityAsync(
        Matloob.Domain.Applications.OpportunityApplication application,
        Guid? applierEstablishmentId,
        CancellationToken ct)
    {
        var opp = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == application.OpportunityId, ct);
        if (opp is null) return null;

        var bundle = await OpportunityReadQueries.LoadSidecarAsync(
            _db, opp,
            subClaim: null,
            establishmentApplicantId: applierEstablishmentId,
            ct);
        return OpportunityReadMapper.Map(
            opp,
            bundle.Category,
            bundle.Issuer,
            bundle.Nationality,
            bundle.SuccessCriteria,
            bundle.Uploads,
            bundle.ApplicantsCount,
            bundle.IsApplied);
    }
}
