using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Persistence.Interceptors;
using Matloob.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matloob.Api.Tests.Assets.Cleanup;

/// <summary>
/// Test factory for the orphan-cleanup tests. Three differences from the
/// other test factories:
///
/// 1. <see cref="TimeProvider"/> is replaced with a controllable
///    <see cref="TestTimeProvider"/> so tests can advance "now" past the
///    retention window without actually waiting.
/// 2. <see cref="LocalFileStorage"/> stays in place (production driver),
///    pointed at a unique temp directory. The cleanup tests need real
///    files on real disk so DeleteAsync exercises the real code path.
/// 3. DbContext swaps to EF Core InMemory + the production interceptors
///    are re-attached so DeletedAt gets stamped via the test clock.
/// </summary>
public sealed class CleanupApiFactory : MatloobApiFactory, IAsyncDisposable
{
    private readonly string _dbName = $"matloob-cleanup-tests-{Guid.NewGuid():N}";

    public TestTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public string StorageRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "matloob-cleanup-tests",
        Guid.NewGuid().ToString("N"));

    private readonly IServiceProvider _efServiceProvider = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Driver"] = "Local",
                ["Storage:AssetsRoot"] = StorageRoot,
                // We don't enable the background service in tests -- the loop
                // would race the assertions. Tests resolve and call
                // AssetOrphanCleanupService directly.
                ["Storage:CleanupEnabled"] = "false",
                // Default retention is 30 days; tests override "now" via the
                // TestTimeProvider rather than the option.
                ["Storage:SoftDeletedRetentionDays"] = "30",
                ["Storage:CleanupBatchSize"] = "100",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace TimeProvider EVERYWHERE -- the interceptor and the
            // cleanup service share this singleton, so both see the same
            // "now" the tests control.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Swap DbContext to InMemory + re-attach the production
            // interceptors (same pattern as the Establishments factory).
            RemoveAllByServiceType(services, typeof(DbContextOptions<AppDbContext>));
            RemoveAllByServiceType(services, typeof(AppDbContext));
            RemoveAllByServiceType(services, typeof(IDbContextOptionsConfiguration<AppDbContext>));

            services.AddScoped(sp =>
            {
                var soft = sp.GetRequiredService<SoftDeleteInterceptor>();
                var audit = sp.GetRequiredService<AuditingInterceptor>();
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInternalServiceProvider(_efServiceProvider)
                    .UseInMemoryDatabase(_dbName)
                    .AddInterceptors(soft, audit)
                    .Options;
                return new AppDbContext(options);
            });
        });
    }

    public IServiceScope CreateDbScope() => Services.CreateScope();

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort -- test infra MUST NOT throw from teardown.
        }
    }

    private static void RemoveAllByServiceType(IServiceCollection services, Type serviceType)
    {
        var matches = services.Where(d => d.ServiceType == serviceType).ToList();
        foreach (var m in matches)
        {
            services.Remove(m);
        }
    }
}
