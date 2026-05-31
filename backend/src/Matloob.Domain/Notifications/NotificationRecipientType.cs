namespace Matloob.Domain.Notifications;

/// <summary>
/// Who a notification is addressed to (legacy Laravel polymorphic
/// <c>notifiable</c>). Stored as the lowercase wire token.
/// </summary>
public enum NotificationRecipientType
{
    /// <summary>An individual — <see cref="Notification.RecipientId"/> is the IdM <c>sub</c>.</summary>
    User = 0,

    /// <summary>An establishment — <see cref="Notification.RecipientId"/> is its Guid.</summary>
    Establishment = 1,
}

public static class NotificationRecipientTypeWire
{
    public static string ToWire(this NotificationRecipientType type) => type switch
    {
        NotificationRecipientType.User => "user",
        NotificationRecipientType.Establishment => "establishment",
        _ => "user",
    };
}

/// <summary>Notification kind tokens the frontend switches on for routing/icons.</summary>
public static class NotificationTypes
{
    public const string SentOffer = "sent_offer";
    public const string ReceivedOffer = "received_offer";
    public const string Event = "event";
    public const string Opportunity = "opportunity";
}
