using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using Matloob.Api.Features.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Persistence.Seed;
using Matloob.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
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

    // In-memory cache for read-mostly reference data (init-data, lookups).
    builder.Services.AddMemoryCache();

    // EF Core + Npgsql + audit/soft-delete interceptors + current-user abstraction.
    builder.Services.AddMatloobPersistence(builder.Configuration);

    // Local-disk file storage (v1). Future S3 driver plugs in here.
    builder.Services.AddMatloobStorage(builder.Configuration);

    // Transactional outbox for domain events. Writer is request-scoped;
    // dispatcher background service runs only when Outbox:DispatcherEnabled.
    builder.Services.AddMatloobOutbox(builder.Configuration);

    // Feature-slice handlers that orchestrate across multiple endpoints get
    // registered here. Inline handlers (most slices) need no entry.
    builder.Services
        .AddScoped<Matloob.Api.Features.Establishments.Registration.UploadDocument.LinkDocumentHandler>()
        .AddScoped<Matloob.Api.Features.Establishments.ChangeRequests.AttachDocument.AttachProposedDocumentHandler>();

    // JwtBearer validation against NEC IdentityServer. Registers the
    // authentication scheme + authorization services. No endpoint requires
    // auth yet — that arrives in the next commit.
    builder.Services.AddMatloobAuth(builder.Configuration);

    // CORS. Configured via Cors:AllowedOrigins so production can ship with
    // an empty list (same-origin / reverse-proxy) and dev can allow the
    // Angular admin at http://localhost:4200 and the public Next.js
    // frontend at http://localhost:3001.
    //
    // AllowCredentials() is required because the public frontend's axios
    // client carries `withCredentials: true` (a holdover from its Laravel
    // Sanctum days — the new API doesn't read cookies, but the browser
    // still refuses preflight if the header isn't echoed back). When
    // credentials are allowed the spec forbids `Allow-Origin: *`, which is
    // fine here: WithOrigins() always emits a specific origin echo.
    var corsAllowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? Array.Empty<string>();
    if (corsAllowedOrigins.Length > 0)
    {
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy => policy
                .WithOrigins(corsAllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("Content-Disposition"));
        });
    }

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    // Auto-migrate on startup when Database:AutoMigrate=true. The flag is
    // ON in appsettings.Development.json so `dotnet run` always brings the
    // schema up to date in dev, and OFF by default everywhere else. Set
    // Database__AutoMigrate=true on the host (or in appsettings.<env>.json)
    // to enable in other environments — single-instance staging, throwaway
    // PR previews, etc. Production typically keeps it off and applies
    // migrations from a dedicated job before the API rolls out, so two
    // replicas can't race on the migrations history table.
    //
    // Seeding runs only in Development (the test factory sets
    // UseEnvironment("Testing"), and production reference data ships via
    // the migration job).
    var autoMigrate = app.Configuration.GetValue("Database:AutoMigrate", false);
    if (autoMigrate || app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            Log.Information(
                "Applying {Count} pending migration(s): {Migrations}",
                pending.Count,
                string.Join(", ", pending));
            await db.Database.MigrateAsync();
        }

        if (app.Environment.IsDevelopment())
        {
            // Idempotent: each per-entity step skips a table that already
            // has rows, so safe to run on every launch.
            await ReferenceDataSeeder.SeedAsync(db);
        }
    }

    // One structured log line per HTTP request (method, path, status, elapsed ms).
    app.UseSerilogRequestLogging();

    // Translate non-success status codes (4xx) into ProblemDetails bodies.
    app.UseStatusCodePages();

    // Translate unhandled exceptions (5xx) into ProblemDetails bodies.
    app.UseExceptionHandler();

    // CORS runs BEFORE auth so the preflight OPTIONS request (which
    // browsers send anonymously) gets the Access-Control-Allow-Origin
    // header without first being rejected by JwtBearer.
    if (corsAllowedOrigins.Length > 0)
    {
        app.UseCors();
    }

    // Authentication / authorization run BEFORE the endpoint-routing terminal
    // middleware. No endpoint currently requires either, so anonymous traffic
    // still reaches every route. Endpoint-level policies arrive in the next commit.
    app.UseAuthentication();
    app.UseAuthorization();

    // After auth, sync the local Users row for the current principal.
    // Best-effort; never aborts the request (the service catches DB
    // failures internally + logs).
    app.UseMiddleware<Matloob.Api.Infrastructure.Identity.UserSync.CurrentUserSyncMiddleware>();

    // FastEndpoints wires routing + endpoint discovery from the assembly.
    // The custom ResponseSerializer applies the global { data } / { data, meta,
    // links } envelope shim (ResponseEnvelopeShim) so the public frontend's
    // ApiResponse<T> parser works without per-endpoint changes. DTOs marked
    // IBypassEnvelope and non–public-frontend route prefixes pass through
    // unwrapped, identical to the default serializer.
    app.UseFastEndpoints(c =>
    {
        var jsonOptions = c.Serializer.Options;
        c.Serializer.ResponseSerializer = (rsp, dto, contentType, jsonCtx, ct) =>
        {
            var payload = ResponseEnvelopeShim.Wrap(rsp.HttpContext, dto);
            rsp.ContentType = contentType;
            return payload is null
                ? Task.CompletedTask
                : JsonSerializer.SerializeAsync(rsp.Body, payload, payload.GetType(), jsonOptions, ct);
        };

        // Validation failures: return Laravel-style 422 with a snake_case
        // { message, errors } body so the frontend's laravel-precognition client
        // maps field errors correctly. (Business-rule errors that pass an
        // explicit status to Send.ErrorsAsync are unaffected.)
        c.Errors.StatusCode = StatusCodes.Status422UnprocessableEntity;
        c.Errors.ResponseBuilder = (failures, _, _) =>
            ValidationErrorResponse.FromFailures(failures);
    });

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
