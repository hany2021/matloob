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
}
