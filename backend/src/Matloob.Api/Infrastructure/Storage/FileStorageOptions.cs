namespace Matloob.Api.Infrastructure.Storage;

/// <summary>
/// Storage configuration bound from the <c>Storage</c> section of
/// appsettings. See <see cref="StorageRegistration.AddMatloobStorage"/> for
/// how the section is wired and what defaults apply.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Which driver implementation handles new uploads. v1 ships only
    /// <c>"Local"</c>. Any other value during startup fails fast.
    /// </summary>
    public string Driver { get; set; } = "Local";

    /// <summary>
    /// Root directory for the Local driver. Either an absolute path or a
    /// path relative to the application's content root (e.g. <c>./_assets</c>).
    /// Created on first write if missing.
    /// </summary>
    public string AssetsRoot { get; set; } = "./_assets";

    /// <summary>
    /// Largest accepted upload, in bytes. Defaults to 10 MB
    /// (docs/15-establishment-onboarding-spec.md §3.3). Enforced at the
    /// upload endpoint, not in the storage driver, so a future "internal
    /// import" path can write larger blobs without bypassing storage.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>
    /// Master switch for the orphan-cleanup background service. Off by
    /// default — Dev environments don't need automatic cleanup running
    /// (test files are short-lived), and an explicit opt-in avoids
    /// surprising integration test runs by stomping their fixtures.
    /// Production sets this to true via appsettings.
    /// </summary>
    public bool CleanupEnabled { get; set; }

    /// <summary>
    /// How long an asset's bytes stay on disk after the DB row is
    /// soft-deleted. Spec §11: 30 days, giving operators a window to
    /// restore a row + bytes pair if a soft-delete was a mistake.
    /// </summary>
    public int SoftDeletedRetentionDays { get; set; } = 30;

    /// <summary>
    /// How many candidate rows the cleanup service pulls per pass.
    /// Each pass deletes its batch then waits for the next interval.
    /// 100 keeps memory + filesystem-call counts bounded; tune up if
    /// the orphan backlog gets large.
    /// </summary>
    public int CleanupBatchSize { get; set; } = 100;

    /// <summary>
    /// Minutes between cleanup passes. Once daily is plenty for a
    /// 30-day retention; 24*60 = 1440. Test fixtures override to a
    /// small value when they need to exercise the loop.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 1440;
}
