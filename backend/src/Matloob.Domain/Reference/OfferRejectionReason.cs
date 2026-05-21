using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

public sealed class OfferRejectionReason : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// True for the "Other" catch-all reason that allows free-text input.
    /// Replaces the legacy <c>ajeer_id == OTHER_AJEER_REASON_ID</c> check;
    /// Ajeer is removed from the new system.
    /// </summary>
    public bool IsOther { get; private set; }

    public bool IsActive { get; private set; } = true;

    private OfferRejectionReason() { }

    public OfferRejectionReason(Guid id, string name, bool isOther = false, bool isActive = true)
    {
        Id = id;
        Name = name;
        IsOther = isOther;
        IsActive = isActive;
    }
}
