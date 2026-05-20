using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Infrastructure.Persistence;

/// <summary>
/// Composition root for persistence. Wires DbContext + Npgsql + snake_case
/// naming convention + interceptors + the current-user abstraction.
/// </summary>
public static class PersistenceRegistration
{
    public const string ConnectionStringName = "Matloob";

    public static IServiceCollection AddMatloobPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Current-user abstraction. Replaced by a JWT-driven implementation in Phase 4.
        services.AddSingleton<ICurrentUser, SystemCurrentUser>();

        // Clock injected into interceptors. TimeProvider is the standard .NET 8+
        // abstraction; do not roll our own.
        services.AddSingleton(TimeProvider.System);

        // Interceptors are singletons (stateless apart from injected services).
        services.AddSingleton<AuditingInterceptor>();
        services.AddSingleton<SoftDeleteInterceptor>();

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Missing connection string '{ConnectionStringName}'. " +
                "Set it in appsettings.Development.json or the {ConnectionStrings__Matloob} env var.");

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                // Keep migrations history table consistent with snake_case naming.
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
                // Sensible retry policy for the readiness window after Postgres restarts.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });

            // snake_case for table + column names — matches Postgres conventions
            // and the legacy Laravel column names we want to preserve.
            options.UseSnakeCaseNamingConvention();

            options.AddInterceptors(
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<AuditingInterceptor>());
        });

        return services;
    }
}
