using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Persistence.Seed;
using Matloob.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Reference;

/// <summary>
/// Specialization of <see cref="MatloobApiFactory"/> for init-data tests.
/// Replaces the production Npgsql DbContext with an in-memory provider so the
/// test host needs no running Postgres, then runs
/// <see cref="ReferenceDataSeeder"/> against the in-memory database once per
/// factory lifetime.
///
/// Sharing this factory across the suite via <see cref="IClassFixture{T}"/>
/// re-uses the same seeded database — both faster and a guarantee that the
/// seeder is idempotent (every test triggers a redundant seed call).
/// </summary>
public sealed class InitDataApiFactory : MatloobApiFactory
{
    // Stable, instance-scoped name so the same in-memory store survives across
    // every scope created off this factory. A Guid prevents cross-factory
    // pollution if a second InitDataApiFactory ever exists in the run.
    private readonly string _dbName = $"matloob-init-data-tests-{Guid.NewGuid():N}";

    // Private internal EF service provider — required because the production
    // DI graph already has Npgsql's IDatabaseProvider in it. UseInMemoryDatabase
    // would otherwise collide with "two providers in one container".
    private readonly IServiceProvider _efServiceProvider = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Strip every registration the production AddDbContext path made
            // for AppDbContext. RemoveAll covers the generic DbContextOptions,
            // the DbContext itself, and EF's per-context option configurations.
            RemoveAllByServiceType(services, typeof(DbContextOptions<AppDbContext>));
            RemoveAllByServiceType(services, typeof(AppDbContext));
            RemoveAllByServiceType(services, typeof(IDbContextOptionsConfiguration<AppDbContext>));

            // Hand-roll the DbContext registration so EF doesn't try to graft
            // the InMemory provider onto the same DI graph that still carries
            // Npgsql's IDatabaseProvider.
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

    /// <summary>
    /// Lazily build the host and seed once. Safe to call repeatedly — the
    /// seeder short-circuits on AnyAsync, so subsequent calls are no-ops.
    /// </summary>
    public async Task EnsureSeededAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ReferenceDataSeeder.SeedAsync(db);
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
