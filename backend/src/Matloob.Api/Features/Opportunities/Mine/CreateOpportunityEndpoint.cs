using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
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
/// <c>POST /api/establishments/me/opportunities</c> (Laravel-compat) and
/// <c>POST /api/v1/establishments/{establishmentId}/opportunities</c>
/// (canonical) — create a new opportunity issued by the resolved
/// establishment.
///
/// <para>
/// Single-opportunity body shape. The legacy Laravel endpoint accepted a
/// bulk array (<c>event_uuid + opportunities[]</c>) — see Q-OAO-BULK-CREATE
/// in the readiness audit. The single shape is sufficient for the public
/// frontend's per-row create flow; a future array wrapper can layer on
/// top without changing this endpoint.
/// </para>
///
/// <para>
/// Auto-status: new rows go to <see cref="OpportunityStatus.Upcoming"/>
/// when <c>start_date</c> is in the future, otherwise
/// <see cref="OpportunityStatus.Active"/> (Q-OPP-1 default).
/// </para>
/// </summary>
public sealed class CreateOpportunityEndpoint
    : Endpoint<CreateOpportunityRequest, OpportunityResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public CreateOpportunityEndpoint(
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
        Post(
            "/api/establishments/me/opportunities",
            "/api/v1/establishments/{establishmentId}/opportunities");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OpportunityResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Create an opportunity for the resolved establishment.";
        });
    }

    public override async Task HandleAsync(CreateOpportunityRequest req, CancellationToken ct)
    {
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(
            _db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        // Category existence check — also implicitly asserts non-soft-deleted.
        var category = await _db.OpportunityCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.OpportunityCategoryId, ct);
        if (category is null)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.CategoryNotFound,
                "Opportunity category does not exist.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        Opportunity opportunity;
        try
        {
            opportunity = Opportunity.Create(
                id: Guid.NewGuid(),
                issuerEstablishmentId: establishmentId.Value,
                eventId: req.EventId,
                opportunityCategoryId: req.OpportunityCategoryId,
                name: req.Name,
                description: req.Description,
                startDate: req.StartDate,
                endDate: req.EndDate,
                locationTitle: req.LocationTitle,
                latitude: req.Lat,
                longitude: req.Lon,
                requiredPersonnel: req.RequiredPersonnel,
                now: now,
                cityId: req.CityId,
                nationalityId: req.NationalityId,
                monthlySalary: req.MonthlySalary,
                yearsOfExperienceRequired: req.YearsOfExperienceRequired,
                workingHoursType: ParseWorkingHours(req.WorkingHoursType),
                workingHoursFrom: ParseTime(req.WorkingHoursFrom),
                workingHoursTo: ParseTime(req.WorkingHoursTo),
                fees: req.Fees,
                phoneContactInformation: req.PhoneContactInformation,
                emailContactInformation: req.EmailContactInformation,
                establishmentClassifications: CollapseFlags(req.EstablishmentClassification, ParseClassification),
                genders: CollapseFlags(req.Gender, ParseGender));
        }
        catch (ArgumentException ex)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                "invalid_opportunity_payload",
                ex.Message,
                ct);
            return;
        }

        _db.Opportunities.Add(opportunity);

        _outbox.Enqueue(
            OpportunityEventTypes.Created,
            aggregateType: nameof(Opportunity),
            aggregateId: opportunity.Id,
            payload: new
            {
                id = opportunity.Id,
                issuerEstablishmentId = opportunity.IssuerEstablishmentId,
                eventId = opportunity.EventId,
                opportunityCategoryId = opportunity.OpportunityCategoryId,
                status = opportunity.Status.ToString(),
                createdAt = now,
                createdByUserId = _currentUser.UserId,
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
            bundle.IsApplied);

        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/opportunities/{opportunity.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }

    // -- helpers ------------------------------------------------------------

    private static WorkingHoursType? ParseWorkingHours(string? value) =>
        Enum.TryParse<WorkingHoursType>(value, ignoreCase: true, out var v) ? v : null;

    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParse(value, out var t) ? t : null;

    private static EstablishmentClassification ParseClassification(string s) => s.ToLowerInvariant() switch
    {
        "small" => EstablishmentClassification.Small,
        "medium" => EstablishmentClassification.Medium,
        "large" => EstablishmentClassification.Large,
        "freelancers" => EstablishmentClassification.Freelancers,
        _ => EstablishmentClassification.None,
    };

    private static OpportunityGender ParseGender(string s) => s.ToLowerInvariant() switch
    {
        "male" => OpportunityGender.Male,
        "female" => OpportunityGender.Female,
        _ => OpportunityGender.None,
    };

    private static T CollapseFlags<T>(IReadOnlyList<string>? values, Func<string, T> parse)
        where T : struct, Enum
    {
        if (values is null || values.Count == 0)
        {
            return default;
        }
        var combined = 0;
        foreach (var value in values)
        {
            combined |= (int)(object)parse(value);
        }
        return (T)(object)combined;
    }
}

