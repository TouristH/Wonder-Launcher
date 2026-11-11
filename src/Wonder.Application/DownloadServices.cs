// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Wonder.Core;

namespace Wonder.Application;

public interface IDownloadEngine
{
    Task DownloadAsync(DownloadFile file, DownloadOptions options, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
    Task DownloadManyAsync(IReadOnlyList<DownloadFile> files, DownloadOptions options, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
    Task<string> GetStringAsync(string url, DownloadOptions options, CancellationToken ct = default);
}

public interface IDownloadCatalog
{
    Task<IReadOnlyList<MinecraftVersion>> GetMinecraftVersionsAsync(DownloadOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<LoaderVersion>> GetLoaderVersionsAsync(string loader, string minecraftVersion, DownloadOptions options, CancellationToken ct = default);
    Task<ResourceSearchResult> SearchResourcesAsync(DownloadResourceKind kind, string query, string? minecraftVersion, string? loader, int offset, DownloadOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<ResourceVersion>> GetResourceVersionsAsync(string projectId, string? minecraftVersion, string? loader, DownloadOptions options, CancellationToken ct = default);
    Task<string> InstallGameAsync(GameInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct);
    Task<string> InstallResourceAsync(ResourceInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct);
}
