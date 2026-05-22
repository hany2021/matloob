namespace Matloob.Domain.Opportunities;

/// <summary>
/// Gender slots an opportunity is open to. Legacy Laravel stored this as a
/// CSV column (<c>'male,female'</c>); the new schema stores it as
/// <see cref="System.FlagsAttribute"/> bitflags backed by a single integer
/// column.
///
/// Bit positions are stable forever — never re-number; only add.
/// </summary>
[Flags]
public enum OpportunityGender
{
    None   = 0,
    Male   = 1 << 0,    // 1
    Female = 1 << 1,    // 2
}
