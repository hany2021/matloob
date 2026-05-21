using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Auth;

/// <summary>
/// WebApplicationFactory that replaces JwtBearer with <see cref="TestAuthHandler"/>
/// and supplies minimal in-memory configuration so the host boots without a
/// real PostgreSQL / IdentityServer.
///
/// IMPORTANT: this factory does NOT exercise /health/ready in tests. Readiness
/// checks (DbContextCheck, IdM JWKS) would touch real dependencies; they live
/// in the wire-up but are simply not hit by these auth tests.
/// </summary>
public sealed class MatloobApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Setting environment to "Testing" disables the Development-only Swagger
        // UI + DeveloperExceptionPage; suits an integration-test host.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            // Provide all the config the production registrations require so
            // the host builds — never connected to.
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // PersistenceRegistration.AddMatloobPersistence throws if this is missing.
                // The DbContext is registered but never opens a connection in these tests.
                ["ConnectionStrings:Matloob"] =
                    "Host=test;Port=5432;Database=matloob_test;Username=test;Password=test",

                ["Identity:Authority"] = "https://test.local",
                ["Identity:Issuer"] = "https://test.local",
                ["Identity:Audience"] = "matloob:api",
                ["Identity:AdminAudience"] = "matloob:admin",
                ["Identity:RequireHttpsMetadata"] = "false",
                ["Identity:RoleClaimType"] = "role",
                ["Identity:NameClaimType"] = "sub",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Flip the default scheme from JwtBearer to Test. JwtBearer is still
            // registered (production AuthRegistration added it), but the
            // default scheme is what UseAuthentication invokes — and the policies
            // call DefaultAuthenticateScheme by default, so the swap is enough.
            services.Configure<AuthenticationOptions>(opts =>
            {
                opts.DefaultScheme = TestAuthHandler.SchemeName;
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            services
                .AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ => { });
        });
    }

    /// <summary>
    /// Convenience: create an HttpClient that sends an X-Test-User header
    /// representing the supplied user. Pass <c>null</c> for an anonymous client.
    /// </summary>
    public HttpClient CreateClientFor(TestUser? user)
    {
        var client = CreateClient();

        if (user is not null)
        {
            var json = JsonSerializer.Serialize(user);
            client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, json);

            // Set an Authorization header too so callers/middleware that sniff
            // for "is this anonymous?" by the presence of Authorization see
            // a non-empty value. The value is not validated — TestAuthHandler
            // reads the X-Test-User header, not Authorization.
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "test-stub");
        }

        return client;
    }
}
