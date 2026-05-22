namespace Matloob.Domain.Opportunities;

/// <summary>
/// Shape of the working-hours window an opportunity offers. Mirrors the
/// legacy Laravel <c>WorkingHoursType</c> enum.
/// </summary>
public enum WorkingHoursType
{
    /// <summary>Fixed daily window (working_hours_from / working_hours_to apply).</summary>
    Fixed = 0,

    /// <summary>Flexible; worker chooses within shift bands.</summary>
    Flexible = 1,

    /// <summary>Shift-based rotation.</summary>
    Shifts = 2,
}
