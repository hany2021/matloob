using FastEndpoints;
using Matloob.Api.Features.Applications.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Applications.UserReads;

/// <summary>
/// <c>GET /api/users/opportunities/applications/{applicantId}</c>
/// (Laravel-compat) and
/// <c>GET /api/v1/users/opportunities/applications/{applicantId}</c>
/// (canonical) — detail of the worker's own application.
///
/// <para>
/// 404 (not 403) when the application exists but belongs to another
/// user. The Laravel route used the same enumeration-leak policy.
/// </para>
/// </summary>
public sealed class GetUserApplicationEndpoint
    : EndpointWithoutRequest<OpportunityApplicationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetUserApplicationEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/users/opportunities/applications/{applicantId}",
            "/api/v1/users/opportunities/applications/{applicantId}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OpportunityApplicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Applications"));
        Summary(s =>
        {
            s.Summary = "Detail of the worker's own opportunity application.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var applicationId = Route<Guid>("applicantId");

        var application = await _db.OpportunityApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (application is null || application.ApplicantUserId != sub)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var opp = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == application.OpportunityId, ct);
        OpportunityResponse? opportunity = null;
        if (opp is not null)
        {
            var bundle = await OpportunityReadQueries.LoadSidecarAsync(
                _db, opp,
                subClaim: sub,
                establishmentApplicantId: null,
                ct);
            opportunity = OpportunityReadMapper.Map(
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

        var response = await ApplicationReadMapper.MapAsync(_db, application, opportunity, ct);
        await Send.OkAsync(response, ct);
    }
}
