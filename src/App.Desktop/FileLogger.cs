using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace App.Desktop;

/// <summary>
/// Minimal dependency-free file logger so the desktop app records detailed errors (including Blazor
/// component exceptions, which Blazor logs via <see cref="ILogger"/> rather than crashing the process).
/// Writes timestamped lines to a per-launch file under %LOCALAPPDATA%\MappingStudio\logs.
/// </summary>
internal sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, Append);

    public void Append(string line)
    {
        lock (_gate)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    public void Dispose()
    {
    }

    private sealed class FileLogger(string category, Action<string> append) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line = $"{DateTimeOffset.Now:O} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            append(line);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
