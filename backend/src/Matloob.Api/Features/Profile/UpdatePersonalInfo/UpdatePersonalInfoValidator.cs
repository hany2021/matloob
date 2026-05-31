using System.Text.RegularExpressions;
using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Profile.UpdatePersonalInfo;

public sealed partial class UpdatePersonalInfoValidator : Validator<UpdatePersonalInfoRequest>
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9\s\-()]{5,30}$")]
    private static partial Regex PhonePattern();

    public UpdatePersonalInfoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(320)
            .Must(v => v is not null && EmailPattern().IsMatch(v))
            .WithMessage("Email is not a valid address.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .Must(v => v is not null && PhonePattern().IsMatch(v))
            .WithMessage("Phone number format is invalid.");

        RuleFor(x => x.AdditionalPhoneNumber)
            .NotEmpty().WithMessage("Additional phone number is required.")
            .Must(v => v is not null && PhonePattern().IsMatch(v))
            .WithMessage("Additional phone number format is invalid.");

        RuleFor(x => x.Bio)
            .NotEmpty().WithMessage("Bio is required.")
            .MaximumLength(250);

        RuleFor(x => x.CityId)
            .NotNull().NotEqual(Guid.Empty).WithMessage("City is required.");

        RuleFor(x => x.RegionId)
            .NotNull().NotEqual(Guid.Empty).WithMessage("Region is required.");

        RuleFor(x => x.BankId)
            .NotNull().NotEqual(Guid.Empty).WithMessage("Bank is required.");

        RuleFor(x => x.Iban)
            .NotEmpty().WithMessage("IBAN is required.");
    }
}
