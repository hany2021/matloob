using System.Text.RegularExpressions;
using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Establishments.Registration.UpdateBasicInfo;

/// <summary>
/// Field-shape validation only — no presence check on §3.1 fields, that
/// gate runs at SubmitForReview. Length caps line up with the EF mapping;
/// any string longer than the cap fails at the DB anyway, but catching it
/// here gives the caller a friendlier 400 instead of a 500.
/// </summary>
public sealed class UpdateBasicInfoValidator : Validator<UpdateBasicInfoRequest>
{
    // RFC 5321 says 320 chars; we mirror the EF column length.
    private const int EmailMax = 320;

    // Pragmatic mail regex — full RFC 5322 is too permissive for UI use cases.
    // Mirrors System.ComponentModel.DataAnnotations.EmailAddressAttribute.
    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Saudi-friendly phone: digits, optional leading +, length 5..30.
    private static readonly Regex PhonePattern = new(
        @"^\+?[0-9\s\-()]{5,30}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public UpdateBasicInfoValidator()
    {
        // §3.1 — length caps match EstablishmentConfiguration.
        RuleFor(x => x.Name).MaximumLength(255);
        RuleFor(x => x.CommercialRegistrationNumber).MaximumLength(50);
        RuleFor(x => x.LaborOfficeId).MaximumLength(50);
        RuleFor(x => x.SequenceNumber).MaximumLength(50);
        RuleFor(x => x.City).MaximumLength(100);

        RuleFor(x => x.Email)
            .MaximumLength(EmailMax)
            .Must(BeNullOrValidEmail).WithMessage("Email is not a valid address.");
        RuleFor(x => x.Phone)
            .MaximumLength(30)
            .Must(BeNullOrValidPhone).WithMessage("Phone format is invalid.");

        // §3.2 — same length checks.
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

        // Lat/lon range — column is numeric(8,6)/(9,6). Decimal range alone
        // is not enough; the field is a geographic coordinate.
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
