// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;

namespace Wonder.Application;

/// <summary>Coordinates downloads and destructive file operations within one launcher session.</summary>
public sealed class GameFileOperationCoordinator
{
    private readonly object _gate = new();
    private readonly List<Reservation> _reservations = [];

    internal IDisposable ReserveDownload(string path) => Reserve(true, [path]);
    internal IDisposable ReserveMutation(params string[] paths) => Reserve(false, paths);

    private IDisposable Reserve(bool download, string[] paths)
    {
        var normalized = paths.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(WonderPath.Comparer).ToArray();
        lock (_gate)
        {
            foreach (var existing in _reservations)
            {
                // DownloadService already serializes installs and checks conflicts between its jobs.
                if (download && existing.Download) continue;
                if (!normalized.Any(path => existing.Paths.Any(other => Overlaps(path, other)))) continue;
                throw new InvalidOperationException(existing.Download
                    ? "此目录仍被下载任务占用（含排队、暂停或失败任务）。请完成或取消相关任务，等待停止后再修改文件。"
                    : "目标目录正在移动、删除或修改资源，请操作完成后重试。");
            }
            var reservation = new Reservation(this, normalized, download);
            _reservations.Add(reservation);
            return reservation;
        }
    }

    private static bool Overlaps(string left, string right) => WonderPath.Comparer.Equals(left, right) ||
        left.StartsWith(right.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, WonderPath.Comparison) ||
        right.StartsWith(left.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, WonderPath.Comparison);

    private sealed class Reservation(GameFileOperationCoordinator owner, string[] paths, bool download) : IDisposable
    {
        public string[] Paths { get; } = paths;
        public bool Download { get; } = download;
        public void Dispose() { lock (owner._gate) owner._reservations.Remove(this); }
    }
}
