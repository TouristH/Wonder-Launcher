// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json.Serialization;

namespace Wonder.Workspace;

public enum SplitAxis { Horizontal, Vertical }
public enum SidebarEdge { None, Left, Right, Top, Bottom }

public sealed class ComponentItem
{
    private ComponentItem? application;
    private Dictionary<string, string> state = new(StringComparer.Ordinal);

    [JsonRequired] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonRequired] public string DefinitionId { get; set; } = string.Empty;
    public string? ApplicationId { get; set; }
    [JsonIgnore] public ComponentItem Application => application ?? this;
    [JsonIgnore] public Dictionary<string, string> State
    {
        get => application?.State ?? state;
        set
        {
            if (application is null) state = value;
            else application.State = value;
        }
    }
    [JsonInclude, JsonPropertyName("state"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal Dictionary<string, string>? PersistedState
    {
        get => ApplicationId is null ? state : null;
        set { state = value!; HasPersistedState = true; }
    }
    [JsonIgnore] internal bool HasPersistedState { get; private set; }
    [JsonIgnore] internal bool HasLocalState => HasPersistedState || state is null || state.Count != 0;
    internal bool IsStateValid() => state is { Count: <= 64 } && state.All(pair =>
        !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 80 && pair.Value is { Length: <= 65_536 });
    /// <summary>In v2 and later, the component center's horizontal pixel offset from the entire region center.</summary>
    public double X { get; set; }
    /// <summary>In v2 and later, the component center's vertical pixel offset from the entire region center.</summary>
    public double Y { get; set; }

    internal void BindApplication(ComponentItem source)
    {
        if (ReferenceEquals(this, source) || source.ApplicationId is not null || !ReferenceEquals(source.Application, source) ||
            DefinitionId != source.DefinitionId || HasLocalState)
            throw new InvalidDataException("工作区应用引用不能指向自身或其他快捷方式。");
        application = source;
        ApplicationId = source.Id;
    }
}

public sealed class Area
{
    [JsonRequired] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "区域";
    public SidebarEdge SidebarEdge { get; set; }
    public double SidebarExtent { get; set; } = WorkspaceLayout.DefaultSidebarExtent;
    [JsonRequired] public List<ComponentItem> Components { get; set; } = [];
}

public sealed class LayoutNode
{
    [JsonRequired] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Area? Area { get; set; }
    public LayoutNode? First { get; set; }
    public LayoutNode? Second { get; set; }
    public SplitAxis Axis { get; set; }
    public double Ratio { get; set; } = .5;
}

public sealed class WorkspaceDocument
{
    [JsonRequired] public int Version { get; set; } = 3;
    [JsonRequired] public LayoutNode? Root { get; set; }
    public List<ComponentItem> Library { get; set; } = [];
    [JsonIgnore] public IEnumerable<Area> Areas => Nodes(Root).Where(node => node.Area is not null).Select(node => node.Area!);
    public static WorkspaceDocument CreateEmpty()
    {
        var document = new WorkspaceDocument();
        document.EnsureLibrary();
        return document;
    }

    /// <summary>Restores shared application links and adds missing applications for the requested definitions.</summary>
    public void EnsureLibrary(IEnumerable<string>? definitionIds = null)
    {
        if (Library is null)
            throw new InvalidDataException("工作区组件库不能为空。");
        var byId = new Dictionary<string, ComponentItem>(StringComparer.Ordinal);
        var byDefinition = new Dictionary<string, ComponentItem>(StringComparer.Ordinal);
        foreach (var source in Library)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Id) || source.Id.Length > 80 ||
                !ComponentIds.IsValid(source.DefinitionId) || !source.IsStateValid() ||
                source.ApplicationId is not null || !ReferenceEquals(source.Application, source) || !byId.TryAdd(source.Id, source))
                throw new InvalidDataException("工作区组件库的应用标识、种类或引用无效。");
            byDefinition.TryAdd(source.DefinitionId, source);
        }

        var shortcuts = Areas.SelectMany(area => area.Components).ToArray();
        foreach (var shortcut in shortcuts)
        {
            if (shortcut is null || string.IsNullOrWhiteSpace(shortcut.Id) || shortcut.Id.Length > 80 ||
                !ComponentIds.IsValid(shortcut.DefinitionId) || shortcut.HasLocalState || !byId.TryAdd(shortcut.Id, shortcut))
                throw new InvalidDataException("工作区快捷方式的标识、定义或状态无效。");
            if (shortcut.ApplicationId is not null &&
                (!byId.TryGetValue(shortcut.ApplicationId, out var source) || !Library.Contains(source) ||
                 source.DefinitionId != shortcut.DefinitionId || ReferenceEquals(source, shortcut)))
                throw new InvalidDataException("工作区快捷方式引用了不存在或不匹配的应用。");
        }

        var requestedDefinitions = (definitionIds ?? [ComponentIds.Settings]).Concat(shortcuts.Select(item => item.DefinitionId)).Distinct(StringComparer.Ordinal).ToArray();
        if (requestedDefinitions.Any(id => !ComponentIds.IsValid(id)))
            throw new InvalidDataException("工作区组件定义标识无效。");
        foreach (var definitionId in requestedDefinitions)
        {
            if (byDefinition.ContainsKey(definitionId))
                continue;
            var source = new ComponentItem { DefinitionId = definitionId };
            Library.Add(source);
            byDefinition.Add(definitionId, source);
            byId.Add(source.Id, source);
        }

        foreach (var shortcut in shortcuts)
        {
            var source = byDefinition[shortcut.DefinitionId];
            shortcut.BindApplication(shortcut.ApplicationId is null ? source : byId[shortcut.ApplicationId]);
        }
    }

    public ComponentItem CreateApplication(string definitionId)
    {
        if (!ComponentIds.IsValid(definitionId))
            throw new ArgumentException("组件定义标识无效。", nameof(definitionId));
        var application = new ComponentItem { DefinitionId = definitionId };
        Library.Add(application);
        return application;
    }

    public ComponentItem CreateShortcut(ComponentItem application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!Library.Contains(application) || application.ApplicationId is not null || !ReferenceEquals(application.Application, application))
            throw new ArgumentException("只能为当前组件库中的应用创建快捷方式。", nameof(application));
        var shortcut = new ComponentItem { DefinitionId = application.DefinitionId };
        shortcut.BindApplication(application);
        return shortcut;
    }

    public static WorkspaceDocument CreateDefault()
    {
        var document = CreateEmpty();
        var area = document.AddArea();
        area.Title = "工作区";
        area.Components.Add(document.CreateShortcut(document.Library.Single(item => item.DefinitionId == ComponentIds.Settings)));
        return document;
    }

    public Area AddArea(string? besideAreaId = null, SplitAxis axis = SplitAxis.Horizontal)
    {
        if (!Enum.IsDefined(axis))
            throw new ArgumentOutOfRangeException(nameof(axis));

        var area = new Area { Title = $"区域 {Areas.Count() + 1}" };
        if (Root is null)
        {
            Root = new LayoutNode { Area = area };
            return area;
        }

        var leaf = Nodes(Root).FirstOrDefault(node => node.Area is not null && (besideAreaId is null || node.Area.Id == besideAreaId))
            ?? throw new ArgumentException("找不到要分割的区域。", nameof(besideAreaId));
        leaf.Area!.SidebarEdge = SidebarEdge.None;
        leaf.First = new LayoutNode { Area = leaf.Area };
        leaf.Second = new LayoutNode { Area = area };
        leaf.Area = null;
        leaf.Axis = axis;
        leaf.Ratio = .5;
        return area;
    }

    public bool RemoveArea(string id)
    {
        if (FindArea(id) is null)
            return false;

        Root = Remove(Root, id);
        NormalizeSidebars(Root, null, (true, true, true, true));
        return true;
    }

    public bool SwapAreas(string firstId, string secondId)
    {
        if (firstId == secondId)
            return false;

        var first = Nodes(Root).FirstOrDefault(node => node.Area?.Id == firstId);
        var second = Nodes(Root).FirstOrDefault(node => node.Area?.Id == secondId);
        if (first is null || second is null)
            return false;

        if (first.Area!.SidebarEdge != SidebarEdge.None || second.Area!.SidebarEdge != SidebarEdge.None)
            first.Area.SidebarEdge = second.Area!.SidebarEdge = SidebarEdge.None;
        (first.Area, second.Area) = (second.Area, first.Area);
        return true;
    }

    public Area? FindArea(string id) => Areas.FirstOrDefault(area => area.Id == id);
    public LayoutNode? FindNode(string id) => Nodes(Root).FirstOrDefault(node => node.Id == id);

    private static LayoutNode? Remove(LayoutNode? node, string areaId)
    {
        if (node is null || node.Area?.Id == areaId)
            return null;
        if (node.Area is not null)
            return node;

        node.First = Remove(node.First, areaId);
        node.Second = Remove(node.Second, areaId);
        return node.First is null ? node.Second : node.Second is null ? node.First : node;
    }

    private static void NormalizeSidebars(LayoutNode? node, SplitAxis? parentAxis,
        (bool Left, bool Right, bool Top, bool Bottom) outerEdges)
    {
        if (node is null)
            return;
        if (node.Area is { } area)
        {
            var valid = area.SidebarEdge switch
            {
                SidebarEdge.None => true,
                SidebarEdge.Left => parentAxis == SplitAxis.Horizontal && outerEdges.Left,
                SidebarEdge.Right => parentAxis == SplitAxis.Horizontal && outerEdges.Right,
                SidebarEdge.Top => parentAxis == SplitAxis.Vertical && outerEdges.Top,
                SidebarEdge.Bottom => parentAxis == SplitAxis.Vertical && outerEdges.Bottom,
                _ => false
            };
            if (!valid)
                area.SidebarEdge = SidebarEdge.None;
            return;
        }

        if (node.Axis == SplitAxis.Horizontal)
        {
            NormalizeSidebars(node.First, node.Axis, (outerEdges.Left, false, outerEdges.Top, outerEdges.Bottom));
            NormalizeSidebars(node.Second, node.Axis, (false, outerEdges.Right, outerEdges.Top, outerEdges.Bottom));
        }
        else
        {
            NormalizeSidebars(node.First, node.Axis, (outerEdges.Left, outerEdges.Right, outerEdges.Top, false));
            NormalizeSidebars(node.Second, node.Axis, (outerEdges.Left, outerEdges.Right, false, outerEdges.Bottom));
        }
    }

    private static IEnumerable<LayoutNode> Nodes(LayoutNode? node)
    {
        if (node is null)
            yield break;

        yield return node;
        foreach (var descendant in Nodes(node.First))
            yield return descendant;
        foreach (var descendant in Nodes(node.Second))
            yield return descendant;
    }
}
