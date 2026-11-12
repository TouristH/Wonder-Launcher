// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Buffers;
using System.IO.Compression;

namespace Wonder.Infrastructure;

internal static class ArchiveEntryContent
{
    public static async Task<byte[]> ReadAsync(ZipArchiveEntry entry, int maximumBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var length = entry.Length;
        if (length < 0 || length > maximumBytes) throw new InvalidDataException("归档条目超过读取限制。");
        var bytes = new byte[(int)length];
        using var input = entry.Open();
        using var output = new MemoryStream(bytes, writable: true);
        await CopyExactlyAsync(input, output, length, ct).ConfigureAwait(false);
        return bytes;
    }

    public static async Task ExtractAsync(ZipArchiveEntry entry, string destination, CancellationToken ct, bool preserveFileMetadata = false)
    {
        ct.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".wonder-extract-" + Path.GetRandomFileName());
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
        var permissions = (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x1ff);
        if (preserveFileMetadata && !OperatingSystem.IsWindows() && permissions != UnixFileMode.None)
            options.UnixCreateMode = permissions;
        var output = new FileStream(temporary, options);
        try
        {
            await using (output.ConfigureAwait(false))
            {
                using var input = entry.Open();
                await CopyExactlyAsync(input, output, entry.Length, ct).ConfigureAwait(false);
            }
            if (preserveFileMetadata)
            {
                try { File.SetLastWriteTimeUtc(temporary, entry.LastWriteTime.UtcDateTime); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException) { }
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static async Task CopyExactlyAsync(Stream input, Stream output, long length, CancellationToken ct)
    {
        if (length < 0) throw new InvalidDataException("归档条目大小无效。");
        var remaining = length;
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var available = remaining < buffer.Length ? (int)remaining + 1 : buffer.Length;
                var read = await input.ReadAsync(buffer.AsMemory(0, available), ct).ConfigureAwait(false);
                if (read > remaining) throw new InvalidDataException("归档条目的实际大小超出清单。");
                if (read == 0) break;
                remaining -= read;
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            if (remaining != 0) throw new InvalidDataException("归档条目不完整。");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
