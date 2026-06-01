namespace Matloob.Api.Infrastructure.StatusSync;

/// <summary>
/// Options for the time-driven status-sync sweep (config section
/// <c>StatusSync</c>). Mirrors the asset-cleanup options style: disabled by
/// default so dev/test hosts stay quiet, opt-in per environment.
/// </summary>
public sealed class StatusSyncOptions
{
    public const string SectionName = "StatusSync";

    /// <summary>
    /// Master switch. When <c>false</c> (default) the background service idles
    /// and never touches the DB. Production sets this <c>true</c> so events /
    /// opportunities / offers advance through their lifecycles by date.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Minutes between sweeps (min 1). Default 15.</summary>
    public int IntervalMinutes { get; init; } = 15;
}
