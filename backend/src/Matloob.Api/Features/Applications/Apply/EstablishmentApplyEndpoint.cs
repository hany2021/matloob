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
/// <c>POST /api/establishments/opportunities/{id}/apply</c> +
/// <c>POST /api/v1/establishments/{establishmentId}/browse/opportunities/{id}/apply</c>
/// — an establishment applies to a non-vacancy opportunity.
///
/// <para>
/// Rules (matching Laravel
/// <c>Establishments\Opportunities\Applications\StoreOpportunityApplicationController</c>
/// minus the establishment <c>profile_completed</c> gate):
/// </para>
/// <list type="bullet">
///   <item>Opportunity must exist and be in a browsable status.</item>
///   <item>Category must be <c>for_vacancy = false</c>.</item>
///   <item>Cannot apply to one's own opportunity.</item>
///   <item>Cannot apply twice.</item>
///   <item>Opportunity not full.</item>
///   <item>Suspended establishments cannot apply (423).</item>
/// </list>
/// </summary>
public sealed class EstablishmentApplyEndpoint
    : EndpointWithoutRequest<EstablishmentApplyResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;

    public EstablishmentApplyEndpoint(
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
            "/api/establishments/opportunities/{id}/apply",
            "/api/v1/establishments/{establishmentId}/browse/opportunities/{id}/apply");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EstablishmentApplyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Applications"));
        Summary(s =>
        {
            s.Summary = "Establishment applies to a non-vacancy opportunity.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(
            _db, HttpContext, sub, ct);
        if (establishmentId is null) return;

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

        // Cannot apply to own opportunity.
        if (opportunity.IssuerEstablishmentId == establishmentId.Value)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.ApplicationSelfNotAllowed,
                "Establishment cannot apply to its own opportunity.",
                ct);
            return;
        }

        // Category must NOT be for_vacancy (worker-side opportunities not
        // openable to organizations).
        var category = await _db.OpportunityCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == opportunity.OpportunityCategoryId, ct);
        if (category is null || category.ForVacancy)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.ApplicationCategoryMismatch,
                "Establishments can only apply to non-vacancy opportunities.",
                ct);
            return;
        }

        var alreadyApplied = await _db.OpportunityApplications
            .AsNoTracking()
            .AnyAsync(a =>
                a.OpportunityId == opportunity.Id &&
                a.ApplicantEstablishmentId == establishmentId.Value, ct);
        if (alreadyApplied)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status409Conflict,
                OpportunityErrorCodes.ApplicationAlreadyExists,
                "Establishment has already applied to this opportunity.",
                ct);
            return;
        }

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

        var application = OpportunityApplication.ForEstablishment(
            id: Guid.NewGuid(),
            opportunityId: opportunity.Id,
            applicantEstablishmentId: establishmentId.Value,
            appliedByUserId: sub);
        _db.OpportunityApplications.Add(application);

        _outbox.Enqueue(
            ApplicationEventTypes.Submitted,
            aggregateType: nameof(OpportunityApplication),
            aggregateId: application.Id,
            payload: new
            {
                id = application.Id,
                opportunityId = opportunity.Id,
                applicantEstablishmentId = establishmentId.Value,
                appliedByUserId = sub,
            });
        _outbox.Flush();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status409Conflict,
                OpportunityErrorCodes.ApplicationAlreadyExists,
                "Establishment has already applied to this opportunity.",
                ct);
            return;
        }

        var response = new EstablishmentApplyResponse
        {
            Id = application.Id,
            OpportunityId = opportunity.Id,
            Message = "application_submitted_successfully",
        };
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/browse/applications/{application.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}

public sealed class EstablishmentApplyResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("opportunity_id")]
    public Guid OpportunityId { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
