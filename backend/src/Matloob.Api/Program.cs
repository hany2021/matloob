var builder = WebApplication.CreateBuilder(args);

// Liveness + readiness probes. Real checks (DB, IdM JWKS, disk) are wired in
// later commits as the corresponding dependencies are added.
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // .NET 10 enables this implicitly for minimal APIs in Development;
    // declaring it here makes intent explicit and protects against future
    // hosting-default churn.
    app.UseDeveloperExceptionPage();
}

// Liveness: "the process is up." No tag filter -> always reports Healthy
// until real checks are registered.
app.MapHealthChecks("/health");

// Readiness: "the process is up AND its dependencies respond." Currently
// identical to /health because no dependencies are wired yet; will diverge
// once DB and IdM JWKS checks are added (tagged "ready").
app.MapHealthChecks("/health/ready");

app.Run();
