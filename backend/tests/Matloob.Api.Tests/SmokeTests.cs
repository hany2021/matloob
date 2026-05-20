namespace Matloob.Api.Tests;

/// <summary>
/// Placeholder smoke tests. Confirms the test runner, the project reference
/// to Matloob.Api, and the build pipeline are wired correctly. Replaced by
/// real endpoint tests when FastEndpoints + WebApplicationFactory arrive.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void Test_runner_runs()
    {
        Assert.Equal(2, 1 + 1);
    }
}
