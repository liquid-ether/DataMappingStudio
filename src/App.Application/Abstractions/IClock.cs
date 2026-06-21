namespace App.Application.Abstractions;

/// <summary>
/// Abstraction over the system clock. All timestamps used by the change log, audit trail and
/// deterministic fold flow through this so they are testable and deterministic in tests.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default <see cref="IClock"/> backed by the real system clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
