using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Assets;

/// <summary>
/// Test factory for the Assets API. Same pattern as the InitData factory:
/// swap Npgsql for an EF Core InMemory provider (private internal EF service
/// provider so the two drivers don't collide), then point
/// <c>Storage:AssetsRoot</c> at a unique temp directory so each factory
/// owns its blobs.
///
/// The blob directory is best-effort deleted in <see cref="DisposeAsync"/>;
/// if a test left a file open it gets cleaned up on the next CI run by the
/// runner's workspace wipe.
/// </summary>
public sealed class AssetsApiFactory : MatloobApiFactory, IAsyncDisposable
{
    private readonly string _dbName = $"matloob-assets-tests-{Guid.NewGuid():N}";

    public string StorageRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "matloob-assets-tests",
        Guid.NewGuid().ToString("N"));

    private readonly IServiceProvider _efServiceProvider = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            // Override the storage root for this factory's lifetime so we
            // never write into the developer's checkout-level _assets/.
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Driver"] = "Local",
                ["Storage:AssetsRoot"] = StorageRoot,
                // Keep the production max-size in tests so the size-limit
                // test exercises the real path; we never actually post > 10 MB.
            });
        });

        builder.ConfigureServices(services =>
        {
            RemoveAllByServiceType(services, typeof(DbContextOptions<AppDbContext>));
            RemoveAllByServiceType(services, typeof(AppDbContext));
            RemoveAllByServiceType(services, typeof(IDbContextOptionsConfiguration<AppDbContext>));

            services.AddScoped(_ => BuildContext());
        });
    }

    private AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInternalServiceProvider(_efServiceProvider)
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new AppDbContext(options);
    }

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
