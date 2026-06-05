using FastEndpoints;
using Matloob.Api.Features.Applications.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Applications.UserReads;

/// <summary>
/// <c>GET /api/users/opportunities/applications</c> (Laravel-compat)
/// and <c>GET /api/v1/users/opportunities/applications</c> (canonical)
/// — list the current worker's own applications.
/// </summary>
public sealed class ListUserApplicationsEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OpportunityApplicationResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListUserApplicationsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/users/opportunities/applications",
            "/api/v1/users/opportunities/applications");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OpportunityApplicationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Applications"));
        Summary(s =>
        {
            s.Summary = "List the worker's own opportunity applications.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;

        var applications = await _db.OpportunityApplications
            .AsNoTracking()
            .Where(a => a.ApplicantUserId == sub)
            .OrderByDescending(a => a.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var responses = new List<OpportunityApplicationResponse>(applications.Count);
        foreach (var application in applications)
        {
            var opportunity = await LoadNestedOpportunityAsync(application, sub, ct);
            var mapped = await ApplicationReadMapper.MapAsync(_db, application, opportunity, ct);
            responses.Add(mapped);
        }

        await Send.OkAsync(responses, ct);
    }

    private async Task<OpportunityResponse?> LoadNestedOpportunityAsync(
        Domain.Applications.OpportunityApplication application,
        string subClaim,
        CancellationToken ct)
    {
        var opp = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == application.OpportunityId, ct);
        if (opp is null) return null;

        var bundle = await OpportunityReadQueries.LoadSidecarAsync(
            _db, opp,
            subClaim: subClaim,
            establishmentApplicantId: null,
            ct);
        return OpportunityReadMapper.Map(
            opp,
            bundle.Category,
            bundle.Issuer,
            bundle.Nationality,
            bundle.SuccessCriteria,
            bundle.Uploads,
            bundle.ApplicantsCount,
            bundle.IsApplied,
            bundle.Event);
    }
}
