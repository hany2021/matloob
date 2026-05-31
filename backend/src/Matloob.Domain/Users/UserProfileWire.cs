namespace Matloob.Domain.Users;

/// <summary>
/// Central mapping between the user-profile enums and their legacy Laravel
/// wire tokens (snake_case). Used by both the EF value converters (so the DB
/// column stores the same token Laravel used) and the API DTO layer (so the
/// JSON response is byte-for-byte compatible with the old UserResource).
///
/// Keeping the map in the domain avoids the token drifting between the
/// persistence layer and the wire layer.
/// </summary>
public static class UserProfileWire
{
    // ---- Gender ----
    public static string ToWire(this Gender value) => value switch
    {
        Gender.Male => "male",
        Gender.Female => "female",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static Gender ParseGender(string token) => token switch
    {
        "male" => Gender.Male,
        "female" => Gender.Female,
        _ => throw new ArgumentException($"Unknown gender token '{token}'.", nameof(token)),
    };

    // ---- EducationDegree ----
    public static string ToWire(this EducationDegree value) => value switch
    {
        EducationDegree.Doctorate => "doctorate",
        EducationDegree.Master => "master",
        EducationDegree.Bachelor => "bachelor",
        EducationDegree.Diploma => "diploma",
        EducationDegree.HighSchool => "high_school",
        EducationDegree.ElementarySchool => "elementary_school",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static EducationDegree ParseDegree(string token) => token switch
    {
        "doctorate" => EducationDegree.Doctorate,
        "master" => EducationDegree.Master,
        "bachelor" => EducationDegree.Bachelor,
        "diploma" => EducationDegree.Diploma,
        "high_school" => EducationDegree.HighSchool,
        "elementary_school" => EducationDegree.ElementarySchool,
        _ => throw new ArgumentException($"Unknown degree token '{token}'.", nameof(token)),
    };

    public static string DegreeLabel(this EducationDegree value) => value switch
    {
        EducationDegree.Doctorate => "Doctorate",
        EducationDegree.Master => "Master",
        EducationDegree.Bachelor => "Bachelor",
        EducationDegree.Diploma => "Diploma",
        EducationDegree.HighSchool => "High School",
        EducationDegree.ElementarySchool => "Elementary School",
        _ => string.Empty,
    };

    // ---- ProficiencyLevel ----
    public static string ToWire(this ProficiencyLevel value) => value switch
    {
        ProficiencyLevel.Beginner => "beginner",
        ProficiencyLevel.Intermediate => "intermediate",
        ProficiencyLevel.Expert => "expert",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ProficiencyLevel ParseLevel(string token) => token switch
    {
        "beginner" => ProficiencyLevel.Beginner,
        "intermediate" => ProficiencyLevel.Intermediate,
        "expert" => ProficiencyLevel.Expert,
        _ => throw new ArgumentException($"Unknown level token '{token}'.", nameof(token)),
    };

    public static string LevelLabel(this ProficiencyLevel value) => value switch
    {
        ProficiencyLevel.Beginner => "Beginner",
        ProficiencyLevel.Intermediate => "Intermediate",
        ProficiencyLevel.Expert => "Expert",
        _ => string.Empty,
    };

    // ---- ExperienceType ----
    public static string ToWire(this ExperienceType value) => value switch
    {
        ExperienceType.FullTime => "full_time",
        ExperienceType.PartTime => "part_time",
        ExperienceType.Internship => "internship",
        ExperienceType.FieldInternship => "field_internship",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ExperienceType ParseExperienceType(string token) => token switch
    {
        "full_time" => ExperienceType.FullTime,
        "part_time" => ExperienceType.PartTime,
        "internship" => ExperienceType.Internship,
        "field_internship" => ExperienceType.FieldInternship,
        _ => throw new ArgumentException($"Unknown experience type token '{token}'.", nameof(token)),
    };

    public static string ExperienceTypeLabel(this ExperienceType value) => value switch
    {
        ExperienceType.FullTime => "Full Time",
        ExperienceType.PartTime => "Part Time",
        ExperienceType.Internship => "Internship",
        ExperienceType.FieldInternship => "Field Internship",
        _ => string.Empty,
    };
}
