using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Persistence.Interceptors;
using Matloob.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Test factory for the Establishments API. Same pattern as the InitData and
/// Assets factories: swap Npgsql for an EF Core InMemory provider with a
/// private internal EF service provider, so two drivers do not collide in
/// the same DI container.
///
/// Unlike the earlier factories, this one re-attaches the production
/// audit + soft-delete interceptors to the InMemory DbContext — Phase 7
/// tests assert on CreatedAt and CreatedBy, which would otherwise stay
/// default-valued.
///
/// Each factory instance owns a unique in-memory database name; tests
/// inside one fixture share the database, tests across fixtures do not.
/// </summary>
public sealed class EstablishmentsApiFactory : MatloobApiFactory
{
    private readonly string _dbName = $"matloob-establishments-tests-{Guid.NewGuid():N}";

    private readonly IServiceProvider _efServiceProvider = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Point file storage at a throwaway temp dir so any upload (e.g. event
        // media) never writes into the developer checkout.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Driver"] = "Local",
                ["Storage:AssetsRoot"] = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "matloob-establishments-tests",
                    Guid.NewGuid().ToString("N")),
            });
        });

        builder.ConfigureServices(services =>
        {
            RemoveAllByServiceType(services, typeof(DbContextOptions<AppDbContext>));
            RemoveAllByServiceType(services, typeof(AppDbContext));
            RemoveAllByServiceType(services, typeof(IDbContextOptionsConfiguration<AppDbContext>));

            // Resolve the production interceptors (registered as singletons by
            // AddMatloobPersistence) inside the scope factory so we get the
            // ICurrentUser-bound instance.
            services.AddScoped(sp =>
            {
                var soft = sp.GetRequiredService<SoftDeleteInterceptor>();
                var audit = sp.GetRequiredService<AuditingInterceptor>();
                return BuildContext(soft, audit);
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
    /// Open a scope on the factory's DI so tests can assert directly against
    /// the database after an endpoint call (e.g. "this row was inserted with
    /// Status=Draft").
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
