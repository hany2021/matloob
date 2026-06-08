using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Mine;

/// <summary>
/// <c>PATCH /api/establishments/me/opportunities/{id}/end</c> +
/// canonical alias — manually end an opportunity. Allowed only from
/// <see cref="OpportunityStatus.Upcoming"/> or
/// <see cref="OpportunityStatus.Active"/>; other statuses return 422.
/// </summary>
public sealed class EndOpportunityEndpoint
    : EndpointWithoutRequest<OpportunityResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public EndOpportunityEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider clock,
        IOutboxWriter outbox)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _outbox = outbox;
    }

    public override void Configure()
    {
        Patch(
            "/api/establishments/me/opportunities/{id}/end",
            "/api/v1/establishments/{establishmentId}/opportunities/{id}/end");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OpportunityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Manually end an opportunity (Upcoming/Active → Ended).";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(
            _db, HttpContext, _currentUser.UserId, ct, Infrastructure.Auth.Permissions.Opportunities.Manage);
        if (establishmentId is null) return;

        var oppId = Route<Guid>("id");
        var opportunity = await _db.Opportunities
            .FirstOrDefaultAsync(o => o.Id == oppId, ct);
        if (opportunity is null
            || opportunity.IssuerEstablishmentId != establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var now = _clock.GetUtcNow();
        try
        {
            opportunity.End(_currentUser.UserId, now);
        }
        catch (InvalidOperationException ex)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.InvalidStatusTransition,
                ex.Message,
                ct);
            return;
        }

        _outbox.Enqueue(
            OpportunityEventTypes.Ended,
            aggregateType: nameof(Opportunity),
            aggregateId: opportunity.Id,
            payload: new
            {
                id = opportunity.Id,
                endedAt = now,
                endedByUserId = _currentUser.UserId,
            });
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);

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