public sealed class CreateOpportunityRequest
{
    [JsonPropertyName("event_id")]
    public Guid EventId { get; init; }

    [JsonPropertyName("opportunity_category_id")]
    public Guid OpportunityCategoryId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("start_date")]
    public DateOnly StartDate { get; init; }

    [JsonPropertyName("end_date")]
    public DateOnly EndDate { get; init; }

    [JsonPropertyName("location_title")]
    public string LocationTitle { get; init; } = string.Empty;

    [JsonPropertyName("lat")]
    public decimal Lat { get; init; }

    [JsonPropertyName("lon")]
    public decimal Lon { get; init; }

    [JsonPropertyName("required_personnel")]
    public int RequiredPersonnel { get; init; }

    [JsonPropertyName("monthly_salary")]
    public decimal? MonthlySalary { get; init; }

    [JsonPropertyName("years_of_experience_required")]
    public byte? YearsOfExperienceRequired { get; init; }

    [JsonPropertyName("working_hours_type")]
    public string? WorkingHoursType { get; init; }

    [JsonPropertyName("working_hours_from")]
    public string? WorkingHoursFrom { get; init; }

    [JsonPropertyName("working_hours_to")]
    public string? WorkingHoursTo { get; init; }

    [JsonPropertyName("fees")]
    public decimal? Fees { get; init; }

    [JsonPropertyName("phone_contact_information")]
    public string? PhoneContactInformation { get; init; }

    [JsonPropertyName("email_contact_information")]
    public string? EmailContactInformation { get; init; }

    [JsonPropertyName("establishment_classification")]
    public IReadOnlyList<string>? EstablishmentClassification { get; init; }

    [JsonPropertyName("gender")]
    public IReadOnlyList<string>? Gender { get; init; }

    [JsonPropertyName("city_id")]
    public Guid? CityId { get; init; }

    [JsonPropertyName("nationality_id")]
    public Guid? NationalityId { get; init; }
}

public sealed class CreateOpportunityRequestValidator : Validator<CreateOpportunityRequest>
{
    public CreateOpportunityRequestValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.OpportunityCategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(10).MaximumLength(3000);
        RuleFor(x => x.LocationTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RequiredPersonnel).GreaterThanOrEqualTo(1).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.Lat).InclusiveBetween(-90m, 90m);
        RuleFor(x => x.Lon).InclusiveBetween(-180m, 180m);
        RuleFor(x => x.MonthlySalary).GreaterThanOrEqualTo(0).When(x => x.MonthlySalary.HasValue);
        RuleFor(x => x.Fees).GreaterThanOrEqualTo(0).When(x => x.Fees.HasValue);
        RuleFor(x => x.YearsOfExperienceRequired)
            .LessThanOrEqualTo((byte)100).When(x => x.YearsOfExperienceRequired.HasValue);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("end_date must be on or after start_date.");
        RuleFor(x => x.EmailContactInformation)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.EmailContactInformation));
    }
}
