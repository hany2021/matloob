using Matloob.Domain.Common;

namespace Matloob.Domain.Notifications;

/// <summary>
/// A delivered in-app notification (legacy Laravel <c>notifications</c> table,
/// <c>database</c> channel). Addressed to a user or establishment
/// (<see cref="RecipientType"/> + <see cref="RecipientId"/>). Created by the
/// outbox fanout subscriber; read via the notifications endpoints.
/// </summary>
public sealed class Notification : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public NotificationRecipientType RecipientType { get; private set; }

    /// <summary>IdM <c>sub</c> for users, establishment Guid (string) for establishments.</summary>
    public string RecipientId { get; private set; } = string.Empty;

    /// <summary>Kind token — see <see cref="NotificationTypes"/>.</summary>
    public string Type { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    public string? ResourceType { get; private set; }
    public string? ResourceId { get; private set; }
    public string? Image { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    private Notification() { }

    public Notification(
        Guid id,
        NotificationRecipientType recipientType,
        string recipientId,
        string type,
        string title,
        string message,
        string? resourceType = null,
        string? resourceId = null,
        string? image = null)
    {
        Id = id;
        RecipientType = recipientType;
        RecipientId = recipientId;
        Type = type;
        Title = title;
        Message = message;
        ResourceType = resourceType;
        ResourceId = resourceId;
        Image = image;
    }

    public void MarkRead(DateTimeOffset at)
    {
        ReadAt ??= at;
    }
}
