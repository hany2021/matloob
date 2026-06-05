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
/// <c>PATCH /api/establishments/me/opportunities/{id}</c> +
/// <c>PATCH /api/v1/establishments/{establishmentId}/opportunities/{id}</c>
/// — partial update for an owner-side opportunity. Per the OAO preparation
/// plan default, edits remain unlocked even after applicants exist
/// (Q-OPP-CR-RESTRICTED-EDIT — match Laravel).
/// </summary>
public sealed class UpdateOpportunityEndpoint
    : Endpoint<UpdateOpportunityRequest, OpportunityResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly TimeProvider _clock;

    public UpdateOpportunityEndpoint(
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
        Patch(
            "/api/establishments/me/opportunities/{id}",
            "/api/v1/establishments/{establishmentId}/opportunities/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OpportunityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Patch an owner-side opportunity.";
        });
    }

    public override async Task HandleAsync(UpdateOpportunityRequest req, CancellationToken ct)
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

        if (req.OpportunityCategoryId is { } newCategoryId)
        {
            var categoryExists = await _db.OpportunityCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == newCategoryId, ct);
            if (!categoryExists)
            {
                await ProblemWriter.WriteAsync(
                    HttpContext,
                    StatusCodes.Status422UnprocessableEntity,
                    OpportunityErrorCodes.CategoryNotFound,
                    "Opportunity category does not exist.",
                    ct);
                return;
            }
        }

        try
        {
            opportunity.Update(
                name: req.Name,
                description: req.Description,
                startDate: req.StartDate,
                endDate: req.EndDate,
                locationTitle: req.LocationTitle,
                latitude: req.Lat,
                longitude: req.Lon,
                requiredPersonnel: req.RequiredPersonnel,
                opportunityCategoryId: req.OpportunityCategoryId,
                cityId: req.CityId,
                nationalityId: req.NationalityId,
                monthlySalary: req.MonthlySalary,
                yearsOfExperienceRequired: req.YearsOfExperienceRequired,
                workingHoursType: WorkingHoursTypeWire.Parse(req.WorkingHoursType),
                workingHoursFrom: ParseTime(req.WorkingHoursFrom),
                workingHoursTo: ParseTime(req.WorkingHoursTo),
                fees: req.Fees,
                phoneContactInformation: req.PhoneContactInformation,
                emailContactInformation: req.EmailContactInformation,
                establishmentClassifications: CollapseFlags(req.EstablishmentClassification, ParseClassification),
                genders: CollapseFlags(req.Gender, ParseGender));
        }
        catch (InvalidOperationException ex)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                "invalid_opportunity_payload",
                ex.Message,
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        _outbox.Enqueue(
            OpportunityEventTypes.Updated,
            aggregateType: nameof(Opportunity),
            aggregateId: opportunity.Id,
            payload: new
            {
                id = opportunity.Id,
                updatedAt = now,
                updatedByUserId = _currentUser.UserId,
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

    // -- helpers shared with CreateOpportunityEndpoint -----------------------

    private static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var v) ? v : null;

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

    private static T? CollapseFlags<T>(IReadOnlyList<string>? values, Func<string, T> parse)
        where T : struct, Enum
    {
        if (values is null) return null;
        if (values.Count == 0) return default(T);
        var combined = 0;
        foreach (var value in values)
        {
            combined |= (int)(object)parse(value);
        }
        return (T)(object)combined;
    }
}

public sealed class UpdateOpportunityRequest
{
    [JsonPropertyName("opportunity_category_id")]
    public Guid? OpportunityCategoryId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("start_date")]
    public DateOnly? StartDate { get; init; }

    [JsonPropertyName("end_date")]
    public DateOnly? EndDate { get; init; }

    [JsonPropertyName("location_title")]
    public string? LocationTitle { get; init; }

    [JsonPropertyName("lat")]
    public decimal? Lat { get; init; }

    [JsonPropertyName("lon")]
    public decimal? Lon { get; init; }

    [JsonPropertyName("required_personnel")]
    public int? RequiredPersonnel { get; init; }

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

public sealed class UpdateOpportunityRequestValidator : Validator<UpdateOpportunityRequest>
{
    public UpdateOpportunityRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(120).When(x => x.Name is not null);
        RuleFor(x => x.Description).MinimumLength(10).MaximumLength(3000)
            .When(x => x.Description is not null);
        RuleFor(x => x.LocationTitle).MaximumLength(200).When(x => x.LocationTitle is not null);
        RuleFor(x => x.RequiredPersonnel)
            .GreaterThanOrEqualTo(1).LessThanOrEqualTo(1_000_000)
            .When(x => x.RequiredPersonnel.HasValue);
        RuleFor(x => x.Lat).InclusiveBetween(-90m, 90m).When(x => x.Lat.HasValue);
        RuleFor(x => x.Lon).InclusiveBetween(-180m, 180m).When(x => x.Lon.HasValue);
        RuleFor(x => x.MonthlySalary).GreaterThanOrEqualTo(0).When(x => x.MonthlySalary.HasValue);
        RuleFor(x => x.Fees).GreaterThanOrEqualTo(0).When(x => x.Fees.HasValue);
    }
}
