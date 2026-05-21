using Xunit;

// Serialize all tests in this assembly.
//
// WebApplicationFactory<Program> builds the full host, which sets the static
// Serilog.Log.Logger to a "reloadable bootstrap logger" that is frozen on
// first IHostBuilder.UseSerilog call. A second concurrent host build calls
// Freeze() on the already-frozen logger and throws:
//   System.InvalidOperationException : The logger is already frozen.
//
// xUnit's default behavior is to run test CLASSES in parallel within an
// assembly. Disabling that parallelization makes the host builds serial and
// dodges the issue without changing the production Program.cs flow.
//
// Tests within a single class already run serially by default — this attribute
// only affects cross-class parallelism.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
