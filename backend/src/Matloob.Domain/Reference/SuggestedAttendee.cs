using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

public sealed class SuggestedAttendee : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public int Min { get; private set; }
    public int Max { get; private set; }
    public bool IsActive { get; private set; } = true;

    private SuggestedAttendee() { }

    public SuggestedAttendee(Guid id, int min, int max, bool isActive = true)
    {
        Id = id;
        Min = min;
        Max = max;
        IsActive = isActive;
    }
}
