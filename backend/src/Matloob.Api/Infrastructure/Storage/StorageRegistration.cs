namespace Matloob.Api.Infrastructure.Storage;

/// <summary>
/// Composition root for file storage. Binds <see cref="FileStorageOptions"/>
/// from the <c>Storage</c> config section and picks an <see cref="IFileStorage"/>
/// implementation per the configured driver name.
///
/// Only <c>Local</c> is registered today; the section name + driver switch
/// exist so a future S3 / MinIO driver lands as a one-line change here, not
/// a refactor across consumers.
/// </summary>
public static class StorageRegistration
{
    public static IServiceCollection AddMatloobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(FileStorageOptions.SectionName);
        services
            .AddOptions<FileStorageOptions>()
            .Bind(section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.AssetsRoot),
                "Storage:AssetsRoot must be set.");

        // Read the driver once at startup so a typo fails fast rather than
        // surfacing as "cannot find IFileStorage" later.
        var driver = section.GetValue<string>("Driver") ?? "Local";
        switch (driver.Trim())
        {
            case "Local":
                services.AddSingleton<IFileStorage, LocalFileStorage>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Storage:Driver '{driver}' is not supported. v1 only ships 'Local'.");
        }

        return services;
    }
}
