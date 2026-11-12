// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Extensibility;

public record PluginManifest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public int ApiVersion { get; init; }
    public string EntryAssembly { get; init; } = "";
    public string EntryType { get; init; } = "";
    public PluginPermission[]? Permissions { get; init; }
}

public sealed record PluginPermission
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed record PluginConsentRequest(PluginManifest Manifest, string Fingerprint, PluginAnalysis Analysis);

public sealed record PluginCatalogEntry : PluginManifest
{
    public string DownloadUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
    public string SourceUrl { get; init; } = "";
    public PluginReview? Review { get; init; }
}

public sealed record PluginReview
{
    public string Status { get; init; } = "";
    public string Reviewer { get; init; } = "";
    public DateTimeOffset ReviewedAt { get; init; }
    public string Sha256 { get; init; } = "";
}

public sealed record InstalledPlugin(PluginManifest Manifest, string Directory, bool Enabled, string Status,
    string? Error, IReadOnlyList<string> Messages);
