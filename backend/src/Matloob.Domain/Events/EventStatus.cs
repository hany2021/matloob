namespace Matloob.Domain.Events;

/// <summary>
/// Lifecycle of an establishment event, mirroring the legacy Laravel
/// <c>EventStatus</c> enum. Stored as the PascalCase name; the API emits the
/// lowercase wire token via <see cref="EventStatusWire.ToWire"/>.
/// </summary>
public enum EventStatus
{
    /// <summary>Work-in-progress draft (created progressively, not yet published).</summary>
    Drafted = 0,

    /// <summary>Published, start date in the future.</summary>
    Upcoming = 1,

    /// <summary>Published and running.</summary>
    Active = 2,

    /// <summary>Ended manually (or by the end action).</summary>
    Ended = 3,

    /// <summary>End date passed — derived/eventual state.</summary>
    Finished = 4,
}

public static class EventStatusWire
{
    public static string ToWire(this EventStatus status) => status switch
    {
        EventStatus.Drafted => "drafted",
        EventStatus.Upcoming => "upcoming",
        EventStatus.Active => "active",
        EventStatus.Ended => "ended",
        EventStatus.Finished => "finished",
        _ => "drafted",
    };

    /// <summary>
    /// Human-readable Arabic label for the events-page tab titles + card
    /// status pills. Matches the existing frontend i18n `tabs.*` wording
    /// (messages/ar.json) so the new API displays statuses identically to
    /// the legacy Laravel app. The frontend reads `status_label` verbatim
    /// (no client-side i18n on it), so this is the authoritative source.
    /// </summary>
    public static string Label(this EventStatus status) => status switch
    {
        EventStatus.Drafted => "بانتظار الإكمال",
        EventStatus.Upcoming => "القادمة",
        EventStatus.Active => "نشطة",
        EventStatus.Ended => "منتهية",
        EventStatus.Finished => "منتهية",
        _ => "بانتظار الإكمال",
    };

    /// <summary>card_type collapses Finished onto the frontend's 4-value union.</summary>
    public static string CardType(this EventStatus status) => status switch
    {
        EventStatus.Finished => "ended",
        _ => status.ToWire(),
    };

    public static bool TryParse(string? token, out EventStatus status)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "drafted": status = EventStatus.Drafted; return true;
            case "upcoming": status = EventStatus.Upcoming; return true;
            case "active": status = EventStatus.Active; return true;
            case "ended": status = EventStatus.Ended; return true;
            case "finished": status = EventStatus.Finished; return true;
            default: status = EventStatus.Drafted; return false;
        }
    }
}
