using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Establishments.Profile.UpdateExperience;

/// <summary>
/// Mirrors the legacy <c>UpdateProfileExperienceRequest</c>:
/// <c>years_of_experience</c> required, integer, between 1 and 100.
/// </summary>
public sealed class UpdateExperienceValidator : Validator<UpdateExperienceRequest>
{
    public UpdateExperienceValidator()
    {
        RuleFor(x => x.YearsOfExperience)
            .NotNull().WithMessage("Years of experience is required.")
            .InclusiveBetween(1, 100).WithMessage("Years of experience must be between 1 and 100.");
    }
}
