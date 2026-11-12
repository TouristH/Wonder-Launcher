// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text;
using Wonder.Core;

namespace Wonder.Application;

// Keep the launch output separate from the UI's rolling buffer.
internal sealed class LaunchTranscript : IDisposable
{
    private const long Limit = 32L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly FileStream? _stream;
    private readonly StreamWriter? _writer;
    private long _bytes;
    private bool _truncated, _failed;

    public LaunchTranscript()
    {
        try
        {
            var options = new FileStreamOptions
            {
                Access = FileAccess.ReadWrite,
                Mode = FileMode.CreateNew,
                Share = FileShare.None,
                Options = FileOptions.DeleteOnClose | FileOptions.SequentialScan
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            _stream = new FileStream(Path.Combine(Path.GetTempPath(), $"Wonder-launch-{Guid.NewGuid():N}.tmp"), options);
            _writer = new StreamWriter(_stream, new UTF8Encoding(false), bufferSize: 16 * 1024, leaveOpen: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _failed = true;
            LauncherDiagnostics.Warn("OpenLaunchTranscript", ex);
        }
    }

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            if (_failed || _truncated) return;
            _bytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            try
            {
                if (_bytes <= Limit) _writer!.WriteLine(line);
                else
                {
                    _truncated = true;
                    _writer!.WriteLine("[Wonder] 启动记录超过 32 MiB，后续输出请查看归档中的最近日志和游戏日志。");
                }
            }
            catch (IOException error)
            {
                _failed = true;
                LauncherDiagnostics.Warn("WriteLaunchTranscript", error);
            }
        }
    }

    public Stream? Finish()
    {
        if (_failed) return null;
        try
        {
            _writer!.Flush();
            _stream!.Position = 0;
            return _stream;
        }
        catch (IOException error)
        {
            LauncherDiagnostics.Warn("FlushLaunchTranscript", error);
            return null;
        }
    }

    public void Dispose()
    {
        try { _writer?.Dispose(); } catch (IOException error) { LauncherDiagnostics.Warn("CloseLaunchTranscriptWriter", error); }
        try { _stream?.Dispose(); } catch (IOException error) { LauncherDiagnostics.Warn("CloseLaunchTranscriptStream", error); }
    }
}
