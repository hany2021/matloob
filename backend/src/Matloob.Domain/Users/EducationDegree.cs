namespace Matloob.Domain.Users;

/// <summary>
/// Academic degree levels for <see cref="UserEducation"/>. Wire tokens match
/// the legacy Laravel enum: doctorate, master, bachelor, diploma, high_school,
/// elementary_school.
/// </summary>
public enum EducationDegree
{
    Doctorate = 1,
    Master = 2,
    Bachelor = 3,
    Diploma = 4,
    HighSchool = 5,
    ElementarySchool = 6,
}
