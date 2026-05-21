namespace Matloob.Api.Infrastructure.Events;

/// <summary>
/// Configuration for the transactional outbox. Bound from the
/// <c>Outbox</c> section of appsettings.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Master switch for the dispatcher background service. Off by default
    /// so test runs + Dev hosts don't flap the table; production sets true.
    /// The writer (which inserts outbox rows in the same SaveChanges as
    /// aggregate changes) does NOT consult this flag — rows always land
    /// in the table regardless of dispatcher state, so a later boot with
    /// the dispatcher enabled drains them in order.
    /// </summary>
    public bool DispatcherEnabled { get; set; }

    /// <summary>How many pending rows the dispatcher pulls per pass.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Seconds between dispatcher passes when there's no backlog.</summary>
    public int IntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Stop attempting a row after this many failed dispatches. The row
    /// stays in the table with Error / Attempts populated for ops review.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;
}
