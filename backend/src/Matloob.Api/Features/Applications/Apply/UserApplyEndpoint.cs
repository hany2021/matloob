using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Applications;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Applications.Apply;

/// <summary>
/// <c>POST /api/users/opportunities/{id}/apply</c> +
/// <c>POST /api/v1/users/opportunities/{id}/apply</c> — individual
/// worker applies to a vacancy opportunity.
///
/// <para>
/// Rules (matching Laravel <c>ApplyForOpportunityController</c>, minus
/// the <c>profile_completed</c> gate per Q-OPP-APPLY-PROFILE-GATE
/// default (b)):
/// </para>
/// <list type="bullet">
///   <item>Opportunity must exist and be in a browsable status
///     (Upcoming/Active).</item>
///   <item>Category must be <c>for_vacancy = true</c>.</item>
///   <item>Cannot apply twice (409 application_already_exists).</item>
///   <item>Opportunity must not have reached required_personnel.</item>
/// </list>
/// </summary>
public sealed class UserApplyEndpoint
    : EndpointWithoutRequest<UserApplyResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;

    public UserApplyEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        IOutboxWriter outbox)
    {
        _db = db;
        _currentUser = currentUser;
        _outbox = outbox;
    }

    public override void Configure()
    {
        Post(
            "/api/users/opportunities/{id}/apply",
            "/api/v1/users/opportunities/{id}/apply");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<UserApplyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Applications"));
        Summary(s =>
        {
            s.Summary = "Worker applies to a vacancy opportunity.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var oppId = Route<Guid>("id");

        var opportunity = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == oppId, ct);
        if (opportunity is null
            || !OpportunityReadQueries.BrowsableStatuses.Contains(opportunity.Status))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var category = await _db.OpportunityCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == opportunity.OpportunityCategoryId, ct);
        if (category is null || !category.ForVacancy)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.ApplicationCategoryMismatch,
                "Workers can only apply to vacancy-category opportunities.",
                ct);
            return;
        }

        // Already applied?
        var alreadyApplied = await _db.OpportunityApplications
            .AsNoTracking()
            .AnyAsync(a =>
                a.OpportunityId == opportunity.Id &&
                a.ApplicantUserId == sub, ct);
        if (alreadyApplied)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status409Conflict,
                OpportunityErrorCodes.ApplicationAlreadyExists,
                "You have already applied to this opportunity.",
                ct);
            return;
        }

        // Required personnel reached?
        var existing = await _db.OpportunityApplications
            .AsNoTracking()
            .CountAsync(a => a.OpportunityId == opportunity.Id, ct);
        if (existing >= opportunity.RequiredPersonnel)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.ApplicationNotApplicable,
                "Opportunity has reached its required personnel count.",
                ct);
            return;
        }

        var application = OpportunityApplication.ForUser(
            id: Guid.NewGuid(),
            opportunityId: opportunity.Id,
            applicantUserId: sub);
        _db.OpportunityApplications.Add(application);

        _outbox.Enqueue(
            ApplicationEventTypes.Submitted,
            aggregateType: nameof(OpportunityApplication),
            aggregateId: application.Id,
            payload: new
            {
                id = application.Id,
                opportunityId = opportunity.Id,
                applicantUserId = sub,
            });
        _outbox.Flush();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race with the partial unique index — translate to 409.
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status409Conflict,
                OpportunityErrorCodes.ApplicationAlreadyExists,
                "You have already applied to this opportunity.",
                ct);
            return;
        }

        var response = new UserApplyResponse
        {
            Id = application.Id,
            OpportunityId = opportunity.Id,
            Message = "application_submitted_successfully",
        };
        HttpContext.Response.Headers.Location =
            $"/api/v1/users/opportunities/applications/{application.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}

public sealed class UserApplyResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("opportunity_id")]
    public Guid OpportunityId { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
