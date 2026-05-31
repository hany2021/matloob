using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Persistence.Interceptors;
using Matloob.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Profile;

/// <summary>
/// Test factory for the Phase-D profile mutator endpoints. Combines the two
/// patterns the existing factories use:
///   * like <c>EstablishmentsApiFactory</c> it re-attaches the production
///     audit + soft-delete interceptors to the InMemory DbContext, so deletes
///     soft-delete and the global query filter hides them (the delete tests
///     assert the row disappears from GET /profile);
///   * like <c>AssetsApiFactory</c> it points <c>Storage:AssetsRoot</c> at a
///     unique temp directory so the photo / certificate / education file
///     uploads write into throwaway storage instead of the developer checkout.
///
/// Each factory instance owns a unique in-memory database name; tests inside
/// one fixture share the database, so each test uses a distinct user sub.
/// </summary>
public sealed class ProfileMutatorsApiFactory : MatloobApiFactory, IAsyncDisposable
{
    private readonly string _dbName = $"matloob-profile-mutators-tests-{Guid.NewGuid():N}";

    public string StorageRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "matloob-profile-mutators-tests",
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
            });
        });

        builder.ConfigureServices(services =>
        {
            RemoveAllByServiceType(services, typeof(DbContextOptions<AppDbContext>));
            RemoveAllByServiceType(services, typeof(AppDbContext));
            RemoveAllByServiceType(services, typeof(IDbContextOptionsConfiguration<AppDbContext>));

            services.AddScoped(sp =>
            {
                var soft = sp.GetRequiredService<SoftDeleteInterceptor>();
                var audit = sp.GetRequiredService<AuditingInterceptor>();
                return BuildContext(soft, audit);
            });
        });
    }

    private AppDbContext BuildContext(SoftDeleteInterceptor soft, AuditingInterceptor audit)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInternalServiceProvider(_efServiceProvider)
            .UseInMemoryDatabase(_dbName)
            .AddInterceptors(soft, audit)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Open a scope on the factory's DI so tests can seed reference data and
    /// assert directly against the database.
    /// </summary>
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
            // Best-effort — test infra MUST NOT throw from teardown.
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
