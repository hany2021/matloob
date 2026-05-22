using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Persistence.Interceptors;
using Matloob.Api.Infrastructure.Persistence.Seed;
using Matloob.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Test factory for the Opportunities + Applications + Offers slices.
/// Uses the same swap-Npgsql-for-InMemory pattern as
/// <c>EstablishmentsApiFactory</c>, including the production
/// audit + soft-delete interceptors so OAO domain rules behave the same
/// as production.
///
/// <para>
/// On first scope creation the factory seeds the reference data (cities,
/// nationalities, opportunity_categories, etc.) via
/// <see cref="ReferenceDataSeeder"/>, the same way the production
/// startup path does. Tests can rely on a populated <c>opportunity_categories</c>
/// table.
/// </para>
/// </summary>
public sealed class OpportunitiesApiFactory : MatloobApiFactory
{
    private readonly string _dbName = $"matloob-oao-tests-{Guid.NewGuid():N}";

    private readonly IServiceProvider _efServiceProvider = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    private int _seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            RemoveAllByServiceType(services, typeof(DbContextOptions<AppDbContext>));
            RemoveAllByServiceType(services, typeof(AppDbContext));
            RemoveAllByServiceType(services, typeof(IDbContextOptionsConfiguration<AppDbContext>));

            services.AddScoped(sp =>
            {
                var soft = sp.GetRequiredService<SoftDeleteInterceptor>();
                var audit = sp.GetRequiredService<AuditingInterceptor>();
                var ctx = BuildContext(soft, audit);

                // Lazy-seed on first scope only — keeps the factory cheap
                // for tests that don't need lookups.
                if (System.Threading.Interlocked.Exchange(ref _seeded, 1) == 0)
                {
                    ReferenceDataSeeder.SeedAsync(ctx).GetAwaiter().GetResult();
                }
                return ctx;
            });
        });
    }

    private AppDbContext BuildContext(
        SoftDeleteInterceptor soft,
        AuditingInterceptor audit)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInternalServiceProvider(_efServiceProvider)
            .UseInMemoryDatabase(_dbName)
            .AddInterceptors(soft, audit)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Open a scope on the factory's DI so tests can assert directly
    /// against the database after an endpoint call.
    /// </summary>
    public IServiceScope CreateDbScope() => Services.CreateScope();

    private static void RemoveAllByServiceType(IServiceCollection services, Type serviceType)
    {
        var matches = services.Where(d => d.ServiceType == serviceType).ToList();
        foreach (var m in matches)
        {
            services.Remove(m);
        }
    }
}
