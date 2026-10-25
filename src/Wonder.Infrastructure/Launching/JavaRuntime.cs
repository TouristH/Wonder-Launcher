// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;

namespace Wonder.Infrastructure;

public sealed record JavaRuntime(string Executable, int MajorVersion, string Architecture)
{
    internal static async Task<JavaRuntime> Probe(string requested, CancellationToken ct)
    {
        var executable = requested;
        if (Path.GetFileName(executable).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
        {
            var console = Path.Combine(Path.GetDirectoryName(executable) ?? "", "java.exe");
            if (File.Exists(console)) executable = console;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process { StartInfo = StartInfo(executable, null, ["-XshowSettings:properties", "-version"]) };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); throw; }
            var text = await output + "\n" + await error;
            if (process.ExitCode != 0) throw new InvalidDataException("Java 检查失败：" + text[..Math.Min(text.Length, 1000)]);
            var version = Regex.Match(text, @"java\.version\s*=\s*(?:1\.)?(\d+)");
            var arch = Regex.Match(text, @"os\.arch\s*=\s*(\S+)");
            if (!version.Success || !arch.Success) throw new InvalidDataException("无法读取 Java 版本和架构。请指定有效的 java.exe。");
            return new(executable, int.Parse(version.Groups[1].Value), arch.Groups[1].Value);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new InvalidDataException("Java 检查超时，请检查 Java 路径。"); }
        catch (System.ComponentModel.Win32Exception ex) { throw new InvalidDataException("无法运行 Java，请在实例设置中指定 java.exe。" + ex.Message, ex); }
    }
    internal static ProcessStartInfo StartInfo(string executable, string? directory, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        if (directory is not null) info.WorkingDirectory = directory;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }
}
