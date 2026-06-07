using FastEndpoints;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.UserBrowse;

/// <summary>
/// <c>GET /api/users/opportunities/{id}</c> (Laravel-compat) and
/// <c>GET /api/v1/users/opportunities/{id}</c> (canonical) — full
/// opportunity detail for an individual worker.
///
/// <para>
/// Returns 404 for opportunities not in a browsable status
/// (Upcoming/Active) so unpublished / ended / finished rows can't be
/// enumerated by id. <c>is_applied</c> reflects whether the current
/// principal has an active application on the opportunity.
/// </para>
/// </summary>
public sealed class GetUserOpportunityEndpoint
    : EndpointWithoutRequest<OpportunityResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetUserOpportunityEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/users/opportunities/{id}", "/api/v1/users/opportunities/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OpportunityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Detail of one browsable opportunity for the individual user.";
            s.Description =
                "Returns 404 when the opportunity is not in a browsable " +
                "status (Upcoming/Active) so unpublished / ended rows " +
                "are not enumerable.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var sub = _currentUser.UserId;

        var opportunity = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null
            || !OpportunityReadQueries.BrowsableStatuses.Contains(opportunity.Status))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // For show, also require the category to be a worker (for_vacancy)
        // category — Laravel checked this implicitly via the same scope on
        // index. A non-vacancy opportunity on /users/* by id is treated as
        // not-found rather than 403, matching the enumeration-leak policy.
        var category = await _db.OpportunityCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == opportunity.OpportunityCategoryId, ct);
        if (category is null || !category.ForVacancy)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var bundle = await OpportunityReadQueries.LoadSidecarAsync(
            _db, opportunity,
            subClaim: sub,
            establishmentApplicantId: null,
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
            bundle.Event,
            contractsCount: bundle.ContractsCount);

        await Send.OkAsync(response, ct);
    }
}
