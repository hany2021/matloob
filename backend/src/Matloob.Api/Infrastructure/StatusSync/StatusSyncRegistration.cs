namespace Matloob.Api.Infrastructure.StatusSync;

/// <summary>
/// Composition root for the time-driven status sync. Binds
/// <see cref="StatusSyncOptions"/> from the <c>StatusSync</c> section, registers
/// the scope-resolved worker, and the hosted timer (which idles unless
/// <c>StatusSync:Enabled=true</c>, so registering it unconditionally is safe).
/// </summary>
public static class StatusSyncRegistration
{
    public static IServiceCollection AddMatloobStatusSync(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<StatusSyncOptions>()
            .Bind(configuration.GetSection(StatusSyncOptions.SectionName));

        services.AddScoped<StatusSyncService>();
        services.AddHostedService<StatusSyncBackgroundService>();

        return services;
    }
}
