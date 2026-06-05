namespace Matloob.Domain.Opportunities;

/// <summary>
/// Shape of the working-hours window an opportunity offers. Mirrors the legacy
/// Laravel <c>WorkingHoursType</c> string enum (<c>full_time</c>/<c>part_time</c>)
/// — the public frontend sends and expects exactly those wire tokens, so the
/// enum is mapped to/from them via <see cref="WorkingHoursTypeWire"/> on read,
/// write, and persistence (C# members can't be snake_case).
/// </summary>
public enum WorkingHoursType
{
    FullTime = 0,
    PartTime = 1,
}

/// <summary>
/// Maps <see cref="WorkingHoursType"/> to/from the lowercase snake_case wire
/// token the public frontend uses (<c>full_time</c>/<c>part_time</c>), matching
/// the legacy Laravel backing values. Modelling it as <c>Fixed/Flexible/Shifts</c>
/// (the old buggy shape) made <c>Enum.TryParse("full_time")</c> fail, so the
/// value was silently dropped on every opportunity create.
/// </summary>
public static class WorkingHoursTypeWire
{
    public static string ToWire(this WorkingHoursType type) => type switch
    {
        WorkingHoursType.FullTime => "full_time",
        WorkingHoursType.PartTime => "part_time",
        _ => "full_time",
    };

    /// <summary>Parses a wire token; null/blank/unknown → null (the field is optional).</summary>
    public static WorkingHoursType? Parse(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "full_time" => WorkingHoursType.FullTime,
        "part_time" => WorkingHoursType.PartTime,
        _ => null,
    };
}
