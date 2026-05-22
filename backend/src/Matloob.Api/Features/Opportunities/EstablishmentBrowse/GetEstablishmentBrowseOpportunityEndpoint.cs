using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.EstablishmentBrowse;

/// <summary>
/// <c>GET /api/establishments/opportunities/{id}</c> (Laravel-compat)
/// and <c>GET /api/v1/establishments/{establishmentId}/browse/opportunities/{id}</c>
/// (canonical) — detail page for an opportunity an establishment is
/// considering applying to.
///
/// <para>
/// Returns 404 for:
/// </para>
/// <list type="bullet">
///   <item>Opportunities not in a browsable status (Upcoming/Active).</item>
///   <item>Opportunities in <c>for_vacancy = true</c> categories
///     (those belong on the worker-side route).</item>
///   <item>Opportunities created by the resolved establishment itself
///     (cannot apply to your own).</item>
/// </list>
/// </summary>
public sealed class GetEstablishmentBrowseOpportunityEndpoint
    : EndpointWithoutRequest<OpportunityResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetEstablishmentBrowseOpportunityEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/opportunities/{id}",
            "/api/v1/establishments/{establishmentId}/browse/opportunities/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OpportunityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Detail of a browsable opportunity for the applying establishment.";
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
        if (opportunity is null
            || !OpportunityReadQueries.BrowsableStatuses.Contains(opportunity.Status)
            || opportunity.IssuerEstablishmentId == establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var category = await _db.OpportunityCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == opportunity.OpportunityCategoryId, ct);
        if (category is null || category.ForVacancy)
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
