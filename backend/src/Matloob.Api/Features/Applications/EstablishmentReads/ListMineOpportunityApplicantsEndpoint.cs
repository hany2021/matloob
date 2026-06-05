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
/// <c>GET /api/establishments/me/opportunities/{id}/applications</c>
/// (Laravel-compat) and
/// <c>GET /api/v1/establishments/{establishmentId}/opportunities/{id}/applications</c>
/// (canonical) — list applicants ON an opportunity owned by the
/// resolved establishment.
///
/// <para>
/// Mirrors the legacy
/// <c>MyIndexOpportunityApplicationController</c> filter: applicants
/// who don't yet have an offer
/// (<c>whereDoesntHave('offer')</c>). Once an offer exists for the
/// application, the applicant disappears from this list (the owner sees
/// them in the offers slice instead).
/// </para>
/// </summary>
public sealed class ListMineOpportunityApplicantsEndpoint
    : EndpointWithoutRequest<IReadOnlyList<OpportunityApplicationResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListMineOpportunityApplicantsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get(
            "/api/establishments/me/opportunities/{id}/applications",
            "/api/v1/establishments/{establishmentId}/opportunities/{id}/applications");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<OpportunityApplicationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Applications"));
        Summary(s =>
        {
            s.Summary = "List applicants on one of the establishment's own opportunities.";
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
            || opportunity.IssuerEstablishmentId != establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Applicants who have NO offer yet (Laravel whereDoesntHave('offer')).
        var withOffer = _db.Offers
            .AsNoTracking()
            .Select(o => o.ApplicationId);

        var applications = await _db.OpportunityApplications
            .AsNoTracking()
            .Where(a => a.OpportunityId == oppId)
            .Where(a => !withOffer.Contains(a.Id))
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        OpportunityResponse? opportunityResponse = null;
        var oppBundle = await OpportunityReadQueries.LoadSidecarAsync(
            _db, opportunity,
            subClaim: null,
            establishmentApplicantId: establishmentId,
            ct);
        opportunityResponse = OpportunityReadMapper.Map(
            opportunity,
            oppBundle.Category,
            oppBundle.Issuer,
            oppBundle.Nationality,
            oppBundle.SuccessCriteria,
            oppBundle.Uploads,
            oppBundle.ApplicantsCount,
            oppBundle.IsApplied,
            oppBundle.Event);

        var responses = new List<OpportunityApplicationResponse>(applications.Count);
        foreach (var application in applications)
        {
            var mapped = await ApplicationReadMapper.MapAsync(_db, application, opportunityResponse, ct);
            responses.Add(mapped);
        }

        await Send.OkAsync(responses, ct);
    }
}
