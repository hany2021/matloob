using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

public sealed class SuggestedLocation : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Title { get; private set; } = string.Empty;
    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }
    public bool IsActive { get; private set; } = true;

    private SuggestedLocation() { }

    public SuggestedLocation(Guid id, string title, decimal latitude, decimal longitude, bool isActive = true)
    {
        Id = id;
        Title = title;
        Latitude = latitude;
        Longitude = longitude;
        IsActive = isActive;
    }
}
