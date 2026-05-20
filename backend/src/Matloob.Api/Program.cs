using FastEndpoints;
using FastEndpoints.Swagger;
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

    // Liveness + readiness probes. Real checks (DB, IdM JWKS, disk) are wired in
    // later commits as the corresponding dependencies are added.
    builder.Services.AddHealthChecks();

    // FastEndpoints + OpenAPI (FastEndpoints.Swagger wraps NSwag).
    builder.Services.AddFastEndpoints();
    builder.Services.SwaggerDocument();

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

    // FastEndpoints wires routing + endpoint discovery from the assembly.
    app.UseFastEndpoints();

    if (app.Environment.IsDevelopment())
    {
        // OpenAPI JSON at  /swagger/v1/swagger.json
        // Swagger UI    at  /swagger
        app.UseSwaggerGen();
    }

    // Liveness: "the process is up." No tag filter -> always reports Healthy
    // until real checks are registered.
    app.MapHealthChecks("/health");

    // Readiness: "the process is up AND its dependencies respond." Currently
    // identical to /health because no dependencies are wired yet; will diverge
    // once DB and IdM JWKS checks are added (tagged "ready").
    app.MapHealthChecks("/health/ready");

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
