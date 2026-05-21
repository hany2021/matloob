// Makes the top-level-statement Program class accessible to
// WebApplicationFactory<Program> in the integration test project. Without this
// the generated Program class is internal and the test host cannot find it.
// Kept in its own file to avoid putting infrastructure noise at the bottom of
// the production entry point.

public partial class Program;
