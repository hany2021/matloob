using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matloob.Api.Infrastructure.Persistence.Configurations.Users;

/// <summary>
/// Shared EF value converters that persist the user-profile enums as their
/// legacy Laravel wire tokens (e.g. "high_school", "field_internship") so the
/// DB column matches what the old app stored. EF auto-wraps these for nullable
/// properties.
/// </summary>
internal static class UserProfileConverters
{
    public static readonly ValueConverter<Gender, string> Gender =
        new(v => v.ToWire(), v => UserProfileWire.ParseGender(v));

    public static readonly ValueConverter<EducationDegree, string> Degree =
        new(v => v.ToWire(), v => UserProfileWire.ParseDegree(v));

    public static readonly ValueConverter<ProficiencyLevel, string> Level =
        new(v => v.ToWire(), v => UserProfileWire.ParseLevel(v));

    public static readonly ValueConverter<ExperienceType, string> ExperienceType =
        new(v => v.ToWire(), v => UserProfileWire.ParseExperienceType(v));
}
