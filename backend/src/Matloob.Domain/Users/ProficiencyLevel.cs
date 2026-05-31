namespace Matloob.Domain.Users;

/// <summary>
/// Skill / language proficiency. Wire tokens: beginner, intermediate, expert.
/// Shared by <see cref="UserSkill"/> and <see cref="UserLanguageProficiency"/>.
/// </summary>
public enum ProficiencyLevel
{
    Beginner = 1,
    Intermediate = 2,
    Expert = 3,
}
