// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wonder.Core;

namespace Wonder.Application;

internal sealed record LaunchDiagnosticFile(string FullPath, string ArchiveName, DateTime LastWriteTimeUtc, long Length,
    bool Compressed, bool IsCrashReport);

public sealed class LaunchCrashReport
{
    private const int FileLimit = 128;
    private const int FileCharacterLimit = 8 * 1024 * 1024;
    private const int TotalCharacterLimit = 48 * 1024 * 1024;
    private static readonly Regex CredentialFields = new(
        """(?<prefix>--(?:accessToken|session)\b[ \t=]+|\b(?:access[_-]?token|refresh[_-]?token|client[_-]?token|api[_-]?key|password)\b["']?[ \t]*[:=][ \t]*|\bauthorization\b["']?[ \t]*[:=][ \t]*["']?(?:Bearer|Basic)[ \t]+)(?:"(?:\\.|[^"\\\r\n])*"|'(?:\\.|[^'\\\r\n])*'|[^\s,;"'{}\[\]&]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly EnumerationOptions LogEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        MaxRecursionDepth = 4
    };
    private static readonly EnumerationOptions TopLevelEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true
    };
    private static readonly HashSet<string> LegacyLogs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModLoader.txt", "MinecraftLog.txt", "output-client.log", "launcher_log.txt", "latest.log", "debug.log"
    };

    private readonly GameInstance _instance;
    private readonly DateTimeOffset _startedAt;
    private readonly DateTimeOffset _endedAt;
    private readonly LaunchPlan? _plan;
    private readonly Func<string, string> _redact;
    private readonly Stream? _transcript;
    private readonly IReadOnlyList<string> _recentLines;
    private readonly IReadOnlyList<LaunchDiagnosticFile> _files;
    private readonly object _saveGate = new();
    private string? _savedPath;

    public string InstanceName => Redact(_instance.Name);
    public int? ExitCode { get; }
    public string Reason { get; }
    public string DestinationDirectory { get; }

    internal LaunchCrashReport(GameInstance instance, string destinationDirectory, DateTimeOffset startedAt, DateTimeOffset endedAt,
        int? exitCode, string reason, LaunchPlan? plan, Func<string, string> redact, Stream? transcript,
        IReadOnlyList<string> recentLines, IReadOnlyList<LaunchDiagnosticFile> files)
    {
        _instance = instance;
        _startedAt = startedAt;
        _endedAt = endedAt;
        _plan = plan;
        _redact = redact;
        _transcript = transcript;
        _recentLines = recentLines;
        _files = files;
        ExitCode = exitCode;
        Reason = Redact(reason);
        DestinationDirectory = destinationDirectory;
    }

    internal static IReadOnlyList<LaunchDiagnosticFile> FindFiles(string? gameDirectory, DateTimeOffset since)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) return [];
        var files = new List<LaunchDiagnosticFile>();
        DirectoryInfo root;
        try { root = new DirectoryInfo(gameDirectory); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { return []; }
        Collect(root, IsRootLog, IsJvmCrash, TopLevelEnumeration);
        Collect(new DirectoryInfo(Path.Combine(root.FullName, "logs")), IsTextLog, _ => false, LogEnumeration);
        Collect(new DirectoryInfo(Path.Combine(root.FullName, "crash-reports")), IsTextLog, name => name.StartsWith("crash-", StringComparison.OrdinalIgnoreCase), TopLevelEnumeration);
        return files.OrderByDescending(file => file.IsCrashReport)
            .ThenByDescending(file => file.LastWriteTimeUtc).ToArray();

        void Collect(DirectoryInfo directory, Func<string, bool> include, Func<string, bool> isCrash, EnumerationOptions options)
        {
            try
            {
                if (!IsOrdinaryDirectory(root) || !IsOrdinaryDirectory(directory)) return;
                foreach (var file in directory.EnumerateFiles("*", options))
                {
                    if (!include(file.Name) || file.LastWriteTimeUtc < since.UtcDateTime) continue;
                    var relative = Path.GetRelativePath(root.FullName, file.FullName);
                    // ZIP readers on other hosts may interpret these characters as separators or streams.
                    if (relative.Any(c => char.IsControl(c) || c is ':' or '<' or '>' or '"' or '|' or '?' or '*') ||
                        !OperatingSystem.IsWindows() && relative.Contains('\\') ||
                        relative.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith(' ') || part.EndsWith('.'))) continue;
                    var archiveName = "game/" + relative.Replace(Path.DirectorySeparatorChar, '/');
                    var compressed = file.Name.EndsWith(".log.gz", StringComparison.OrdinalIgnoreCase);
                    files.Add(new(file.FullName, archiveName, file.LastWriteTimeUtc, file.Length, compressed, isCrash(file.Name)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LauncherDiagnostics.Warn("CollectGameDiagnostics", ex); }
        }
    }

    private static bool IsOrdinaryDirectory(DirectoryInfo directory) =>
        directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0;

    private static bool IsTextLog(string name) => name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".log.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    private static bool IsJvmCrash(string name) => name.StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".log", StringComparison.OrdinalIgnoreCase);

    private static bool IsRootLog(string name) => LegacyLogs.Contains(name) || IsJvmCrash(name) ||
        name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && (name.StartsWith("replay_pid", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("fml-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ForgeModLoader-", StringComparison.OrdinalIgnoreCase));

    public Task<string> SaveAsync() => Task.Run(() =>
    {
        lock (_saveGate) return _savedPath ??= Save();
    });

    private string Save()
    {
        Directory.CreateDirectory(DestinationDirectory);
        var date = _endedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var name = $"Wonder-crash-{date}-{Guid.NewGuid():N}.zip";
        var path = Path.Combine(DestinationDirectory, name);
        var temporary = Path.Combine(DestinationDirectory, "." + name + ".tmp");
        var created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                var notes = new List<string>();
                WriteTranscript(archive, notes);
                WriteGameFiles(archive, notes);
                WriteSummary(archive, notes);
            }
            File.Move(temporary, path);
            return path;
        }
        finally
        {
            if (created)
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LauncherDiagnostics.Warn("CleanCrashReportTemporaryFile", ex); }
        }
    }

    private void WriteTranscript(ZipArchive archive, List<string> notes)
    {
        if (_transcript is not null)
        {
            using var output = OpenEntry(archive, "launcher/launch.log");
            using var input = new StreamReader(_transcript, Encoding.UTF8, leaveOpen: true);
            _transcript.Position = 0;
            while (true)
            {
                string? line;
                try { line = input.ReadLine(); }
                catch (IOException)
                {
                    notes.Add("完整启动日志读取中断，已保留最近日志。");
                    break;
                }
                if (line is null) break;
                output.WriteLine(Redact(line));
            }
        }
        else notes.Add("完整启动日志不可用，已保留最近日志。");
        using var recent = OpenEntry(archive, "launcher/recent.log");
        foreach (var line in _recentLines) recent.WriteLine(Redact(line));
    }

    private void WriteGameFiles(ZipArchive archive, List<string> notes)
    {
        var remaining = TotalCharacterLimit;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in _files.Take(FileLimit))
        {
            if (remaining == 0)
            {
                notes.Add("已达到游戏诊断文本总量上限，其余文件已跳过。");
                break;
            }
            var name = file.Compressed ? file.ArchiveName[..^3] : file.ArchiveName;
            if (Redact(name) != name)
            {
                notes.Add("已跳过文件名含有凭据的游戏诊断文件。");
                continue;
            }
            if (!names.Add(name))
            {
                notes.Add($"{name} 与已收录文件同名，已跳过。");
                continue;
            }

            string content;
            bool truncated;
            int readCount;
            try
            {
                var info = new FileInfo(file.FullPath);
                // A file discovered at exit may be moved, rewritten or replaced while the dialog is open.
                if (info.LastWriteTimeUtc != file.LastWriteTimeUtc || info.Length != file.Length ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0 || !ParentsUnchanged(info, file.ArchiveName))
                {
                    notes.Add($"{name} 在保存前已变化，已跳过。");
                    continue;
                }
                using var source = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var decompressed = file.Compressed ? new GZipStream(source, CompressionMode.Decompress) : null;
                (content, truncated, readCount) = ReadBounded(decompressed ?? (Stream)source, Math.Min(remaining, FileCharacterLimit));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                notes.Add($"{name} 无法读取，已跳过。");
                continue;
            }
            remaining -= readCount;
            using var writer = OpenEntry(archive, name);
            writer.Write(Redact(content));
            if (truncated)
            {
                writer.WriteLine("[诊断文件超过大小上限，后续内容已省略]");
                notes.Add($"{name} 超过大小上限，仅收录开头的完整行。");
            }
        }
        if (_files.Count > FileLimit) notes.Add($"诊断文件数量超过 {FileLimit}，其余文件已跳过。");
    }

    private static bool ParentsUnchanged(FileInfo info, string archiveName)
    {
        var parent = info.Directory;
        // The synthetic game/ prefix corresponds to the actual game directory.
        foreach (var separator in archiveName.Where(c => c == '/'))
        {
            if (parent is null || !IsOrdinaryDirectory(parent)) return false;
            parent = parent.Parent;
        }
        return true;
    }

    private static (string Content, bool Truncated, int CharactersRead) ReadBounded(Stream source, int limit)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
        var content = new StringBuilder();
        Span<char> buffer = stackalloc char[4096];
        var lastCompleteLine = 0;
        while (content.Length < limit)
        {
            var count = reader.ReadBlock(buffer[..Math.Min(buffer.Length, limit - content.Length)]);
            if (count == 0) break;
            var chunk = buffer[..count];
            var lineEnd = chunk.LastIndexOf('\n');
            if (lineEnd >= 0) lastCompleteLine = content.Length + lineEnd + 1;
            content.Append(chunk);
        }
        var charactersRead = content.Length;
        var truncated = charactersRead == limit && reader.Peek() != -1;
        if (truncated) content.Length = lastCompleteLine;
        return (content.ToString(), truncated, charactersRead);
    }

    private void WriteSummary(ZipArchive archive, List<string> notes)
    {
        var settings = _instance.EffectiveSettings;
        var gameDirectory = _plan?.WorkingDirectory ?? settings.GameDirectory;
        var mods = ReadModNames(gameDirectory, notes);
        using var stream = archive.CreateEntry("summary.json", CompressionLevel.Fastest).Open();
        JsonSerializer.Serialize(stream, new
        {
            wonderVersion = LauncherBuildInfo.Version,
            startedAt = _startedAt,
            endedAt = _endedAt,
            exitCode = ExitCode,
            reason = Reason,
            instance = new
            {
                name = InstanceName,
                source = _instance.Source.ToString(),
                minecraftVersion = Redact(_instance.GameVersion),
                versionId = Redact(_instance.VersionId),
                modLoaders = _instance.ModLoaders.Select(loader => new { name = Redact(loader.Name), version = Redact(loader.Version) }),
                gameDirectory = gameDirectory is null ? null : Redact(gameDirectory),
                minimumMemoryMb = settings.MinimumMemoryMb,
                maximumMemoryMb = settings.MaximumMemoryMb,
                windowWidth = settings.WindowWidth,
                windowHeight = settings.WindowHeight
            },
            java = new
            {
                executable = (_plan?.JavaPath ?? settings.JavaPath) is { } executable ? Redact(executable) : null,
                majorVersion = _plan?.JavaMajorVersion,
                architecture = _plan?.JavaArchitecture is { } architecture ? Redact(architecture) : null
            },
            system = new
            {
                os = RuntimeInformation.OSDescription,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                runtime = RuntimeInformation.FrameworkDescription,
                processorCount = Environment.ProcessorCount
            },
            gameLogScope = "收录本次游戏进程启动后更新的诊断文本，部分大型文件可能截断。",
            mods,
            notes
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private string[] ReadModNames(string? gameDirectory, List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) return [];
        try
        {
            var root = new DirectoryInfo(gameDirectory);
            var directory = new DirectoryInfo(Path.Combine(root.FullName, "mods"));
            if (!IsOrdinaryDirectory(root) || !IsOrdinaryDirectory(directory)) return [];
            return directory.EnumerateFiles("*", TopLevelEnumeration).Select(file => file.Name)
                .Where(name => name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase).Select(Redact).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            notes.Add("无法读取模组文件名列表。");
            return [];
        }
    }

    private string Redact(string value)
    {
        var redacted = _redact(_plan?.RedactOutput(value) ?? value);
        return CredentialFields.Replace(redacted, "${prefix}[已隐藏凭据]");
    }

    private static StreamWriter OpenEntry(ZipArchive archive, string name) =>
        new(archive.CreateEntry(name, CompressionLevel.Fastest).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { NewLine = "\n" };
}
