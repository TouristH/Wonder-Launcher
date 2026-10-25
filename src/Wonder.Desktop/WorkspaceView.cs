// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Wonder.Desktop.Components;

namespace Wonder.Desktop;

public sealed partial class WorkspaceView : Panel
{
    public const int HoldMilliseconds = 450;
    public WorkspaceDocument Document { get; private set; }
    public LayoutSnapshot Snapshot => MeasureWorkspace(Bounds.Size);
    public event EventHandler? Changed;
    public event EventHandler? HelpRequested;
    public event Action? ComponentLibraryRequested;
    public event Action? SettingsRequested;
    public void OpenSettings() { CancelGesture(); SettingsRequested?.Invoke(); }
    internal ComponentHost Components { get; }
    private bool _runtimeVisible = true;
    internal Control? ComponentLibraryDropTarget { get; set; }
    private readonly Dictionary<string, AreaView> _areas = new();
    private readonly Dictionary<string, Border> _splitters = new();
    private readonly DispatcherTimer _holdTimer;
    private readonly Border _dragLabel;
    private readonly TextBlock _dragText;
    private Gesture? _gesture;
    private ComponentView? _dragPreview;
    private ComponentView? _libraryPreview;
    private Rect _libraryPreviewBounds;
    private string? _libraryTargetAreaId;
    private Point _libraryPointer;
    private string? _hoveredSplitId;
    private bool _finishing;
    private AreaRenameWindow? _renameDialog;
    private LauncherSettings _settings = new();

    public void ApplySettings(LauncherSettings settings)
    {
        settings.Validate();
        CancelGesture();
        FinishSidebarAnimation();
        _settings = settings;
        foreach (var area in _areas.Values) area.ShowNumber(settings.ShowAreaNumbers);
        _holdTimer.Interval = TimeSpan.FromMilliseconds(settings.HoldDelayMilliseconds);
        _sidebarCloseTimer.Stop();
        _sidebarCloseTimer.Interval = TimeSpan.FromMilliseconds(settings.SidebarCloseDelayMilliseconds);
        ScheduleSidebarClose();
        InvalidateArrange();
    }

    public WorkspaceView(WorkspaceDocument document) : this(document, null) { }

