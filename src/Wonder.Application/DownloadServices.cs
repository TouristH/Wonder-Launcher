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
    Task<IReadOnlyList<GameAddonVersion>> GetGameAddonsAsync(GameAddonKind kind, string loader, string minecraftVersion, string? loaderVersion, DownloadOptions options, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GameAddonVersion>>([]);
    Task<ResourceSearchResult> SearchResourcesAsync(DownloadResourceKind kind, string query, string? minecraftVersion, string? loader, int offset, DownloadOptions options, CancellationToken ct = default);
    Task<ResourceSearchResult> SearchResourcesAsync(ResourceProvider provider, DownloadResourceKind kind, string query, string? minecraftVersion, string? loader, int offset, DownloadOptions options, CancellationToken ct = default) =>
        provider == ResourceProvider.Modrinth ? SearchResourcesAsync(kind, query, minecraftVersion, loader, offset, options, ct) : throw new NotSupportedException("此目录未提供所选资源平台。");
    Task<ResourceProject> GetResourceProjectAsync(ResourceProject project, DownloadOptions options, CancellationToken ct = default) => Task.FromResult(project);
    Task<IReadOnlyList<ResourceVersion>> GetResourceVersionsAsync(string projectId, string? minecraftVersion, string? loader, DownloadOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<ResourceVersion>> GetResourceVersionsAsync(ResourceProvider provider, string projectId, string? minecraftVersion, string? loader, DownloadOptions options, CancellationToken ct = default) =>
        provider == ResourceProvider.Modrinth ? GetResourceVersionsAsync(projectId, minecraftVersion, loader, options, ct) : throw new NotSupportedException("此目录未提供所选资源平台。");
    Task<string> InstallGameAsync(GameInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct);
    Task<string> InstallResourceAsync(ResourceInstallRequest request, DownloadOptions options, IProgress<DownloadProgress>? progress, CancellationToken ct);
}
