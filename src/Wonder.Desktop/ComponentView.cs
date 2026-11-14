// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Desktop.Components;
using Wonder.Extensibility.Contracts;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed class ComponentView : Viewbox, IDisposable
{
    private readonly ComponentHost _host;
    private readonly bool _preview;
    private ComponentSession? _session;
    private ComponentContent? _content;
    private ComponentDefinition _definition;
    private StreamGeometry? _outline;
    private bool _runtimeVisible = true;
    private bool _attached;
    private bool _disposed;
    private bool _selected;
    private bool _showingFailure;
    private bool _refreshing;
    private bool _inViewport;
    private double _scale = 1;
    private bool _scaleApplied;
    private bool _sidebar;
    private double _areaWidth = double.NaN;
    private bool _revealStarted;
    private DispatcherTimer? _revealTimer;
    private Control? _revealSurface;
    private Window? _detailsWindow;
    private readonly List<Visual> _visibilityAncestors = [];
    public ComponentItem Item { get; }
    public bool IsSidebarButton => _sidebar && _host.Registry.Find(Item.DefinitionId)?.SidebarEntry is not null;
    public Size Footprint => GetFootprint(_definition, _areaWidth, _scale, IsSidebarButton);
    private Size NativeSize => new(Footprint.Width / _scale, Footprint.Height / _scale);

    internal static Size GetFootprint(ComponentDefinition definition, double areaWidth, double scale, bool sidebarButton)
    {
        if (sidebarButton) return new Size(120 * scale, 40 * scale);
        var width = definition.Desktop.StretchToAreaWidth && double.IsFinite(areaWidth)
            ? Math.Max(ComponentGrid.Unit, areaWidth - 2 * definition.Desktop.HorizontalMargin)
            : definition.Width * scale;
        return new Size(width, definition.Height * scale);
    }

    public void SetAreaPresentation(bool sidebar, double areaWidth)
    {
        var wasSidebar = IsSidebarButton;
        _sidebar = sidebar;
        _areaWidth = areaWidth;
        if (wasSidebar != IsSidebarButton) BuildContent();
        UpdateSize();
    }

    public ComponentView(ComponentItem item, ComponentHost host, bool preview = false, bool livePreview = false)
    {
        Item = item;
        _host = host;
        _preview = preview;
        _definition = host.GetDefinition(item);
        if (preview)
        {
            IsHitTestVisible = false;
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        }
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        ToolTip.SetTip(this, _definition.Description);
        BuildContent();
        AttachedToVisualTree += (_, _) =>
        {
            if (_disposed || _preview && !livePreview) return;
            _attached = true;
            _inViewport = _preview;
            _visibilityAncestors.AddRange(this.GetVisualAncestors().Prepend(this));
            foreach (var ancestor in _visibilityAncestors) ancestor.PropertyChanged += AncestorVisibilityChanged;
            // Layout supplies the first effective viewport; an offscreen catalogue card never starts eagerly.
            _session = _host.Attach(Item, visible: false);
            _session.Changed += RuntimeChanged;
            UpdateVisibility();
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            _inViewport = false;
            ReleaseVisibilitySubscriptions();
            DetachSession();
            FinishReveal();
        };
        EffectiveViewportChanged += (_, args) =>
        {
            if (_preview) return;
            _inViewport = args.EffectiveViewport.Intersects(new Rect(Bounds.Size));
            UpdateVisibility();
        };
    }

    private void UpdateVisibility() =>
        _session?.SetVisible(_attached && _runtimeVisible && _inViewport && _visibilityAncestors.All(ancestor => ancestor.IsVisible));

    private void AncestorVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty) UpdateVisibility();
    }

    private void ReleaseVisibilitySubscriptions()
    {
        foreach (var ancestor in _visibilityAncestors) ancestor.PropertyChanged -= AncestorVisibilityChanged;
        _visibilityAncestors.Clear();
    }

    public void SetRuntimeVisible(bool visible)
    {
        _runtimeVisible = visible;
        UpdateVisibility();
    }

    public void SetScale(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor));
        _scale = factor;
        _scaleApplied = true;
        UpdateSize();
    }

    private void UpdateSize()
    {
        Width = Footprint.Width;
        Height = Footprint.Height;
        if (Child is not null) { Child.Width = NativeSize.Width; Child.Height = NativeSize.Height; }
    }

    public void RevealOnFirstActivation()
    {
        if (_revealStarted || _preview || _disposed) return;
        _revealStarted = true;
        var options = _definition.Desktop;
        if (options.RevealDurationMilliseconds <= 0 || Child is null) return;
        _revealSurface = Child;
        var surface = _revealSurface;
        var translation = new TranslateTransform(0, options.RevealOffsetY);
        surface.Opacity = 0;
        surface.RenderTransform = translation;
        var started = Stopwatch.GetTimestamp();
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _revealTimer.Tick += (_, _) =>
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds - options.RevealDelayMilliseconds;
            var progress = Math.Clamp(elapsed / options.RevealDurationMilliseconds, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            surface.Opacity = eased;
            translation.Y = options.RevealOffsetY * (1 - eased);
            if (progress >= 1) FinishReveal();
        };
        _revealTimer.Start();
    }

    private void FinishReveal()
    {
        _revealTimer?.Stop();
        _revealTimer = null;
        if (_revealSurface is not null) { _revealSurface.Opacity = 1; _revealSurface.RenderTransform = null; }
        _revealSurface = null;
    }

    public bool IsHitAt(Point local)
    {
        if (Child is null) return false;
        var point = this.TranslatePoint(local, Child);
        return point.HasValue && (_outline?.FillContains(point.Value) ?? new Rect(NativeSize).Contains(point.Value));
    }

    public bool CanStartDrag(Point local)
    {
        if (_disposed || !IsHitAt(local) || Child is null) return false;
        try
        {
            var point = this.TranslatePoint(local, Child);
            return point.HasValue && (_content?.CanStartDrag(point.Value) ?? true);
        }
        catch (Exception exception)
        {
            FailPresentation(exception);
            return false;
        }
    }

    public void SetSelected(bool selected)
    {
        if (_disposed) return;
        _selected = selected;
        try { _content?.SetSelected(selected); }
        catch (Exception exception) { FailPresentation(exception); }
    }

    private void RuntimeChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            var failed = _session?.Status is ComponentRunStatus.Faulted or ComponentRunStatus.Unavailable;
            var definition = _host.GetDefinition(Item);
            if (failed != _showingFailure || definition != _definition)
            {
                _definition = definition;
                ToolTip.SetTip(this, _definition.Description);
                BuildContent();
            }
            _content?.Refresh();
        }
        catch (Exception exception)
        {
            FailPresentation(exception);
        }
        finally { _refreshing = false; }
    }

    private void BuildContent()
    {
        ReleaseContent();
        if (_scaleApplied) SetScale(_scale);
        _outline = null;
        if (!IsSidebarButton && _definition.Outline.Count > 0)
        {
            _outline = new StreamGeometry();
            using var geometry = _outline.Open();
            var points = _definition.Outline.Select(point => new Point(point.X * NativeSize.Width, point.Y * NativeSize.Height)).ToArray();
            geometry.BeginFigure(points[0], true);
            foreach (var point in points.Skip(1)) geometry.LineTo(point);
            geometry.EndFigure(true);
        }
        var registration = _host.Registry.Find(Item.Application.DefinitionId);
        if (registration is null || _session?.Status is ComponentRunStatus.Faulted or ComponentRunStatus.Unavailable)
        {
            BuildFailure(_session?.Error ?? "尚未安装或注册：" + Item.Application.DefinitionId);
            return;
        }
        try
        {
            var context = new ComponentPresentationContext(_definition, _preview,
                () => _session?.Values ?? new ReadOnlyDictionary<string, string>(Item.Application.State),
                (action, value) => _session?.Invoke(action, value));
            _content = IsSidebarButton ? BuildSidebarEntry(context, registration.SidebarEntry!) : registration.CreatePresentation(context);
            _showingFailure = false;
            Child = new Grid { Width = NativeSize.Width, Height = NativeSize.Height, Children = { _content.Control } };
            _content.SetSelected(_selected);
            _content.Refresh();
        }
        catch (Exception exception)
        {
            FailPresentation(exception);
        }
    }

    private ComponentContent BuildSidebarEntry(ComponentPresentationContext context, ComponentSidebarEntry entry)
    {
        var title = entry.Title ?? _definition.Title;
        var button = new Button
        {
            Content = title, FontSize = 11, Padding = new Thickness(10, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Background = Palette.Active, Foreground = Palette.Text, BorderBrush = Palette.Accent,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), IsEnabled = !_preview
        };
        AutomationProperties.SetName(button, "打开" + title);
        ToolTip.SetTip(button, "打开" + title + " · 长按移动");
        button.Click += (_, _) =>
        {
            if (entry.OpenPage is not null) entry.OpenPage(TopLevel.GetTopLevel(this) as Window);
            else if (entry.ActionId is not null) context.Invoke(entry.ActionId);
            else OpenDetails();
        };
        return new ComponentContent(button, select: selected => button.BorderBrush = selected ? Palette.Emphasis : Palette.Accent);
    }

    private void OpenDetails()
    {
        if (_detailsWindow is not null) { _detailsWindow.Activate(); return; }
        var component = new ComponentView(Item, _host);
        component.SetScale(1);
        var owner = TopLevel.GetTopLevel(this) as Window;
        var details = new Window
        {
            Title = _definition.Title, Width = _definition.Width + 32, Height = _definition.Height + 32,
            MinWidth = _definition.Width + 32, MinHeight = _definition.Height + 32,
            Background = Palette.Background, Content = new Border { Padding = new Thickness(16), Child = component }
        };
        if (owner is not null) details.FontFamily = owner.FontFamily;
        details.Closed += (_, _) => { component.Dispose(); _detailsWindow = null; };
        _detailsWindow = details;
        if (owner is null) details.Show(); else details.Show(owner);
    }

    private void FailPresentation(Exception exception)
    {
        if (!_preview) _session?.Fail(exception);
        BuildFailure(exception.Message);
    }

    private void BuildFailure(string? error)
    {
        ReleaseContent();
        _showingFailure = true;
        _outline = null;
        var panel = new StackPanel { Spacing = 9, Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "组件暂不可用", FontWeight = FontWeight.SemiBold, Foreground = Palette.Error });
        panel.Children.Add(new TextBlock { Text = Item.Application.DefinitionId, FontSize = 10, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = error, FontSize = 11, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap, MaxHeight = 56 });
        var retry = new Button { Content = "重试", FontSize = 11, IsEnabled = !_preview, HorizontalAlignment = HorizontalAlignment.Left };
        retry.Click += (_, _) => _session?.Retry();
        panel.Children.Add(retry);
        _content = ComponentContent.Card(panel, canStartDrag: point => point.Y < 40);
        Child = new Grid { Width = NativeSize.Width, Height = NativeSize.Height, Children = { _content.Control } };
        _content.SetSelected(_selected);
    }

    private void DetachSession()
    {
        if (_session is null) return;
        _session.Changed -= RuntimeChanged;
        _session.Dispose();
        _session = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FinishReveal();
        _detailsWindow?.Close();
        ReleaseVisibilitySubscriptions();
        DetachSession();
        ReleaseContent();
    }

    private void ReleaseContent()
    {
        var content = _content;
        _content = null;
        // A faulty native presenter must still release its session and leave other components usable.
        try { content?.Dispose(); }
        catch (Exception error) { Wonder.Core.LauncherDiagnostics.Warn("DisposeComponentContent", error); }
    }
}
