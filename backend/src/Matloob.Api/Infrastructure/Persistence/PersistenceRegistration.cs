using Matloob.Api.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Infrastructure.Persistence;

/// <summary>
/// Composition root for persistence. Wires DbContext + Npgsql + snake_case
/// naming convention + interceptors.
///
/// Depends on <c>ICurrentUser</c> being registered elsewhere (today by
/// <c>AddMatloobAuth</c> in Infrastructure/Auth). The auditing interceptor
/// resolves it lazily so the registration order in Program.cs does not matter
/// at runtime.
/// </summary>
public static class PersistenceRegistration
{
    public const string ConnectionStringName = "Matloob";

    public static IServiceCollection AddMatloobPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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
