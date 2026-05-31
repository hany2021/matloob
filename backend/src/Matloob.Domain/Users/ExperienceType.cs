namespace Matloob.Domain.Users;

/// <summary>
/// Employment type for <see cref="UserExperience"/>. Wire tokens:
/// full_time, part_time, internship, field_internship.
/// </summary>
public enum ExperienceType
{
    FullTime = 1,
    PartTime = 2,
    Internship = 3,
    FieldInternship = 4,
}
