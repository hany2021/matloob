using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Mine;

/// <summary>
/// <c>GET /api/establishments/me/opportunities/{id}</c> (Laravel-compat)
/// and
/// <c>GET /api/v1/establishments/{establishmentId}/opportunities/{id}</c>
/// (canonical) — owner-side opportunity detail. Visible to any active
/// member of the issuing establishment plus admins.
///
/// <para>
/// 404 (not 403) when the caller has no membership on the resolved
/// establishment, or when the opportunity exists but belongs to a
/// different establishment. Status is not filtered — owners see Drafted /
/// Ended / Finished records the public browse endpoints would hide.
/// </para>
/// </summary>
public sealed class GetMineOpportunityEndpoint
    : EndpointWithoutRequest<OpportunityResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetMineOpportunityEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/opportunities/{id}",
            "/api/v1/establishments/{establishmentId}/opportunities/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OpportunityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Owner-side opportunity detail.";
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

        var oppId = Route<Guid>("id");
        var opportunity = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == oppId, ct);

        // Owner-only ownership check: the opportunity must belong to
        // the resolved establishment. Even cross-establishment ids on
        // the same caller are masked as 404.
        if (opportunity is null
            || opportunity.IssuerEstablishmentId != establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var bundle = await OpportunityReadQueries.LoadSidecarAsync(
            _db, opportunity,
            subClaim: null,
            establishmentApplicantId: establishmentId,
            ct);

        var response = OpportunityReadMapper.Map(
            opportunity,
            bundle.Category,
            bundle.Issuer,
            bundle.Nationality,
            bundle.SuccessCriteria,
            bundle.Uploads,
            bundle.ApplicantsCount,
            bundle.IsApplied);
        await Send.OkAsync(response, ct);
    }
}
