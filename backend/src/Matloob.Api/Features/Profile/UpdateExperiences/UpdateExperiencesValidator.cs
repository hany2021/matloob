using System.Globalization;
using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Profile.UpdateExperiences;

public sealed class UpdateExperiencesValidator : Validator<UpdateExperiencesRequest>
{
    private static readonly string[] Types =
        ["full_time", "part_time", "internship", "field_internship"];

    public UpdateExperiencesValidator()
    {
        RuleFor(x => x.YearsOfExperience)
            .NotNull().WithMessage("Years of experience is required.")
            .InclusiveBetween(1, 100);

        RuleForEach(x => x.Experiences).ChildRules(e =>
        {
            e.RuleFor(i => i.Company)
                .NotEmpty().WithMessage("Company is required.").MaximumLength(60);
            e.RuleFor(i => i.Position)
                .NotEmpty().WithMessage("Position is required.").MaximumLength(60);
            e.RuleFor(i => i.From)
                .Must(BeADate).WithMessage("From must be a valid date (yyyy-MM-dd).");
            e.RuleFor(i => i.To)
                .Must(v => string.IsNullOrEmpty(v) || BeADate(v))
                .WithMessage("To must be a valid date (yyyy-MM-dd).");
            e.RuleFor(i => i.Description).MaximumLength(5000);
            e.RuleFor(i => i.Type)
                .Must(v => v is not null && Types.Contains(v))
                .WithMessage("Type must be full_time, part_time, internship or field_internship.");
        });
    }

    private static bool BeADate(string? value)
        => value is not null && DateOnly.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
