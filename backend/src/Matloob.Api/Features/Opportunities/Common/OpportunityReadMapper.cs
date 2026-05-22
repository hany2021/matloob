using Matloob.Domain.Establishments;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;

namespace Matloob.Api.Features.Opportunities.Common;

/// <summary>
/// Pure mapping helpers from EF entities to the Laravel-compatible
/// <see cref="OpportunityResponse"/> wire shape. Centralised so every
/// browse / show / mine endpoint emits the exact same fields and
/// formatting (date strings as <c>yyyy-MM-dd</c>, time strings as
/// <c>HH:mm:ss</c>, classification CSV exploded into an array, etc.).
/// </summary>
internal static class OpportunityReadMapper
{
    public static OpportunityResponse Map(
        Opportunity opportunity,
        OpportunityCategory? category,
        Establishment? issuer,
        Nationality? nationality,
        IReadOnlyList<SuccessManagementCriterion> successCriteria,
        IReadOnlyList<OpportunityUploadDto> uploads,
        int applicantsCount,
        bool? isApplied)
    {
        return new OpportunityResponse
        {
            Id = opportunity.Id,
            Name = opportunity.Name,
            Description = opportunity.Description,
            StartDate = opportunity.StartDate.ToString("yyyy-MM-dd"),
            EndDate = opportunity.EndDate.ToString("yyyy-MM-dd"),
            Lat = opportunity.Latitude,
            Lon = opportunity.Longitude,
            LocationTitle = opportunity.LocationTitle,
            RequiredPersonnel = opportunity.RequiredPersonnel,
            MonthlySalary = opportunity.MonthlySalary,
            YearsOfExperienceRequired = opportunity.YearsOfExperienceRequired,
            EstablishmentClassification = ExpandClassificationFlags(opportunity.EstablishmentClassifications),
            EstablishmentClassificationLabel = null,
            WorkingHoursType = opportunity.WorkingHoursType?.ToString(),
            WorkingHoursTypeLabel = null,
            WorkingHoursFrom = opportunity.WorkingHoursFrom?.ToString("HH:mm:ss"),
            WorkingHoursTo = opportunity.WorkingHoursTo?.ToString("HH:mm:ss"),
            Fees = opportunity.Fees,
            PhoneContactInformation = opportunity.PhoneContactInformation,
            EmailContactInformation = opportunity.EmailContactInformation,
            Gender = ExpandGenderFlags(opportunity.Genders),
            GenderLabel = null,
            Nationality = nationality is null
                ? null
                : new NamedRefDto { Id = nationality.Id, Name = nationality.Name },
            Status = opportunity.Status.ToString(),
            CardType = null,
            StatusLabel = null,
            StatusIcon = null,
            Event = null,
            OpportunityCategory = category is null
                ? null
                : new OpportunityCategoryDto
                {
                    Id = category.Id,
                    Title = category.Title,
                    Description = category.Description,
                    Icon = category.Icon,
                    ForVacancy = category.ForVacancy,
                    IsOther = category.IsOther,
                },
            Uploads = uploads,
            Applicants = [],
            ApplicantsCount = applicantsCount,
            CanEnd = CanEnd(opportunity.Status),
            Issuer = issuer is null
                ? null
                : new OpportunityIssuerDto
                {
                    Id = issuer.Id,
                    Name = issuer.Name,
                    Email = issuer.Email,
                    Logo = null,
                },
            SuccessCriteria = successCriteria
                .Select(sc => new SuccessCriterionDto
                {
                    Id = sc.Id,
                    Output = sc.Output,
                    SuccessCriteria = sc.SuccessCriteria,
                    Comment = sc.Comment,
                    Uploads = [],
                })
                .ToList(),
            IsApplied = isApplied,
        };
    }

    /// <summary>
    /// Matches Laravel's <c>OpportunitySupport::canEnd</c>: only Upcoming
    /// or Active opportunities can be manually ended.
    /// </summary>
    public static bool CanEnd(OpportunityStatus status) =>
        status is OpportunityStatus.Upcoming or OpportunityStatus.Active;

    public static IReadOnlyList<string> ExpandClassificationFlags(EstablishmentClassification flags)
    {
        var values = new List<string>(4);
        if ((flags & EstablishmentClassification.Small) != 0) values.Add("small");
        if ((flags & EstablishmentClassification.Medium) != 0) values.Add("medium");
        if ((flags & EstablishmentClassification.Large) != 0) values.Add("large");
        if ((flags & EstablishmentClassification.Freelancers) != 0) values.Add("freelancers");
        return values;
    }

    public static IReadOnlyList<string> ExpandGenderFlags(OpportunityGender flags)
    {
        var values = new List<string>(2);
        if ((flags & OpportunityGender.Male) != 0) values.Add("male");
        if ((flags & OpportunityGender.Female) != 0) values.Add("female");
        return values;
    }
}
