using FastEndpoints;
using FastEndpoints.Swagger;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

// Bootstrap logger: captures errors thrown during host construction (before
// the real Serilog pipeline is wired). Replaced on the first call to
// UseSerilog below.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Matloob API");

    var builder = WebApplication.CreateBuilder(args);

    // Replace ASP.NET Core's default ILogger pipeline with Serilog.
    // Sinks + minimum levels come from appsettings.json so verbosity can
    // change without a rebuild. Enrichers stay in code because they're
    // process-wide and never change per environment.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithProcessId()
        .Enrich.WithThreadId());

    // RFC 7807 ProblemDetails responses for all unhandled exceptions and status-code 4xx/5xx.
    builder.Services.AddProblemDetails();

    // Liveness + readiness probes.
    //   - liveness (/health):           untagged checks only
    //   - readiness (/health/ready):    checks tagged "ready" (Postgres + IdM JWKS)
    builder.Services
        .AddHealthChecks()
        .AddDbContextCheck<AppDbContext>(
            name: "postgres",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "ready" })
        .AddCheck<IdentityServerHealthCheck>(
            name: "idm-jwks",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "ready" });

    // FastEndpoints + OpenAPI (FastEndpoints.Swagger wraps NSwag).
    builder.Services.AddFastEndpoints();
    builder.Services.SwaggerDocument();

    // EF Core + Npgsql + audit/soft-delete interceptors + current-user abstraction.
    builder.Services.AddMatloobPersistence(builder.Configuration);

    // JwtBearer validation against NEC IdentityServer. Registers the
    // authentication scheme + authorization services. No endpoint requires
    // auth yet — that arrives in the next commit.
    builder.Services.AddMatloobAuth(builder.Configuration);

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    // One structured log line per HTTP request (method, path, status, elapsed ms).
    app.UseSerilogRequestLogging();

    // Translate non-success status codes (4xx) into ProblemDetails bodies.
    app.UseStatusCodePages();

    // Translate unhandled exceptions (5xx) into ProblemDetails bodies.
    app.UseExceptionHandler();

    // Authentication / authorization run BEFORE the endpoint-routing terminal
    // middleware. No endpoint currently requires either, so anonymous traffic
    // still reaches every route. Endpoint-level policies arrive in the next commit.
    app.UseAuthentication();
    app.UseAuthorization();

    // FastEndpoints wires routing + endpoint discovery from the assembly.
    app.UseFastEndpoints();

    if (app.Environment.IsDevelopment())
    {
        // OpenAPI JSON at  /swagger/v1/swagger.json
        // Swagger UI    at  /swagger
        app.UseSwaggerGen();
    }

    // Liveness: "the process is up." Filters OUT any tagged check — must stay
    // green even if dependencies (DB, IdM) are down, so orchestrators don't
    // restart a process that is itself healthy.
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = check => !check.Tags.Contains("ready"),
    });

    // Readiness: "the process is up AND its dependencies respond." Checks:
    //   - postgres   (DbContext connectivity)
    //   - idm-jwks   (OIDC discovery + JWKS reachable at Identity:Authority)
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
    });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Matloob API terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
