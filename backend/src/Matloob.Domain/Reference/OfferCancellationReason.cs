using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

public sealed class OfferCancellationReason : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// True for the "Other" catch-all reason that allows free-text input.
    /// Replaces the legacy heuristic that checked Ajeer-specific id values.
    /// </summary>
    public bool IsOther { get; private set; }

    public bool IsActive { get; private set; } = true;

    private OfferCancellationReason() { }

    public OfferCancellationReason(Guid id, string name, bool isOther = false, bool isActive = true)
    {
        Id = id;
        Name = name;
        IsOther = isOther;
        IsActive = isActive;
    }
}
