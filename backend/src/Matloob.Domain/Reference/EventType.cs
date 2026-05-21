using Matloob.Domain.Common;

namespace Matloob.Domain.Reference;

/// <summary>
/// Event type lookup. Carries display assets (icon, background) that the
/// public frontend renders on event-creation forms.
/// </summary>
public sealed class EventType : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Relative path to the type's background image.</summary>
    public string? Background { get; private set; }

    /// <summary>Relative path to the type's icon.</summary>
    public string? Icon { get; private set; }

    public bool IsActive { get; private set; } = true;

    private EventType() { }

    public EventType(
        Guid id,
        string name,
        string? description = null,
        string? background = null,
        string? icon = null,
        bool isActive = true)
    {
        Id = id;
        Name = name;
        Description = description;
        Background = background;
        Icon = icon;
        IsActive = isActive;
    }
}
