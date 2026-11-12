// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using System.Text;

namespace Wonder.Core;

internal static class LauncherDiagnostics
{
    private static LauncherLog? _log;

    internal static void Configure(string dataDirectory) =>
        _log = new LauncherLog(Path.Combine(dataDirectory, "logs"));

    internal static void Warn(string operation, Exception error) => _log?.Write("WARN", operation, error);

    internal static void Error(string operation, Exception error) => _log?.Write("ERROR", operation, error);
}

internal sealed class LauncherLog(string directory)
{
    internal const int MaxLogBytes = 1024 * 1024;
    private const int MaxExceptionCount = 8;
    private const int MaxTraceCharacters = 4096;
    private readonly object _gate = new();

    internal void Write(string level, string operation, Exception error)
    {
        try
        {
            // Exception messages, file paths, and Data can contain credentials and personal details.
            var entry = new StringBuilder().AppendLine($"{DateTimeOffset.UtcNow:O} [{level}] {operation}");
            var errors = new Queue<Exception>();
            errors.Enqueue(error);
            for (var count = 0; count < MaxExceptionCount && errors.TryDequeue(out var next); count++)
            {
                entry.AppendLine($"{next.GetType().FullName} (0x{next.HResult:X8})");
                var stack = new StackTrace(next, fNeedFileInfo: false).ToString();
                entry.AppendLine(stack[..Math.Min(stack.Length, MaxTraceCharacters)]);
                if (next is AggregateException aggregate)
                    foreach (var inner in aggregate.InnerExceptions.Take(MaxExceptionCount)) errors.Enqueue(inner);
                else if (next.InnerException is { } inner) errors.Enqueue(inner);
            }
            var line = entry.ToString();
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                var current = new FileInfo(Path.Combine(directory, "launcher.log"));
                if (current.Exists && current.Length + Encoding.UTF8.GetByteCount(line) > MaxLogBytes)
                    File.Move(current.FullName, Path.Combine(directory, "launcher.previous.log"), overwrite: true);
                File.AppendAllText(current.FullName, line);
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // Diagnostics are best-effort when the data directory itself cannot be written.
            Debug.WriteLine("Could not write the Wonder Launcher diagnostic log.");
        }
    }
}
