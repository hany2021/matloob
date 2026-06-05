namespace Matloob.Domain.Opportunities;

/// <summary>
/// Canonical outbox <c>event_type</c> string constants for the Opportunity
/// aggregate. Wire these into <c>IOutboxWriter.Enqueue</c> when a
/// transition happens; downstream subscribers key off the constant value,
/// never the C# symbol — DO NOT rename without coordinating a schema
/// migration.
/// </summary>
public static class OpportunityEventTypes
{
    public const string Created    = "opportunity.created";
    public const string Updated    = "opportunity.updated";
    public const string Ended      = "opportunity.ended";
    public const string Deleted    = "opportunity.deleted";
    public const string Fulfilled  = "opportunity.fulfilled";

    /// <summary>
    /// Time-driven: the opportunity's end date passed without it being
    /// filled (status sync MarkFinished). Notifies its applicants (legacy
    /// <c>OpportunityExpiredNotification</c>).
    /// </summary>
    public const string Expired    = "opportunity.expired";
}
