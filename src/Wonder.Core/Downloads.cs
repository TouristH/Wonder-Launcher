// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
namespace Wonder.Core;

public enum DownloadSource { Automatic, Official, BmclApi }
public enum DownloadResourceKind { Mod, Modpack, ResourcePack, Shader }
public enum ResourceProvider { Modrinth, CurseForge }
public enum GameAddonKind { OptiFine, FabricApi, QuiltApi }
public enum DownloadTaskState { Queued, Running, Paused, Completed, Failed, Cancelled }

public sealed record DownloadOptions
{
    public DownloadSource Source { get; init; }
    public int ConcurrentFiles { get; init; } = 8;
    public int ConnectionsPerFile { get; init; } = 4;
    public int RetryCount { get; init; } = 3;
    public void Validate()
    {
        if (!Enum.IsDefined(Source) || ConcurrentFiles is < 1 or > 32 || ConnectionsPerFile is < 1 or > 16 || RetryCount is < 0 or > 8)
            throw new ArgumentException("下载源或并发参数无效。");
    }
}

public sealed record DownloadFile(string Url, string Destination, string? Hash = null, string HashAlgorithm = "SHA1", long? Size = null);
public sealed record DownloadProgress(string Stage, long BytesReceived = 0, long? TotalBytes = null, int CompletedFiles = 0, int TotalFiles = 0, double BytesPerSecond = 0,
    int StepNumber = 0, int TotalSteps = 0, string? StepName = null);
public sealed record MinecraftVersion(string Id, string Type, string Url, DateTimeOffset ReleaseTime)
{
    public string? Sha1 { get; init; }
}
public sealed record LoaderVersion(string Loader, string Version, string MinecraftVersion, bool Stable = true);
public sealed record GameAddonVersion(string Id, string Name, GameAddonKind Kind, bool Stable = true);
public sealed record ResourceProject(string Id, string Title, string Description, DownloadResourceKind Kind, string? IconUrl = null, long Downloads = 0)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ResourceProvider Provider { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Author { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PageUrl { get; init; }
}
public sealed record ResourceDependency(string? ProjectId, string? VersionId, string Type);
public sealed record ResourceVersion(string Id, string ProjectId, string Name, string VersionNumber, IReadOnlyList<string> GameVersions, IReadOnlyList<string> Loaders, IReadOnlyList<DownloadFile> Files, IReadOnlyList<ResourceDependency> Dependencies)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DownloadUnavailableReason { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ReleaseType { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? PublishedAt { get; init; }
}
public sealed record ResourceSearchResult(IReadOnlyList<ResourceProject> Projects, int Total, int Offset);
public sealed record GameInstallRequest(string MinecraftVersion, string InstanceName, string TargetDirectory, string Loader = "vanilla", string? LoaderVersion = null, string? JavaPath = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? OptiFineVersion = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? FabricApiVersion = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? QuiltApiVersion = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool InstallInCollection { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ShareGameDirectory { get; init; }
}
public sealed record ResourceInstallRequest(ResourceProject Project, ResourceVersion Version, string TargetDirectory, string MinecraftVersion = "", string Loader = "", string? InstanceName = null, string? JavaPath = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool InstallInCollection { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DownloadOnly { get; init; }
}
public sealed record DownloadTaskSnapshot(string Id, string Title, DownloadTaskState State, DownloadProgress Progress, string? Error, string? ResultPath, DateTimeOffset CreatedAt);
