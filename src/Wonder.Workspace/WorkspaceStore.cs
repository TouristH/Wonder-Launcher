// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Wonder.Workspace;

public sealed class WorkspaceStore
{
    private const int MaximumFileBytes = 2 * 1024 * 1024;
    private readonly string path;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = 64,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public WorkspaceStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public WorkspaceDocument Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumFileBytes)
                throw Invalid("文件超过 2 MiB 限制。");
            var json = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { MaxDepth = 64, AllowDuplicateProperties = false }) as JsonObject
                ?? throw Invalid("内容为空。");
            if (json["version"] is not JsonValue versionValue || !versionValue.TryGetValue<int>(out var version) || version is not (1 or 2 or 3))
                throw Invalid("不支持此文件版本。");
            if (version is 1 or 2)
                MigrateLegacyComponents(json);
            var document = json.Deserialize<WorkspaceDocument>(JsonOptions) ?? throw Invalid("内容为空。");
            Validate(document);
            if (version is 1 or 2)
            {
                document.Library.RemoveAll(item => IsPreviewSample(item.DefinitionId));
                foreach (var area in document.Areas)
                    area.Components.RemoveAll(item => IsPreviewSample(item.DefinitionId));
            }
            document.EnsureLibrary();
            return document;
        }
        catch (FileNotFoundException) { return WorkspaceDocument.CreateDefault(); }
        catch (DirectoryNotFoundException) { return WorkspaceDocument.CreateDefault(); }
        catch (JsonException error) { throw new InvalidDataException("工作区文件格式无效，原文件已保留。", error); }
    }

    public void Save(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version == 1)
            throw Invalid("请先完成旧版工作区坐标转换，再保存工作区。");
        Validate(document);
        document.EnsureLibrary();
        Validate(document);
        var savedDocument = new WorkspaceDocument { Version = 3, Root = document.Root, Library = document.Library };
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, savedDocument, JsonOptions);
                if (stream.Length > MaximumFileBytes)
                    throw Invalid("文件超过 2 MiB 限制。");
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, path);
            document.Version = 3;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void Validate(WorkspaceDocument document)
    {
        if (document.Version is not (1 or 2 or 3))
            throw Invalid("不支持此文件版本。");

        var identities = new HashSet<string>(StringComparer.Ordinal);
        var applications = new Dictionary<string, ComponentItem>(StringComparer.Ordinal);
        var areaCount = 0;
        var componentCount = 0;
        if (document.Library is null || document.Library.Count > 1024)
            throw Invalid("组件库无效或应用数量超出限制。");
        foreach (var application in document.Library)
        {
            if (application is null || application.ApplicationId is not null || !ReferenceEquals(application.Application, application) ||
                !ComponentIds.IsValid(application.DefinitionId) || !application.IsStateValid() ||
                !ValidPosition(application.X, document.Version) || !ValidPosition(application.Y, document.Version))
                throw Invalid("组件库应用的种类、坐标或引用无效。");
            Identity(application.Id);
            applications.Add(application.Id, application);
        }
        if (document.Root is not null)
            Visit(document.Root, 0);

        void Identity(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || !identities.Add(value))
                throw Invalid("标识为空、过长或重复。");
        }

        void Visit(LayoutNode node, int depth)
        {
            if (depth > 24)
                throw Invalid("分区嵌套超过 24 层限制。");
            Identity(node.Id);
            if (!Enum.IsDefined(node.Axis) || !double.IsFinite(node.Ratio) || node.Ratio < 0 || node.Ratio > 1)
                throw Invalid("分割方向或比例无效。");

            if (node.Area is null)
            {
                if (node.First is null || node.Second is null)
                    throw Invalid("分区缺少子节点。");
                Visit(node.First, depth + 1);
                Visit(node.Second, depth + 1);
                return;
            }

            if (node.First is not null || node.Second is not null || ++areaCount > 128)
                throw Invalid("叶区域包含子分区或区域数量超过 128。");
            var area = node.Area;
            Identity(area.Id);
            if (!Enum.IsDefined(area.SidebarEdge) || !double.IsFinite(area.SidebarExtent) || area.SidebarExtent is < 80 or > 1_000_000)
                throw Invalid("侧边栏方向或展开尺寸无效。");
            if (area.Title is null || area.Title.Length > 120 || area.Components is null || area.Components.Count > 64)
                throw Invalid("区域标题或组件列表无效。");
            foreach (var component in area.Components)
            {
                if (component is null || ++componentCount > 1024)
                    throw Invalid("组件为空或总数超过 1024。");
                Identity(component.Id);
                if (!ComponentIds.IsValid(component.DefinitionId) || component.HasLocalState ||
                    !ValidPosition(component.X, document.Version) || !ValidPosition(component.Y, document.Version))
                    throw Invalid("组件种类或坐标无效。");
                if (component.ApplicationId is not null &&
                    (!applications.TryGetValue(component.ApplicationId, out var application) || application.DefinitionId != component.DefinitionId))
                    throw Invalid("快捷方式引用了不存在或不匹配的应用。");
            }
        }
    }

    private static bool ValidPosition(double value, int version) =>
        double.IsFinite(value) && value >= (version == 1 ? 0 : -1_000_000) && value <= 1_000_000;
    // Numeric kinds only belong to the early Wonder previews. Transform their schema before
    // normal validation, then remove samples only after references and layout have passed it.
    private static void MigrateLegacyComponents(JsonObject document)
    {
        ConvertComponents(document["library"], uniqueKinds: true);
        Visit(document["root"]);

        static void Visit(JsonNode? node)
        {
            if (node is not JsonObject layout) return;
            if (layout["area"] is JsonObject area) ConvertComponents(area["components"]);
            Visit(layout["first"]);
            Visit(layout["second"]);
        }

        static void ConvertComponents(JsonNode? node, bool uniqueKinds = false)
        {
            if (node is not JsonArray components) return;
            var seenKinds = new HashSet<int>();
            foreach (var item in components)
            {
                if (item is not JsonObject component) continue;
                if (component.ContainsKey("definitionId") || component.ContainsKey("state") ||
                    component["kind"] is not JsonValue kindValue || !kindValue.TryGetValue<int>(out var kind) || kind is < 0 or > 3)
                    throw Invalid("旧版组件种类无效。");
                if (uniqueKinds && !seenKinds.Add(kind))
                    throw Invalid("旧版组件库包含重复的组件种类。");
                if (component["value"] is { } counter && (counter is not JsonValue value || !value.TryGetValue<int>(out _)))
                    throw Invalid("旧版预览组件的计数值无效。");
                component.Remove("kind");
                component.Remove("value");
                component["definitionId"] = kind == 3 ? ComponentIds.Settings : $"wonder.legacy-preview.{kind}";
            }
        }
    }

    private static bool IsPreviewSample(string definitionId) => definitionId is "wonder.legacy-preview.0" or "wonder.legacy-preview.1" or "wonder.legacy-preview.2";
    private static InvalidDataException Invalid(string detail) => new($"工作区数据无效：{detail}");
}
