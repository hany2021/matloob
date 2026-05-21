namespace Matloob.Api.Tests.Assets.Cleanup;

/// <summary>
/// Minimal controllable <see cref="TimeProvider"/> for cleanup tests.
/// Advance() jumps the clock so tests can simulate "31 days passed" without
/// actually waiting. Microsoft.Extensions.TimeProvider.Testing has a
/// fuller-featured FakeTimeProvider; this rolls our own to avoid adding the
/// dependency for two-method use.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public TestTimeProvider(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
