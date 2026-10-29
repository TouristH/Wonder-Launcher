// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Wonder.Desktop;

internal sealed class LauncherInstanceGuard : IDisposable
{
    private readonly FileStream stream;

    private LauncherInstanceGuard(FileStream stream) => this.stream = stream;

    internal static bool TryAcquire(string dataDirectory, out LauncherInstanceGuard? instance, out LauncherInstanceOwner? owner)
    {
        instance = null;
        owner = null;
        var directory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, ".instance.lock");
        FileStream stream;
        try
        {
            // Unix maps any sharing permission to a shared flock; only None excludes writers.
            // Windows keeps read sharing so the second process can read the activation hint.
            stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None);
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 ||
            OperatingSystem.IsLinux() && (error.HResult & 0xffff) == 11 ||
            OperatingSystem.IsMacOS() && (error.HResult & 0xffff) == 35) // EWOULDBLOCK / EAGAIN
        {
            owner = ReadOwner(lockPath);
            return false;
        }
        try
        {
            using var process = Process.GetCurrentProcess();
            var metadata = string.Create(CultureInfo.InvariantCulture, $"{process.Id}\n{process.StartTime.ToUniversalTime().Ticks}\n");
            stream.SetLength(0);
            stream.Write(Encoding.UTF8.GetBytes(metadata));
            stream.Flush(flushToDisk: true);
            instance = new LauncherInstanceGuard(stream);
            return true;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static LauncherInstanceOwner? ReadOwner(string lockPath)
    {
        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 128) return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            if (int.TryParse(reader.ReadLine(), NumberStyles.None, CultureInfo.InvariantCulture, out var processId) && processId > 0 &&
                long.TryParse(reader.ReadLine(), NumberStyles.None, CultureInfo.InvariantCulture, out var startTime) && startTime > 0)
                return new LauncherInstanceOwner(processId, startTime);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Ownership metadata is only an activation hint. An unreadable owner never grants access.
        }
        return null;
    }

    // Keep the file after release: unlinking it would let another process lock a different file.
    public void Dispose() => stream.Dispose();
}

internal sealed record LauncherInstanceOwner(int ProcessId, long StartTimeUtcTicks);
