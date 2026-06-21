using App.Application.Abstractions;

namespace App.Infrastructure.Local.Tests;

/// <summary>Deterministic clock for tests; advance it explicitly.</summary>
public sealed class FakeClock(DateTimeOffset start) : IClock
{
    private DateTimeOffset _now = start;

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