    internal WorkspaceView(WorkspaceDocument document, string? dataDirectory, ComponentRegistry? registry = null)
    {
        Components = new ComponentHost(OpenSettings, NotifyChanged, dataDirectory, registry);
        document.EnsureLibrary(Components.Registry.Definitions.Select(definition => definition.Id));
        Document = document;
        Background = Brushes.Transparent;
        Focusable = true;
        ClipToBounds = true;
        _dragText = new TextBlock { FontSize = 12, Foreground = Palette.Accent };
        _dragLabel = new Border { Child = _dragText, Background = Palette.Background, BorderBrush = Palette.Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8), IsHitTestVisible = false, IsVisible = false };
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoldMilliseconds) };
        _holdTimer.Tick += (_, _) => ActivateGesture();
        InitializeSidebars();
        AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Moved, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, KeyPressed, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => { if (!_finishing) CancelGesture(); };
        ContextRequested += OpenContextMenu;
        PointerExited += (_, _) => { _hoveredSplitId = null; UpdateHighlights(); };
        DetachedFromVisualTree += (_, _) => { CancelGesture(); FinishSidebarAnimation(); };
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragEnterHandler(this, LibraryDragOver);
        DragDrop.AddDragOverHandler(this, LibraryDragOver);
        DragDrop.AddDragLeaveHandler(this, (_, _) =>
        {
            _sidebarPointer = new Point(-1, -1);
            ClearLibraryDrop();
            ScheduleSidebarClose();
        });
        DragDrop.AddDropHandler(this, LibraryDrop);
        Rebuild();
    }

    internal void SetRuntimeVisible(bool visible)
    {
        _runtimeVisible = visible;
        foreach (var area in _areas.Values) area.SetRuntimeVisible(visible && area.IsVisible);
    }

    internal void DisposeComponents()
    {
        CancelGesture();
        ClearLibraryDrop();
        foreach (var area in _areas.Values) area.Dispose();
        Components.Dispose();
    }

    public Rect GetComponentBounds(string componentId)
    {
        if (_gesture is { Active: true, Component: not null } && _gesture.Component.Id == componentId)
            return PreviewBounds(_gesture);
        var snapshot = Snapshot;
        foreach (var (areaId, view) in _areas)
            if (view.Components.TryGetValue(componentId, out var component) && snapshot.Areas.TryGetValue(areaId, out var bounds))
            {
                var rect = view.ComponentRect(component, new Size(bounds.Width, bounds.Height));
                return new Rect(rect.X + bounds.X, rect.Y + bounds.Y, rect.Width, rect.Height);
            }
        return default;
    }

    /// <summary>Replaces the layout, retaining current applications and importing sources referenced by the new shortcuts.</summary>
    public void ReplaceDocument(WorkspaceDocument document)
    {
        CancelGesture();
        document.EnsureLibrary();
        var requestedApplications = document.Library.ToArray();
        var requestedIds = document.Areas.SelectMany(area => area.Components).Select(item => item.ApplicationId).ToHashSet();
        var applications = new List<ComponentItem>(Document.Library);
        foreach (var application in requestedApplications)
        {
            if (!requestedIds.Contains(application.Id)) continue;
            var existing = applications.FirstOrDefault(item => item.Id == application.Id);
            if (existing is not null && existing.DefinitionId != application.DefinitionId)
                throw new InvalidDataException("应用标识与现有组件定义冲突。");
            if (existing is null) applications.Add(application);
        }
        document.Library = applications;
        document.EnsureLibrary(Components.Registry.Definitions.Select(definition => definition.Id));
        Document = document;
        Rebuild();
        NotifyChanged();
    }

    public void CancelGesture()
    {
        var gesture = _gesture;
        if (gesture == null) return;
        if (gesture.Component != null)
        {
            gesture.Component.X = gesture.OriginalX;
            gesture.Component.Y = gesture.OriginalY;
        }
        if (gesture.Split != null) gesture.Split.Ratio = gesture.OriginalRatio;
        // A pending short press may still belong to a child Button. Cancel its
        // pressed state too, while leaving another control's capture untouched.
        if (gesture.Pointer.Captured is Visual captured && captured.GetVisualAncestors().Contains(this))
        {
            _finishing = true;
            gesture.Pointer.Capture(null);
            _finishing = false;
        }
        EndGesture();
        InvalidateArrange();
    }

    public ContextMenu CreateWorkspaceMenu(string? areaId = null, string? componentId = null)
    {
        var menu = new ContextMenu();
        var area = areaId == null ? null : Document.FindArea(areaId);
        var items = new List<Control> { Menu("组件库…", () => ComponentLibraryRequested?.Invoke()), Menu("设置…", OpenSettings), new Separator() };
        if (componentId != null && area != null)
        {
            items.Add(Menu("移除这个组件", () => { area.Components.RemoveAll(c => c.Id == componentId); CommitStructure(); }));
            items.Add(new Separator());
        }
        if (area != null)
        {
            items.Add(Menu("重命名功能区…", () => RenameArea(area)));
            items.Add(Menu("整理组件到可见范围", () => RecoverComponents(area)));
            items.Add(new Separator());
            items.Add(Menu("左右拆分功能区", () => AddArea(area.Id, SplitAxis.Horizontal)));
            items.Add(Menu("上下拆分功能区", () => AddArea(area.Id, SplitAxis.Vertical)));
            items.Add(Menu("移除此功能区", () => { Document.RemoveArea(area.Id); CommitStructure(); }));
            items.Add(new Separator());
        }
        else items.Add(Menu("添加功能区", () => AddArea(null, SplitAxis.Horizontal)));
        items.Add(Menu("恢复默认布局", RestoreDefaultLayout));
        items.Add(Menu("清空框架", () => ReplaceDocument(WorkspaceDocument.CreateEmpty())));
        items.Add(new Separator());
        items.Add(Menu("操作说明", () => HelpRequested?.Invoke(this, EventArgs.Empty)));
        menu.ItemsSource = items;
        return menu;
    }

    private void RestoreDefaultLayout()
    {
        var document = WorkspaceDocument.CreateDefault();
        var shortcut = document.Areas.Single().Components.Single();
        var existing = Document.Library.First(application => application.DefinitionId == shortcut.DefinitionId);
        document.Library = Document.Library;
        shortcut.ApplicationId = existing.Id;
        ReplaceDocument(document);
    }

    private async void RenameArea(Area area)
    {
        if (_renameDialog != null) { _renameDialog.Activate(); return; }
        if (TopLevel.GetTopLevel(this) is not Window owner || Document.FindArea(area.Id) != area) return;
        CancelGesture();
        ContextMenu?.Close();
        var document = Document;
        var dialog = new AreaRenameWindow(area.Title) { FontFamily = owner.FontFamily };
        _renameDialog = dialog;
        try
        {
            var title = await dialog.ShowDialog<string?>(owner);
            if (title == null || title == area.Title || Document != document || Document.FindArea(area.Id) != area) return;
            area.Title = title;
            CommitStructure();
        }
        finally { _renameDialog = null; }
    }

    private void AddArea(string? id, SplitAxis axis)
    {
        if (Document.Areas.Count() >= 12) return;
        Document.AddArea(id, axis);
        CommitStructure();
    }

    internal bool ContainsShortcut(ComponentItem shortcut) => Document.Areas.Any(area => area.Components.Contains(shortcut));

    internal bool RemoveShortcut(ComponentItem shortcut)
    {
        var area = Document.Areas.FirstOrDefault(area => area.Components.Contains(shortcut));
        if (area == null) return false;
        CancelGesture();
        area.Components.Remove(shortcut);
        CommitStructure();
        return true;
    }

    private bool CanDropLibraryComponent(LibraryComponentTransfer data, Point point, out Area? area)
    {
        var id = AreaAt(point);
        area = id == null ? null : Document.FindArea(id);
        return Document.Library.Contains(data.Application) && new Rect(Bounds.Size).Contains(point) && (Document.Root == null || area is { Components.Count: < 24 });
    }

    private void LibraryDragOver(object? sender, DragEventArgs e)
    {
        var data = e.DataTransfer.TryGetValue(LibraryComponentTransfer.Format);
        if (data == null) { e.DragEffects = DragDropEffects.None; ClearLibraryDrop(); return; }
        CancelGesture();
        var point = e.GetPosition(this);
        UpdateSidebarHover(point);
        var allowed = e.DragEffects.HasFlag(DragDropEffects.Copy) && CanDropLibraryComponent(data, point, out _);
        e.DragEffects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (!allowed) { ClearLibraryDrop(); return; }
        if (_libraryPreview?.Item.Application != data.Application)
        {
            ClearLibraryDrop();
            _libraryPreview = new ComponentView(Document.CreateShortcut(data.Application), Components, preview: true) { IsHitTestVisible = false, Opacity = .85, ZIndex = 400 };
            Children.Add(_libraryPreview);
            _libraryPreview.SetSelected(true);
        }
        _libraryTargetAreaId = AreaAt(point);
        _libraryPointer = point;
        _libraryPreviewBounds = LibraryPlacement(data, point, _libraryPreview.Footprint);
        _dragText.Text = _libraryTargetAreaId == null ? "松开创建功能区并放置快捷方式" : "松开创建快捷方式";
        _dragLabel.IsVisible = true;
        UpdateHighlights();
        InvalidateMeasure();
    }

    private Rect LibraryPlacement(LibraryComponentTransfer data, Point point, Size size)
    {
        var id = AreaAt(point);
        var area = id == null ? new LayoutRect(0, 0, Bounds.Width, Bounds.Height) : GetAreaBounds(id);
        var center = point + data.GrabOffset;
        var offset = ClampOffset(area, size, center.X - area.X - area.Width / 2, center.Y - area.Y - area.Height / 2);
        return new Rect(area.X + area.Width / 2 + offset.X - size.Width / 2, area.Y + area.Height / 2 + offset.Y - size.Height / 2, size.Width, size.Height);
    }

    private void LibraryDrop(object? sender, DragEventArgs e)
    {
        var data = e.DataTransfer.TryGetValue(LibraryComponentTransfer.Format);
        if (data == null) { e.DragEffects = DragDropEffects.None; ClearLibraryDrop(); return; }
        var point = e.GetPosition(this);
        var allowed = e.DragEffects.HasFlag(DragDropEffects.Copy) && CanDropLibraryComponent(data, point, out _);
        e.DragEffects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        ClearLibraryDrop();
        if (!allowed) return;
        CancelGesture();
        var id = AreaAt(point);
        var area = id == null ? Document.AddArea() : Document.FindArea(id)!;
        var item = Document.CreateShortcut(data.Application);
        var definition = Components.GetDefinition(item);
        var size = new Size(definition.Width, definition.Height);
        var placed = LibraryPlacement(data, point, size);
        var bounds = GetAreaBounds(area.Id);
        item.X = placed.Center.X - bounds.X - bounds.Width / 2;
        item.Y = placed.Center.Y - bounds.Y - bounds.Height / 2;
        area.Components.Add(item);
        CommitStructure();
    }

    internal void ClearLibraryDrop()
    {
        if (_libraryPreview != null)
        {
            Children.Remove(_libraryPreview);
            _libraryPreview.Dispose();
        }
        _libraryPreview = null;
        _libraryTargetAreaId = null;
        if (_gesture == null) _dragLabel.IsVisible = false;
        UpdateHighlights();
        InvalidateArrange();
    }

    private void RecoverComponents(Area area)
    {
        var bounds = GetAreaBounds(area.Id);
        foreach (var view in _areas[area.Id].Components.Values)
        {
            var position = ClampOffset(bounds, view.Footprint, view.Item.X, view.Item.Y);
            view.Item.X = position.X;
            view.Item.Y = position.Y;
        }
        InvalidateArrange();
        NotifyChanged();
    }

    private void CommitStructure()
    {
        CancelGesture();
        Rebuild();
        NotifyChanged();
    }

    private static MenuItem Menu(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void Rebuild()
    {
        FinishSidebarAnimation();
        ClearLibraryDrop();
        foreach (var area in _areas.Values) area.Dispose();
        Children.Clear();
        _areas.Clear();
        _splitters.Clear();
        var index = 0;
        foreach (var area in Document.Areas)
        {
            var view = new AreaView(area, index++, Components, _settings.ShowAreaNumbers);
            _areas.Add(area.Id, view);
            Children.Add(view);
        }
        void AddSplits(LayoutNode? node)
        {
            if (node == null || node.Area != null) return;
            var divider = new Border { Background = Brushes.Transparent, Cursor = new Cursor(node.Axis == SplitAxis.Horizontal ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth) };
            divider.Child = new Border { Background = Palette.Line, CornerRadius = new CornerRadius(2), Width = node.Axis == SplitAxis.Horizontal ? 2 : double.NaN, Height = node.Axis == SplitAxis.Horizontal ? double.NaN : 2, HorizontalAlignment = node.Axis == SplitAxis.Horizontal ? HorizontalAlignment.Center : HorizontalAlignment.Stretch, VerticalAlignment = node.Axis == SplitAxis.Horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Center, IsHitTestVisible = false };
            ToolTip.SetTip(divider, "按住整条分隔边拖动，即可调整大小");
            _splitters.Add(node.Id, divider);
            Children.Add(divider);
            AddSplits(node.First);
            AddSplits(node.Second);
        }
        AddSplits(Document.Root);
        RebuildSidebars();
        _dragLabel.ZIndex = 500;
        Children.Add(_dragLabel);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 1000, double.IsFinite(availableSize.Height) ? availableSize.Height : 650);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var snapshot = MeasureWorkspace(finalSize);
        UpgradePreviewCoordinates(snapshot, finalSize);
        foreach (var (id, view) in _areas)
        {
            var rect = ToRect(snapshot.Areas[id]);
            view.IsVisible = view.Area.SidebarEdge == SidebarEdge.None || IsSidebarContentVisible(id);
            view.SetRuntimeVisible(_runtimeVisible && view.IsVisible);
            view.Opacity = view.Area.SidebarEdge == SidebarEdge.None ? 1 : SidebarContentOpacity(id);
            view.ZIndex = id == _expandedSidebarId ? 200 : 0;
            view.PositionComponents(rect.Size);
            view.Arrange(rect);
        }
        foreach (var (id, view) in _splitters)
        {
            view.IsVisible = snapshot.Splits.ContainsKey(id);
            if (view.IsVisible) view.Arrange(SplitHitBounds(id, snapshot));
        }
        ArrangeSidebars(snapshot);
        if (_gesture != null)
        {
            if (_dragPreview != null) _dragPreview.Arrange(PreviewBounds(_gesture));
            var size = _dragLabel.DesiredSize;
            _dragLabel.Arrange(new Rect(Math.Clamp(_gesture.Last.X + 18, 0, Math.Max(0, finalSize.Width - size.Width)), Math.Clamp(_gesture.Last.Y + 18, 0, Math.Max(0, finalSize.Height - size.Height)), size.Width, size.Height));
        }
        if (_libraryPreview != null)
        {
            _libraryPreview.Arrange(_libraryPreviewBounds);
            var size = _dragLabel.DesiredSize;
            _dragLabel.Arrange(new Rect(Math.Clamp(_libraryPointer.X + 18, 0, Math.Max(0, finalSize.Width - size.Width)),
                Math.Clamp(_libraryPointer.Y + 18, 0, Math.Max(0, finalSize.Height - size.Height)), size.Width, size.Height));
        }
        return finalSize;
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gesture != null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var position = e.GetPosition(this);
        _sidebarPointer = position;
        var snapshot = Snapshot;
        var gesture = new Gesture { Pointer = e.Pointer, Trigger = e, Start = position, Last = position };
        gesture.InitialSnapshot = snapshot;
        var resizingSidebar = PrepareSidebarResize(gesture, position);
        foreach (var (id, bounds) in snapshot.Splits)
            if (!resizingSidebar && !IsOverExpandedSidebar(position) && SplitHitBounds(id, snapshot).Contains(position))
            {
                gesture.Split = Document.FindNode(id);
                gesture.OriginalRatio = gesture.Split!.Ratio;
                gesture.SplitBounds = NodeBounds(gesture.Split, snapshot);
                gesture.DividerStart = gesture.Split.Axis == SplitAxis.Horizontal ? bounds.X : bounds.Y;
                break;
            }
        if (gesture.Split == null)
        {
            gesture.AreaId = AreaAt(position);
            if (gesture.AreaId == null) return;
            var area = _areas[gesture.AreaId];
            foreach (var view in area.Components.Values.Reverse())
            {
                if (!HitsComponent(gesture.AreaId, view, position)) continue;
                var componentBounds = GetComponentBounds(view.Item.Id);
                if (!view.CanStartDrag(new Point(position.X - componentBounds.X, position.Y - componentBounds.Y))) return;
                gesture.Component = view.Item;
                gesture.OriginalX = view.Item.X;
                gesture.OriginalY = view.Item.Y;
                gesture.ComponentBounds = GetComponentBounds(view.Item.Id);
                break;
            }
            if (gesture.Component == null)
            {
                Focus();
                var bounds = GetAreaBounds(gesture.AreaId);
                if (position.Y > bounds.Y + AreaView.HeaderHeight) return;
            }
            if (gesture.Component == null) area.Highlight(false, false, true);
        }
        _gesture = gesture;
        if (gesture.Split != null)
        {
            e.Handled = true;
            ActivateGesture();
        }
        else _holdTimer.Start();
    }

    private void ActivateGesture()
    {
        _holdTimer.Stop();
        if (_gesture == null || _gesture.Aborted) return;
        if (_gesture.Split != null) FinishSidebarAnimation();
        _gesture.Active = true;
        _gesture.Pointer.Capture(this);
        if (_gesture.Component != null)
        {
            _dragPreview = new ComponentView(_gesture.Component, Components, preview: true) { IsHitTestVisible = false, Opacity = .92, ZIndex = 400 };
            _dragPreview.SetSelected(true);
            Children.Insert(Children.Count - 1, _dragPreview);
            _areas[_gesture.AreaId!].Components[_gesture.Component.Id].Opacity = .2;
            _gesture.TargetAreaId = _gesture.AreaId;
        }
        Focus();
        Cursor = new Cursor(_gesture.Split != null ? (_gesture.Split.Axis == SplitAxis.Horizontal ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth) : StandardCursorType.SizeAll);
        _dragText.Text = _gesture.SidebarArea != null ? "松开保持侧边栏 · Esc 取消" : _gesture.Split != null ? "调整大小 · Esc 取消" : _gesture.Component != null ? "拖入功能区放置 · Esc 取消" : "拖到其他功能区交换 · Esc 取消";
        _dragLabel.IsVisible = true;
        UpdateHighlights();
        InvalidateMeasure();
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        UpdateSidebarHover(e.GetPosition(this));
        var gesture = _gesture;
        if (gesture == null)
        {
            var snapshot = Snapshot;
            _hoveredSplitId = IsOverExpandedSidebar(e.GetPosition(this)) ? null : snapshot.Splits.Keys.FirstOrDefault(id => SplitHitBounds(id, snapshot).Contains(e.GetPosition(this)));
            UpdateHighlights();
            return;
        }
        if (e.Pointer.Id != gesture.Pointer.Id) return;
        gesture.Last = e.GetPosition(this);
        var delta = gesture.Last - gesture.Start;
        if (!gesture.Active)
        {
            if (!gesture.Aborted && delta.X * delta.X + delta.Y * delta.Y > 49)
            {
                gesture.Aborted = true;
                _holdTimer.Stop();
                // Capture loss clears a native button's pressed state, preventing an accidental click.
                gesture.Pointer.Capture(this);
                UpdateHighlights();
            }
            return;
        }
        e.Handled = true;
        if (gesture.SidebarArea != null)
        {
            ResizeExpandedSidebar(gesture);
        }
        else if (gesture.Component != null && gesture.AreaId != null)
        {
            if (IsOverComponentLibrary(gesture.Last))
            {
                ReturnShortcutToLibrary(gesture);
                return;
            }
            gesture.TargetAreaId = AreaAt(gesture.Last);
            _dragText.Text = gesture.TargetAreaId == null ? "松开取消 · Esc 返回" : "松开放入功能区 · Esc 取消";
        }
        else if (gesture.Split != null)
        {
            var horizontal = gesture.Split.Axis == SplitAxis.Horizontal;
            var length = (horizontal ? gesture.SplitBounds.Width : gesture.SplitBounds.Height) - 10;
            var origin = horizontal ? gesture.SplitBounds.X : gesture.SplitBounds.Y;
            var offset = horizontal ? delta.X : delta.Y;
            if (length > 0) gesture.Split.Ratio = Math.Clamp((gesture.DividerStart - origin + offset) / length, .01, .99);
            FindSidebarCollapse(gesture, gesture.DividerStart - origin + offset, length);
        }
        else gesture.TargetAreaId = AreaAt(gesture.Last);
        UpdateHighlights();
        InvalidateArrange();
    }

    private bool IsOverComponentLibrary(Point point)
    {
        var target = ComponentLibraryDropTarget;
        if (target == null || TopLevel.GetTopLevel(target) is not Window { IsVisible: true } window ||
            window.WindowState == WindowState.Minimized) return false;
        return new Rect(target.Bounds.Size).Contains(target.PointToClient(this.PointToScreen(point)));
    }

    private async void ReturnShortcutToLibrary(Gesture gesture)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(WorkspaceComponentTransfer.Format,
            new WorkspaceComponentTransfer(this, gesture.Component!)));
        // Only the library's accepted Drop removes a shortcut. Finish capture and
        // preview first so native capture loss cannot cancel or touch a removed view.
        EndGesture();
        using var hoverSuppression = HoverTipBehavior.SuppressForNativeDrag();
        await DragDrop.DoDragDropAsync(gesture.Trigger, data, DragDropEffects.Move);
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        var gesture = _gesture;
        if (gesture == null || e.Pointer.Id != gesture.Pointer.Id) return;
        var active = gesture.Active;
        gesture.Last = e.GetPosition(this);
        e.Handled = active || gesture.Aborted;
        if (active && CommitSidebarGesture(gesture)) return;
        if (active && gesture.Split == null && !new Rect(Bounds.Size).Contains(e.GetPosition(this)))
        {
            CancelGesture();
            return;
        }
        var transferred = false;
        if (active && gesture.Component != null)
        {
            var targetId = AreaAt(gesture.Last);
            var source = Document.FindArea(gesture.AreaId!)!;
            var target = targetId == null ? null : Document.FindArea(targetId);
            if (target == null || target != source && target.Components.Count >= 24)
            {
                CancelGesture();
                return;
            }
            var bounds = GetAreaBounds(target.Id);
            var preview = PreviewBounds(gesture);
            var position = ClampOffset(bounds, preview.Size,
                preview.Center.X - bounds.X - bounds.Width / 2, preview.Center.Y - bounds.Y - bounds.Height / 2);
            gesture.Component.X = position.X;
            gesture.Component.Y = position.Y;
            if (source != target)
            {
                source.Components.Remove(gesture.Component);
                target.Components.Add(gesture.Component);
                transferred = true;
            }
        }
        var swapped = active && gesture.Component == null && gesture.Split == null && gesture.AreaId != null && gesture.TargetAreaId != null && Document.SwapAreas(gesture.AreaId, gesture.TargetAreaId);
        EndGesture();
        if (swapped || transferred) Rebuild();
        if (active) NotifyChanged();
    }

    private void EndGesture()
    {
        var sidebarReturn = _gesture?.SidebarArea is { SidebarEdge: not SidebarEdge.None } ? Snapshot : null;
        _holdTimer.Stop();
        var pointer = _gesture?.Pointer;
        if (_gesture?.Component != null && _gesture.AreaId != null)
            _areas[_gesture.AreaId].Components[_gesture.Component.Id].Opacity = 1;
        if (_dragPreview != null) { Children.Remove(_dragPreview); _dragPreview.Dispose(); _dragPreview = null; }
        _gesture = null;
        if (sidebarReturn != null) StartSidebarAnimation(sidebarReturn);
        _dragLabel.IsVisible = false;
        Cursor = Cursor.Default;
        UpdateHighlights();
        _finishing = true;
        if (pointer?.Captured == this) pointer.Capture(null);
        _finishing = false;
        InvalidateArrange();
        ScheduleSidebarClose();
    }

    private void UpdateHighlights()
    {
        foreach (var (id, view) in _areas)
        {
            var draggingArea = _gesture is { Active: true, Component: null, Split: null };
            view.Highlight(draggingArea && id == _gesture!.AreaId,
                draggingArea && id != _gesture!.AreaId && id == _gesture.TargetAreaId);
            foreach (var component in view.Components.Values)
                component.SetSelected(_gesture is { Active: true } && component.Item == _gesture.Component);
        }
        foreach (var (id, border) in _splitters)
        {
            var selected = _gesture is { Active: true, Split: not null } && id == _gesture.Split.Id;
            var highlighted = selected || _gesture == null && id == _hoveredSplitId;
            border.Background = highlighted ? Palette.Active : Brushes.Transparent;
            if (border.Child is Border line) line.Background = highlighted ? Palette.Accent : Palette.Line;
        }
    }

    private void KeyPressed(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelGesture(); e.Handled = true; }
        else if (e.Key == Key.F1) { HelpRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        else if (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (IsTextInput(e.Source as Visual)) return;
            var context = ContextFor(e.Source as Visual);
            ContextMenu = CreateWorkspaceMenu(context.AreaId, context.ComponentId);
            ContextMenu.Open(this);
            e.Handled = true;
        }
    }

    private void OpenContextMenu(object? sender, ContextRequestedEventArgs e)
    {
        if (IsTextInput(e.Source as Visual)) return;
        CancelGesture();
        var context = ContextFor(e.Source as Visual);
        var hasPosition = e.TryGetPosition(this, out var position);
        var areaId = hasPosition ? AreaAt(position) ?? context.AreaId : context.AreaId;
        var componentId = hasPosition ? (areaId == null ? null : _areas[areaId].Components.Values.Reverse().FirstOrDefault(view => HitsComponent(areaId, view, position))?.Item.Id) : context.ComponentId;
        ContextMenu = CreateWorkspaceMenu(areaId, componentId);
        ContextMenu.Open(this);
        e.Handled = true;
    }

    private static bool IsTextInput(Visual? source) => source is TextBox ||
        source?.GetVisualAncestors().OfType<TextBox>().Any() == true;

    private string? AreaAt(Point point)
    {
        if (IsOverExpandedSidebar(point)) return _expandedSidebarId;
        return Snapshot.Areas.FirstOrDefault(pair => Document.FindArea(pair.Key)!.SidebarEdge == SidebarEdge.None && ToRect(pair.Value).Contains(point)).Key;
    }
    private bool HitsComponent(string areaId, ComponentView view, Point position)
    {
        var area = GetAreaBounds(areaId);
        var content = new Rect(area.X + AreaView.Inset + 1, area.Y + AreaView.HeaderHeight + 1,
            Math.Max(0, area.Width - 2 * AreaView.Inset - 2), Math.Max(0, area.Height - AreaView.HeaderHeight - AreaView.Inset - 2));
        var rect = GetComponentBounds(view.Item.Id);
        return content.Contains(position) && rect.Contains(position) && view.IsHitAt(new Point(position.X - rect.X, position.Y - rect.Y));
    }

    private Rect SplitHitBounds(string id, LayoutSnapshot snapshot)
    {
        var rect = ToRect(snapshot.Splits[id]);
        var horizontal = Document.FindNode(id)!.Axis == SplitAxis.Horizontal;
        return horizontal ? new Rect(rect.X - 4, rect.Y, rect.Width + 8, rect.Height)
            : new Rect(rect.X, rect.Y - 4, rect.Width, rect.Height + 8);
    }

    private static (double X, double Y) ClampOffset(LayoutRect area, Size component, double x, double y)
    {
        var topLeft = WorkspaceLayout.FromCenterOffset(area.Width, area.Height, component.Width, component.Height, x, y);
        var originX = AreaView.Inset + 1;
        var originY = AreaView.HeaderHeight + 1;
        var visible = WorkspaceLayout.ClampComponent(topLeft.X - originX, topLeft.Y - originY,
            component.Width, component.Height, area.Width - 2 * originX, area.Height - originY - AreaView.Inset - 1);
        return WorkspaceLayout.ToCenterOffset(area.Width, area.Height, component.Width, component.Height,
            originX + visible.X, originY + visible.Y);
    }

    private void UpgradePreviewCoordinates(LayoutSnapshot snapshot, Size viewport)
    {
        if (Document.Version != 1 || viewport.Width <= 0 || viewport.Height <= 0) return;
        foreach (var (id, area) in _areas)
        {
            var bounds = snapshot.Areas[id];
            var width = Math.Max(0, bounds.Width - 2 * AreaView.Inset - 2);
            var height = Math.Max(0, bounds.Height - AreaView.HeaderHeight - AreaView.Inset - 2);
            foreach (var view in area.Components.Values)
            {
                // Version 1 did not store the viewport. Convert its visible position
                // once at the first real layout, using the same former clamping rule.
                var scale = Math.Min(1, Math.Min(width / view.Footprint.Width, height / view.Footprint.Height));
                var oldWidth = view.Footprint.Width * scale;
                var oldHeight = view.Footprint.Height * scale;
                var oldPosition = WorkspaceLayout.ClampComponent(view.Item.X, view.Item.Y, oldWidth, oldHeight, width, height);
                var offset = WorkspaceLayout.ToCenterOffset(bounds.Width, bounds.Height, oldWidth, oldHeight,
                    AreaView.Inset + 1 + oldPosition.X, AreaView.HeaderHeight + 1 + oldPosition.Y);
                view.Item.X = offset.X;
                view.Item.Y = offset.Y;
            }
        }
        Document.Version = 2;
        NotifyChanged();
    }
    private (string? AreaId, string? ComponentId) ContextFor(Visual? source)
    {
        if (source == null) return (null, null);
        var ancestors = source.GetVisualAncestors().Prepend(source).ToHashSet();
        foreach (var (id, area) in _areas)
            if (ancestors.Contains(area))
                return (id, area.Components.FirstOrDefault(pair => ancestors.Contains(pair.Value)).Key);
        foreach (var (id, tab) in _sidebarTabs)
            if (ancestors.Contains(tab)) return (id, null);
        return (null, null);
    }
    private static Rect ToRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private static Rect PreviewBounds(Gesture gesture) => new(
        gesture.ComponentBounds.X + gesture.Last.X - gesture.Start.X,
        gesture.ComponentBounds.Y + gesture.Last.Y - gesture.Start.Y,
        gesture.ComponentBounds.Width, gesture.ComponentBounds.Height);

    private static Rect NodeBounds(LayoutNode node, LayoutSnapshot snapshot)
    {
        if (node.Area != null) return ToRect(snapshot.Areas[node.Area.Id]);
        return NodeBounds(node.First!, snapshot).Union(NodeBounds(node.Second!, snapshot));
    }

    private sealed class Gesture
    {
        public required IPointer Pointer;
        public required PointerPressedEventArgs Trigger;
        public Point Start, Last;
        public bool Active, Aborted;
        public string? AreaId, TargetAreaId;
        public ComponentItem? Component;
        public LayoutNode? Split;
        public double OriginalX, OriginalY, OriginalRatio, DividerStart;
        public Rect SplitBounds;
        public Rect ComponentBounds;
        public LayoutSnapshot? InitialSnapshot;
        public Area? CollapseArea, SidebarArea;
        public SidebarEdge CollapseEdge;
        public double InitialSidebarExtent, SidebarPreviewExtent;
    }
}
