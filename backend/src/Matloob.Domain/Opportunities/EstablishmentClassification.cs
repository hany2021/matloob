namespace Matloob.Domain.Opportunities;

/// <summary>
/// Establishment size classifications an opportunity is open to. Legacy
/// Laravel stored this as a CSV column (<c>'small,medium'</c>); the new
/// schema stores it as <see cref="System.FlagsAttribute"/> bitflags backed
/// by a single integer column.
///
/// Bit positions are stable forever — never re-number; only add.
/// </summary>
[Flags]
public enum EstablishmentClassification
{
    None        = 0,
    Small       = 1 << 0,   // 1
    Medium      = 1 << 1,   // 2
    Large       = 1 << 2,   // 4
    Freelancers = 1 << 3,   // 8
}
