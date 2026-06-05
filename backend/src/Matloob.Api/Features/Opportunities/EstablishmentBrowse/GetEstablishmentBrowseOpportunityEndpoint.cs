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
/// (canonical) — opportunity detail.
///
/// <para>
/// Mirrors the legacy <c>Establishments\Opportunities\OpportunityController::show</c>,
/// which is a plain route-model-bind: it returns ANY opportunity by id
/// regardless of status, category, or ownership. The frontend uses this
/// single endpoint both for the applying-establishment browse detail AND
/// for the organizer viewing their OWN opportunity
/// (<c>dashboard/opportunities/[slug]</c>), so the only 404 is
/// "no such opportunity". (The list/<c>index</c> endpoint is where the
/// browsable/own-exclusion filters live.)
/// </para>
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

        // Legacy `show` is a plain route-model-bind: any opportunity by id,
        // no status/category/ownership filter. 404 only when missing.
        var oppId = Route<Guid>("id");
        var opportunity = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == oppId, ct);
        if (opportunity is null)
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
            bundle.IsApplied,
            bundle.Event);
        await Send.OkAsync(response, ct);
    }
}
