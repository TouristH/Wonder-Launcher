// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly List<Visual> _visibilityAncestors = [];
    public ComponentItem Item { get; }
    public Size Footprint => new(_definition.Width, _definition.Height);

    public ComponentView(ComponentItem item, ComponentHost host, bool preview = false)
    {
        Item = item;
        _host = host;
        _preview = preview;
        _definition = host.GetDefinition(item);
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.DownOnly;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        ToolTip.SetTip(this, _definition.Description);
        BuildContent();
        AttachedToVisualTree += (_, _) =>
        {
            if (_disposed || _preview) return;
            _attached = true;
            _inViewport = false;
            _visibilityAncestors.AddRange(this.GetVisualAncestors().Prepend(this));
            foreach (var ancestor in _visibilityAncestors) ancestor.PropertyChanged += AncestorVisibilityChanged;
            // Layout supplies the first effective viewport; an offscreen catalogue card never starts eagerly.
            _session = _host.Attach(Item, visible: false);
            _session.Changed += RuntimeChanged;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            _inViewport = false;
            ReleaseVisibilitySubscriptions();
            DetachSession();
        };
        EffectiveViewportChanged += (_, args) =>
        {
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

    public bool IsHitAt(Point local)
    {
        if (Child is null) return false;
        var point = this.TranslatePoint(local, Child);
        return point.HasValue && (_outline?.FillContains(point.Value) ?? new Rect(Footprint).Contains(point.Value));
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
        _outline = null;
        if (_definition.Outline.Count > 0)
        {
            _outline = new StreamGeometry();
            using var geometry = _outline.Open();
            var points = _definition.Outline.Select(point => new Point(point.X * Footprint.Width, point.Y * Footprint.Height)).ToArray();
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
            _content = registration.CreatePresentation(context);
            _showingFailure = false;
            Child = new Grid { Width = Footprint.Width, Height = Footprint.Height, Children = { _content.Control } };
            _content.SetSelected(_selected);
            _content.Refresh();
        }
        catch (Exception exception)
        {
            FailPresentation(exception);
        }
    }

    private void FailPresentation(Exception exception)
    {
        _session?.Fail(exception);
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
        Child = new Grid { Width = Footprint.Width, Height = Footprint.Height, Children = { _content.Control } };
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
        catch { }
    }
}
