using Matloob.Domain.Establishments;
using Matloob.Domain.Opportunities;

namespace Matloob.Api.Features.Opportunities.Common;

/// <summary>
/// Shared field-to-aggregate builder for opportunities, so both the JSON
/// <c>CreateOpportunityEndpoint</c> and the event-wizard's nested
/// <c>opportunities[]</c> path construct an <see cref="Opportunity"/> the same
/// way (classification/gender flag collapsing, working-hours parsing, etc.).
/// </summary>
internal static class OpportunityBuildSupport
{
    public static Opportunity Build(
        Guid establishmentId,
        Guid eventId,
        Guid categoryId,
        string? name,
        string? description,
        DateOnly? startDate,
        DateOnly? endDate,
        string? locationTitle,
        decimal latitude,
        decimal longitude,
        int requiredPersonnel,
        DateTimeOffset now,
        Guid? cityId,
        Guid? nationalityId,
        decimal? monthlySalary,
        byte? yearsOfExperienceRequired,
        string? workingHoursTypeRaw,
        string? workingHoursFromRaw,
        string? workingHoursToRaw,
        decimal? fees,
        string? phoneContact,
        string? emailContact,
        IReadOnlyList<string>? classifications,
        IReadOnlyList<string>? genders)
    {
        return Opportunity.Create(
            id: Guid.NewGuid(),
            issuerEstablishmentId: establishmentId,
            eventId: eventId,
            opportunityCategoryId: categoryId,
            name: name ?? string.Empty,
            description: description ?? string.Empty,
            startDate: startDate ?? throw new ArgumentException("start_date is required.", nameof(startDate)),
            endDate: endDate ?? throw new ArgumentException("end_date is required.", nameof(endDate)),
            locationTitle: locationTitle ?? string.Empty,
            latitude: latitude,
            longitude: longitude,
            requiredPersonnel: requiredPersonnel,
            now: now,
            cityId: cityId,
            nationalityId: nationalityId,
            monthlySalary: monthlySalary,
            yearsOfExperienceRequired: yearsOfExperienceRequired,
            workingHoursType: WorkingHoursTypeWire.Parse(workingHoursTypeRaw),
            workingHoursFrom: ParseTime(workingHoursFromRaw),
            workingHoursTo: ParseTime(workingHoursToRaw),
            fees: fees,
            phoneContactInformation: phoneContact,
            emailContactInformation: emailContact,
            establishmentClassifications: CollapseClassifications(classifications),
            genders: CollapseGenders(genders));
    }

    public static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var v) ? v : null;

    public static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParse(value, out var t) ? t : null;

    public static EstablishmentClassification CollapseClassifications(IReadOnlyList<string>? values)
    {
        if (values is null) return EstablishmentClassification.None;
        var result = EstablishmentClassification.None;
        foreach (var v in values)
        {
            result |= v.ToLowerInvariant() switch
            {
                "small" => EstablishmentClassification.Small,
                "medium" => EstablishmentClassification.Medium,
                "large" => EstablishmentClassification.Large,
                "freelancers" => EstablishmentClassification.Freelancers,
                _ => EstablishmentClassification.None,
            };
        }
        return result;
    }

    public static OpportunityGender CollapseGenders(IReadOnlyList<string>? values)
    {
        if (values is null) return OpportunityGender.None;
        var result = OpportunityGender.None;
        foreach (var v in values)
        {
            result |= v.ToLowerInvariant() switch
            {
                "male" => OpportunityGender.Male,
                "female" => OpportunityGender.Female,
                _ => OpportunityGender.None,
            };
        }
        return result;
    }
}
