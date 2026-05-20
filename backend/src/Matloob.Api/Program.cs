using FastEndpoints;
using FastEndpoints.Swagger;

var builder = WebApplication.CreateBuilder(args);

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
