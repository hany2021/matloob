using System.Text.RegularExpressions;
using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Establishments.ChangeRequests.UpdateBasicInfo;

/// <summary>
/// Same length / format rules as the onboarding
/// <c>UpdateBasicInfoValidator</c> but applied to the Proposed* surface.
/// Copy-of pattern is acceptable here: the spec keeps the two field
/// sets in lock-step (§3.1 + §3.2), so the validation has to mirror.
/// </summary>
public sealed class UpdateChangeRequestBasicInfoValidator
    : Validator<UpdateChangeRequestBasicInfoRequest>
{
    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PhonePattern = new(
        @"^\+?[0-9\s\-()]{5,30}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public UpdateChangeRequestBasicInfoValidator()
    {
        RuleFor(x => x.Name).MaximumLength(255);
        RuleFor(x => x.CommercialRegistrationNumber).MaximumLength(50);
        RuleFor(x => x.LaborOfficeId).MaximumLength(50);
        RuleFor(x => x.SequenceNumber).MaximumLength(50);
        RuleFor(x => x.City).MaximumLength(100);

        RuleFor(x => x.Email)
            .MaximumLength(320)
            .Must(BeNullOrValidEmail).WithMessage("Email is not a valid address.");
        RuleFor(x => x.Phone)
            .MaximumLength(30)
            .Must(BeNullOrValidPhone).WithMessage("Phone format is invalid.");

        RuleFor(x => x.EconomicActivity).MaximumLength(200);
        RuleFor(x => x.SubEconomicActivity).MaximumLength(200);
        RuleFor(x => x.District).MaximumLength(200);
        RuleFor(x => x.Area).MaximumLength(200);
        RuleFor(x => x.Street).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.LocationTitle).MaximumLength(200);
        RuleFor(x => x.BuildingNumber).MaximumLength(20);
        RuleFor(x => x.PostalCode).MaximumLength(20);
        RuleFor(x => x.AdditionalNumber).MaximumLength(20);
        RuleFor(x => x.Website).MaximumLength(500);
        RuleFor(x => x.EstablishmentSize).MaximumLength(50);
        RuleFor(x => x.AdditionalContactNumber)
            .MaximumLength(30)
            .Must(BeNullOrValidPhone).WithMessage("AdditionalContactNumber format is invalid.");

        RuleFor(x => x.Latitude)
            .Must(v => v is null || (v >= -90m && v <= 90m))
            .WithMessage("Latitude must be between -90 and 90.");
        RuleFor(x => x.Longitude)
            .Must(v => v is null || (v >= -180m && v <= 180m))
            .WithMessage("Longitude must be between -180 and 180.");

        RuleFor(x => x.YearsOfExperience)
            .Must(v => v is null || v >= 0)
            .WithMessage("YearsOfExperience cannot be negative.");
    }

    private static bool BeNullOrValidEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) || EmailPattern.IsMatch(value);

    private static bool BeNullOrValidPhone(string? value) =>
        string.IsNullOrWhiteSpace(value) || PhonePattern.IsMatch(value);
}
