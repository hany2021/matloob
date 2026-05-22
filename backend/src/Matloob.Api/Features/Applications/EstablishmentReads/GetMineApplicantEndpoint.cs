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
/// <c>GET /api/establishments/me/applicants/{applicantId}</c>
/// (Laravel-compat) and
/// <c>GET /api/v1/establishments/{establishmentId}/applicants/{applicantId}</c>
/// (canonical) — detail of one applicant on an opportunity owned by
/// the resolved establishment. Mirrors the legacy
/// <c>Me\Opportunities\Applications\ShowApplicationController</c>.
///
/// <para>
/// 404 when the application exists but its opportunity does not belong
/// to the resolved establishment.
/// </para>
/// </summary>
public sealed class GetMineApplicantEndpoint
    : EndpointWithoutRequest<OpportunityApplicationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetMineApplicantEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/applicants/{applicantId}",
            "/api/v1/establishments/{establishmentId}/applicants/{applicantId}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OpportunityApplicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Applications"));
        Summary(s =>
        {
            s.Summary = "Detail of one applicant on one of the establishment's own opportunities.";
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

        var applicationId = Route<Guid>("applicantId");
        var application = await _db.OpportunityApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (application is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var opp = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == application.OpportunityId, ct);
        if (opp is null || opp.IssuerEstablishmentId != establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var bundle = await OpportunityReadQueries.LoadSidecarAsync(
            _db, opp,
            subClaim: null,
            establishmentApplicantId: establishmentId,
            ct);
        var opportunityResponse = OpportunityReadMapper.Map(
            opp,
            bundle.Category,
            bundle.Issuer,
            bundle.Nationality,
            bundle.SuccessCriteria,
            bundle.Uploads,
            bundle.ApplicantsCount,
            bundle.IsApplied);

        var response = await ApplicationReadMapper.MapAsync(_db, application, opportunityResponse, ct);
        await Send.OkAsync(response, ct);
    }
}
