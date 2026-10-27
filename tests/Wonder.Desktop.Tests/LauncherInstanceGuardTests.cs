// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class LauncherInstanceGuardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Second_process_cannot_overwrite_the_first_process_edit(bool secondProcessEdits)
    {
        using var folder = new SyntheticFolder();
        using var first = await Probe.Start(folder.Path);
        Assert.Equal("READY", first.Status);
        using var second = await Probe.Start(folder.Path);
        await first.Edit("first process latest edit");
        // With the old unguarded entry, B has loaded an older snapshot and saves it on close.
        if (second.Status == "READY")
        {
            if (secondProcessEdits) await second.Edit("second process conflicting edit");
            await second.Close();
        }
        Assert.Equal("first process latest edit", ReadTitle(folder.Path));
        Assert.Equal($"BLOCKED:{first.Process.Id}", second.Status);
        Assert.Equal(23, await second.ExitCode());
    }

    [Fact]
    public async Task Different_data_directories_allow_independent_processes()
    {
        using var folder = new SyntheticFolder();
        var firstDirectory = System.IO.Path.Combine(folder.Path, "one");
        var secondDirectory = System.IO.Path.Combine(folder.Path, "two");
        using var first = await Probe.Start(firstDirectory);
        using var second = await Probe.Start(secondDirectory);
        Assert.Equal("READY", first.Status);
        Assert.Equal("READY", second.Status);
        await first.Edit("one edit");
        await second.Edit("two edit");
        Assert.Equal("one edit", ReadTitle(firstDirectory));
        Assert.Equal("two edit", ReadTitle(secondDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normal_exit_or_process_crash_releases_the_lock(bool crash)
    {
        using var folder = new SyntheticFolder();
        using var first = await Probe.Start(folder.Path);
        Assert.Equal("READY", first.Status);
        await first.Edit("persisted before exit");
        if (crash)
        {
            first.Process.Kill(entireProcessTree: true);
            await first.ExitCode();
        }
        else await first.Close();
        using var next = await Probe.Start(folder.Path);
        Assert.Equal("READY", next.Status);
        Assert.Equal("persisted before exit", ReadTitle(folder.Path));
        Assert.True(File.Exists(System.IO.Path.Combine(folder.Path, ".instance.lock")));
    }

    [Fact]
    public async Task Case_relative_paths_and_directory_junctions_cannot_bypass_the_lock()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows filesystem alias regression.");
        using var folder = new SyntheticFolder();
        var dataDirectory = System.IO.Path.Combine(folder.Path, "data");
        using var first = await Probe.Start(dataDirectory);
        Assert.Equal("READY", first.Status);
        var junction = System.IO.Path.Combine(folder.Path, "alias");
        await CreateJunction(junction, dataDirectory);
        try
        {
            foreach (var alias in new[] { dataDirectory.ToUpperInvariant(), @".\data", junction })
            {
                using var second = await Probe.Start(alias, folder.Path);
                Assert.Equal($"BLOCKED:{first.Process.Id}", second.Status);
                Assert.Equal(23, await second.ExitCode());
            }
        }
        finally { Directory.Delete(junction); }
    }

    [Theory]
    [InlineData("directory-file")]
    [InlineData("lock-directory")]
    [InlineData("readonly-lock")]
    public async Task Unavailable_or_readonly_lock_fails_closed_without_reading_workspace(string obstacle)
    {
        using var folder = new SyntheticFolder();
        var dataDirectory = System.IO.Path.Combine(folder.Path, "data");
        var lockPath = System.IO.Path.Combine(dataDirectory, ".instance.lock");
        if (obstacle == "directory-file") File.WriteAllText(dataDirectory, "preserve");
        else
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(System.IO.Path.Combine(dataDirectory, "workspace.json"), "invalid, must never load");
            if (obstacle == "lock-directory") Directory.CreateDirectory(lockPath);
            else
            {
                File.WriteAllText(lockPath, "preserve");
                File.SetAttributes(lockPath, FileAttributes.ReadOnly);
            }
        }
        try
        {
            using var process = await Probe.Start(dataDirectory);
            Assert.StartsWith("ERROR:", process.Status);
            Assert.DoesNotContain("InvalidDataException", process.Status);
            Assert.Equal(24, await process.ExitCode());
            if (obstacle == "directory-file") Assert.Equal("preserve", File.ReadAllText(dataDirectory));
            else Assert.Equal("invalid, must never load", File.ReadAllText(System.IO.Path.Combine(dataDirectory, "workspace.json")));
        }
        finally
        {
            if (obstacle == "readonly-lock") File.SetAttributes(lockPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Unreadable_owner_metadata_still_blocks_a_second_writer()
    {
        using var folder = new SyntheticFolder();
        using var lockFile = new FileStream(System.IO.Path.Combine(folder.Path, ".instance.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var second = await Probe.Start(folder.Path);
        Assert.Equal("BLOCKED:", second.Status);
        Assert.Equal(23, await second.ExitCode());
        Assert.False(File.Exists(System.IO.Path.Combine(folder.Path, "workspace.json")));
    }

    private static string ReadTitle(string directory) => new WorkspaceStore(System.IO.Path.Combine(directory, "workspace.json")).Load().Areas.First().Title;

    private static async Task CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path '{path.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' | Out-Null");
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(process.ExitCode == 0, await error);
    }

    private sealed class Probe : IDisposable
    {
        public Process Process { get; }
        public string Status { get; private set; } = "";
        private Probe(Process process) => Process = process;

        public static async Task<Probe> Start(string directory, string? workingDirectory = null)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory
            };
            start.ArgumentList.Add(typeof(WonderTestProgram).Assembly.Location);
            start.ArgumentList.Add("--wonder-instance-probe");
            start.ArgumentList.Add(directory);
            var probe = new Probe(Process.Start(start)!);
            try
            {
                probe.Status = await probe.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15))
                    ?? throw new InvalidOperationException(await probe.Process.StandardError.ReadToEndAsync());
                return probe;
            }
            catch { probe.Dispose(); throw; }
        }

        public async Task Edit(string title)
        {
            await Process.StandardInput.WriteLineAsync($"edit:{title}");
            await Process.StandardInput.FlushAsync();
            Assert.Equal("SAVED", await Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
        }

        public async Task Close()
        {
            await Process.StandardInput.WriteLineAsync("close");
            await Process.StandardInput.FlushAsync();
            Assert.Equal(0, await ExitCode());
        }

        public async Task<int> ExitCode()
        {
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            return Process.ExitCode;
        }

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit();
            }
            Process.Dispose();
        }
    }

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Wonder.InstanceGuard.Tests", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
