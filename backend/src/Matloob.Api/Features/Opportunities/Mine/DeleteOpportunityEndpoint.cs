using FastEndpoints;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Mine;

/// <summary>
/// <c>DELETE /api/establishments/me/opportunities/{id}</c> + canonical
/// alias — soft-delete an opportunity. The SoftDeleteInterceptor flips
/// <c>is_deleted = true</c> instead of a hard delete; the row stays in
/// the database for audit + history.
/// </summary>
public sealed class DeleteOpportunityEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly TimeProvider _clock;

    public DeleteOpportunityEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _outbox = outbox;
        _clock = clock;
    }

    public override void Configure()
    {
        Delete(
            "/api/establishments/me/opportunities/{id}",
            "/api/v1/establishments/{establishmentId}/opportunities/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Soft-delete an owner-side opportunity.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(
            _db, HttpContext, _currentUser.UserId, ct);
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

        _db.Opportunities.Remove(opportunity);

        var now = _clock.GetUtcNow();
        _outbox.Enqueue(
            OpportunityEventTypes.Deleted,
            aggregateType: nameof(Opportunity),
            aggregateId: opportunity.Id,
            payload: new
            {
                id = opportunity.Id,
                deletedAt = now,
                deletedByUserId = _currentUser.UserId,
            });
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
