using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Profile.UpdateLanguagesSkills;

public sealed class UpdateLanguagesSkillsValidator : Validator<UpdateLanguagesSkillsRequest>
{
    private static readonly string[] Levels = ["beginner", "intermediate", "expert"];

    public UpdateLanguagesSkillsValidator()
    {
        RuleForEach(x => x.Skills).ChildRules(s =>
        {
            s.RuleFor(i => i.Name)
                .NotEmpty().WithMessage("Skill name is required.")
                .MaximumLength(255);
            s.RuleFor(i => i.Level)
                .Must(v => v is not null && Levels.Contains(v))
                .WithMessage("Skill level must be beginner, intermediate or expert.");
        });

        RuleForEach(x => x.Languages).ChildRules(l =>
        {
            l.RuleFor(i => i.Id)
                .NotNull().NotEqual(Guid.Empty).WithMessage("Language id is required.");
            l.RuleFor(i => i.Level)
                .Must(v => v is not null && Levels.Contains(v))
                .WithMessage("Language level must be beginner, intermediate or expert.");
        });
    }
}
