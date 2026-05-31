using System.Globalization;
using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Establishments.Profile.StoreExperience;

/// <summary>
/// Shape validation mirroring the legacy <c>StoreProfileExperienceRequest</c>:
/// type ∈ {event, opportunity}, name 3..255, category required uuid, from/to
/// valid dates with to after from, description 3..200. Category *existence*
/// (in the event-type / opportunity-category table) is checked in the handler.
/// </summary>
public sealed class StoreExperienceValidator : Validator<StoreExperienceRequest>
{
    public StoreExperienceValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Type is required.")
            .Must(v => v is "event" or "opportunity")
            .WithMessage("Type must be event or opportunity.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MinimumLength(3).MaximumLength(255);

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .Must(v => Guid.TryParse(v, out _)).WithMessage("Category is required.");

        RuleFor(x => x.From)
            .NotEmpty().WithMessage("From date is required.")
            .Must(BeDate).WithMessage("From must be a valid date.");

        RuleFor(x => x.To)
            .NotEmpty().WithMessage("To date is required.")
            .Must(BeDate).WithMessage("To must be a valid date.")
            .Must((req, to) => IsAfter(req.From, to))
            .WithMessage("To must be after from.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MinimumLength(3).MaximumLength(200);
    }

    private static bool BeDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, out _);

    private static bool IsAfter(string? from, string? to) =>
        DateOnly.TryParse(from, CultureInfo.InvariantCulture, out var f)
        && DateOnly.TryParse(to, CultureInfo.InvariantCulture, out var t)
        && t > f;
}
