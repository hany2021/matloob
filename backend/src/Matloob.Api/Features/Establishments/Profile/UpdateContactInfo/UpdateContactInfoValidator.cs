using System.Text.RegularExpressions;
using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Establishments.Profile.UpdateContactInfo;

/// <summary>
/// Shape validation for contact-info edits (mirrors the legacy
/// <c>UpdateProfileContactInfoRequest</c>: required + phone format + email
/// format). Uniqueness across establishments is checked in the handler (needs
/// the DB + the self-id to ignore), returning 422 to match Laravel.
/// </summary>
public sealed partial class UpdateContactInfoValidator : Validator<UpdateContactInfoRequest>
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    // Saudi-friendly phone: digits, optional leading +, length 5..50.
    [GeneratedRegex(@"^\+?[0-9\s\-()]{5,50}$")]
    private static partial Regex PhonePattern();

    public UpdateContactInfoValidator()
    {
        RuleFor(x => x.ContactNumber)
            .NotEmpty().WithMessage("Contact number is required.")
            .MaximumLength(50)
            .Must(v => v is not null && PhonePattern().IsMatch(v))
            .WithMessage("Contact number format is invalid.");

        RuleFor(x => x.AdditionalContactNumber)
            .NotEmpty().WithMessage("Additional contact number is required.")
            .MaximumLength(50)
            .Must(v => v is not null && PhonePattern().IsMatch(v))
            .WithMessage("Additional contact number format is invalid.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(320)
            .Must(v => v is not null && EmailPattern().IsMatch(v))
            .WithMessage("Email is not a valid address.");
    }
}
