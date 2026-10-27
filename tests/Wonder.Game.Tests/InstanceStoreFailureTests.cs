// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class InstanceStoreFailureTests
{
    [Fact]
    public void Save_flushes_same_directory_temporary_file_before_replacing_catalog()
    {
        using var fixture = new ImporterFixtures();
        var path = Path.Combine(fixture.Root, "instances.json");
        new InstanceStore(path).Save(new() { OfflineUsername = "OriginalPlayer" });
        var files = new FaultingFiles();

        new InstanceStore(path, files).Save(new() { OfflineUsername = "UpdatedPlayer" });

        Assert.True(files.FlushedBeforeCommit);
        Assert.Equal(fixture.Root, Path.GetDirectoryName(files.TemporaryPath));
        Assert.Equal("UpdatedPlayer", new InstanceStore(path).Load().OfflineUsername);
        Assert.False(File.Exists(files.TemporaryPath));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("commit")]
    public void Save_failure_preserves_existing_catalog_and_removes_partial_file(string stage)
    {
        using var fixture = new ImporterFixtures();
        var path = Path.Combine(fixture.Root, "instances.json");
        new InstanceStore(path).Save(new() { OfflineUsername = "OriginalPlayer" });
        var original = File.ReadAllBytes(path);
        var files = new FaultingFiles(stage);

        var error = Assert.Throws<IOException>(() => new InstanceStore(path, files).Save(new() { OfflineUsername = "UpdatedPlayer" }));

        Assert.Same(files.SaveError, error);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal("OriginalPlayer", new InstanceStore(path).Load().OfflineUsername);
        Assert.False(File.Exists(files.TemporaryPath));
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("commit")]
    public void Cleanup_failure_preserves_primary_error_and_identifies_residual_file(string stage)
    {
        using var fixture = new ImporterFixtures();
        var path = Path.Combine(fixture.Root, "instances.json");
        new InstanceStore(path).Save(new() { OfflineUsername = "OriginalPlayer" });
        var original = File.ReadAllBytes(path);
        var files = new FaultingFiles(stage, failCleanup: true);

        var error = Assert.Throws<IOException>(() => new InstanceStore(path, files).Save(new() { OfflineUsername = "UpdatedPlayer" }));

        Assert.Same(files.SaveError, error);
        Assert.Same(files.CleanupError, error.Data["Wonder.Storage.CleanupException"]);
        Assert.Equal(files.TemporaryPath, error.Data["Wonder.Storage.TemporaryPath"]);
        Assert.True(File.Exists(files.TemporaryPath));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal("OriginalPlayer", new InstanceStore(path).Load().OfflineUsername);
    }

    [Fact]
    public void Existing_temporary_file_is_neither_overwritten_nor_deleted()
    {
        using var fixture = new ImporterFixtures();
        var path = Path.Combine(fixture.Root, "instances.json");
        var files = new FaultingFiles("collision");

        Assert.Throws<IOException>(() => new InstanceStore(path, files).Save(new()));

        Assert.Equal("unowned temporary file", File.ReadAllText(files.TemporaryPath));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Locked_destination_keeps_original_catalog_and_cleans_temporary_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new ImporterFixtures();
        var path = Path.Combine(fixture.Root, "instances.json");
        var store = new InstanceStore(path);
        store.Save(new() { OfflineUsername = "OriginalPlayer" });
        var original = File.ReadAllBytes(path);

        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => store.Save(new() { OfflineUsername = "UpdatedPlayer" }));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Save unexpectedly succeeded.");
        }

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal("OriginalPlayer", new InstanceStore(path).Load().OfflineUsername);
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData("write", false)]
    [InlineData("write", true)]
    [InlineData("flush", false)]
    [InlineData("flush", true)]
    public void Dispose_failure_does_not_replace_write_or_flush_error(string stage, bool failCleanup)
    {
        using var fixture = new ImporterFixtures();
        var path = Path.Combine(fixture.Root, "instances.json");
        new InstanceStore(path).Save(new() { OfflineUsername = "OriginalPlayer" });
        var original = File.ReadAllBytes(path);
        var files = new FaultingFiles(stage, failCleanup, failDispose: true);

        var error = Assert.Throws<IOException>(() => new InstanceStore(path, files).Save(new() { OfflineUsername = "UpdatedPlayer" }));

        Assert.Same(files.SaveError, error);
        Assert.Same(files.DisposeError, error.Data["Wonder.Storage.DisposeException"]);
        Assert.False(files.CommitAttempted);
        Assert.Equal(failCleanup, File.Exists(files.TemporaryPath));
        if (failCleanup)
        {
            Assert.Same(files.CleanupError, error.Data["Wonder.Storage.CleanupException"]);
            Assert.Equal(files.TemporaryPath, error.Data["Wonder.Storage.TemporaryPath"]);
        }
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal("OriginalPlayer", new InstanceStore(path).Load().OfflineUsername);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispose_failure_alone_prevents_commit_and_remains_primary(bool failCleanup)
    {
        using var fixture = new ImporterFixtures();
        var path = Path.Combine(fixture.Root, "instances.json");
        new InstanceStore(path).Save(new() { OfflineUsername = "OriginalPlayer" });
        var original = File.ReadAllBytes(path);
        var files = new FaultingFiles(failCleanup: failCleanup, failDispose: true);

        var error = Assert.Throws<IOException>(() => new InstanceStore(path, files).Save(new() { OfflineUsername = "UpdatedPlayer" }));

        Assert.Same(files.DisposeError, error);
        Assert.False(files.CommitAttempted);
        Assert.Equal(failCleanup, File.Exists(files.TemporaryPath));
        if (failCleanup)
        {
            Assert.Same(files.CleanupError, error.Data["Wonder.Storage.CleanupException"]);
            Assert.Equal(files.TemporaryPath, error.Data["Wonder.Storage.TemporaryPath"]);
        }
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal("OriginalPlayer", new InstanceStore(path).Load().OfflineUsername);
    }

    private sealed class FaultingFiles(string? stage = null, bool failCleanup = false, bool failDispose = false) : InstanceStore.FileOperations
    {
        public IOException SaveError { get; } = new("Injected " + stage + " failure.");
        public IOException CleanupError { get; } = new("Injected cleanup failure.");
        public IOException DisposeError { get; } = new("Injected dispose failure.");
        public string TemporaryPath { get; private set; } = "";
        public bool FlushedBeforeCommit { get; private set; }
        public bool CommitAttempted { get; private set; }
        private FaultingStream? stream;

        public override FileStream CreateTemporary(string path)
        {
            TemporaryPath = path;
            if (stage == "collision")
            {
                File.WriteAllText(path, "unowned temporary file");
                return base.CreateTemporary(path);
            }
            return stream = new FaultingStream(path, stage, SaveError, failDispose ? DisposeError : null);
        }

        public override void Commit(string temporaryPath, string targetPath)
        {
            CommitAttempted = true;
            FlushedBeforeCommit = stream!.FlushedToDisk;
            if (stage == "commit") throw SaveError;
            base.Commit(temporaryPath, targetPath);
        }

        public override void DeleteTemporary(string path)
        {
            if (failCleanup) throw CleanupError;
            base.DeleteTemporary(path);
        }
    }

    private sealed class FaultingStream(string path, string? stage, IOException error, IOException? disposeError)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    {
        public bool FlushedToDisk { get; private set; }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && disposeError is not null) throw disposeError;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (stage == "write")
            {
                base.Write(buffer[..Math.Min(buffer.Length, 8)]);
                throw error;
            }
            base.Write(buffer);
        }

        public override void Flush(bool flushToDisk)
        {
            if (stage == "flush" && flushToDisk) throw error;
            base.Flush(flushToDisk);
            FlushedToDisk |= flushToDisk;
        }
    }
}
