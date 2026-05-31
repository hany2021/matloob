namespace Matloob.Domain.Events;

/// <summary>
/// Join row binding an <see cref="Event"/> to an
/// <see cref="Matloob.Domain.Reference.OpportunityCategory"/> (legacy
/// <c>event_opportunity_category</c> pivot). Replaced wholesale when the event's
/// categories are updated.
/// </summary>
public sealed class EventOpportunityCategory
{
    public Guid EventId { get; private set; }
    public Guid OpportunityCategoryId { get; private set; }

    private EventOpportunityCategory() { }

    public EventOpportunityCategory(Guid eventId, Guid opportunityCategoryId)
    {
        EventId = eventId;
        OpportunityCategoryId = opportunityCategoryId;
    }
}
