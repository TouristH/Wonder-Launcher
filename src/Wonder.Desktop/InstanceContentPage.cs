// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed class InstanceContentPage : UserControl, IDisposable
{
    private readonly InstanceService _service;
    private readonly Window _owner;
    private readonly Action<string, InstanceContentKind>? _browseResources;
    private readonly StackPanel _categories = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly TextBox _search = new() { MaxLength = 200 };
    private readonly ListBox _list = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
    private readonly TextBlock _empty = PageStyles.Hint("");
    private readonly TextBlock _status = PageStyles.Hint("");
    private readonly StackPanel _details = new() { Spacing = 11 };
    private readonly SemaphoreSlim _previewReads = new(2);
    private readonly Button _browse, _refresh, _toggle, _delete, _largePreview;
    private GameInstance? _instance;
    private InstanceContentKind _kind;
    private IReadOnlyList<InstanceContent> _items = [];
    private CancellationTokenSource? _read;
    private bool _working, _closed;

    public InstanceContentPage(InstanceService service, Window owner, Action<string, InstanceContentKind>? browseResources = null)
    {
        _service = service;
        _owner = owner;
        _browseResources = browseResources;

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        foreach (var kind in Enum.GetValues<InstanceContentKind>())
        {
            var tab = new ToggleButton { Content = CategoryName(kind), Tag = kind, MinHeight = 34 };
            AutomationProperties.SetName(tab, "管理" + CategoryName(kind));
            tab.Click += (_, _) => SelectKind(kind);
            _categories.Children.Add(tab);
        }
        layout.Children.Add(_categories);

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,Auto,8,Auto"),
            Margin = new Thickness(0, 10, 0, 12)
        };
        _search.TextChanged += (_, _) => Filter();
        toolbar.Children.Add(_search);
        _browse = ActionButton("搜索在线资源…", () =>
        {
            if (_instance is { } instance) _browseResources?.Invoke(instance.Id, _kind);
        });
        Grid.SetColumn(_browse, 2);
        toolbar.Children.Add(_browse);
        _refresh = ActionButton("刷新", Refresh, "刷新本地资源");
        Grid.SetColumn(_refresh, 4);
        toolbar.Children.Add(_refresh);
        Grid.SetRow(toolbar, 1);
        layout.Children.Add(toolbar);

        var panels = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,12,*") };
        var listBody = new Grid();
        AutomationProperties.SetName(_list, "本地资源列表");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
        });
        _list.ItemTemplate = new FuncDataTemplate<InstanceContent>((item, _) => item is null ? null : BuildRow(item));
        _list.SelectionChanged += (_, _) => UpdateDetails();
        listBody.Children.Add(_list);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.Margin = new Thickness(12);
        listBody.Children.Add(_empty);
        panels.Children.Add(PageStyles.Card(listBody, new Thickness(7)));

        var detailBody = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        detailBody.Children.Add(new ScrollViewer
        {
            Content = _details,
            Padding = new Thickness(0, 0, 6, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        var detailActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        _toggle = ActionButton("禁用", () => UiOperation.Start(ToggleAsync, () => _closed), "启用或禁用资源");
        _largePreview = ActionButton("查看大图", () => UiOperation.Start(ShowScreenshotAsync, () => _closed));
        _delete = ActionButton("删除…", () => UiOperation.Start(DeleteAsync, () => _closed), "删除资源");
        _delete.Foreground = Palette.Error;
        detailActions.Children.Add(_toggle);
        detailActions.Children.Add(_largePreview);
        detailActions.Children.Add(_delete);
        Grid.SetRow(detailActions, 1);
        detailBody.Children.Add(detailActions);
        var detailCard = PageStyles.Card(detailBody, new Thickness(16));
        Grid.SetColumn(detailCard, 2);
        panels.Children.Add(detailCard);
        Grid.SetRow(panels, 2);
        layout.Children.Add(panels);

        _status.Margin = new Thickness(0, 10, 0, 0);
        AutomationProperties.SetName(_status, "实例资源状态");
        Grid.SetRow(_status, 3);
        layout.Children.Add(_status);
        Content = layout;
        SelectKind(InstanceContentKind.Mod);
        Filter();
    }

    public void SetInstance(GameInstance? instance)
    {
        if (_closed) return;
        var changed = _instance?.Id != instance?.Id || !string.Equals(_instance?.EffectiveSettings.GameDirectory,
            instance?.EffectiveSettings.GameDirectory, WonderPath.Comparison);
        _instance = instance;
        if (changed)
        {
            _search.Text = "";
            ClearItems();
            Refresh();
        }
        UpdateActions();
    }

    public void SelectKind(InstanceContentKind kind)
    {
        foreach (var tab in _categories.Children.OfType<ToggleButton>())
            tab.IsChecked = tab.Tag is InstanceContentKind tabKind && tabKind == kind;
        _search.PlaceholderText = "搜索" + CategoryName(kind);
        AutomationProperties.SetName(_search, _search.PlaceholderText);
        if (_kind != kind)
        {
            _kind = kind;
            ClearItems();
            Refresh();
        }
        UpdateActions();
    }

    public void Refresh() => UiOperation.Start(() => ReadAsync(), () => _closed);

    private async Task ReadAsync(string? selection = null, bool? selectionEnabled = null)
    {
        _read?.Cancel();
        if (_instance is not { } instance)
        {
            _read = null;
            SetStatus("");
            return;
        }

        var kind = _kind;
        using var request = new CancellationTokenSource();
        _read = request;
        SetStatus("正在读取" + CategoryName(kind) + "…");
        UpdateActions();
        try
        {
            var items = await _service.ReadContentAsync(instance.Id, kind, request.Token);
            if (request.IsCancellationRequested) return;
            _items = items;
            SetStatus("");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!request.IsCancellationRequested)
                SetStatus("无法读取" + CategoryName(kind) + "：" + ex.Message, error: true);
        }
        finally
        {
            if (ReferenceEquals(_read, request))
            {
                _read = null;
                Filter(selection, selectionEnabled);
            }
        }
    }

    private void ClearItems()
    {
        _items = [];
        Filter();
    }

    private void Filter(string? selection = null, bool? selectionEnabled = null)
    {
        var selectedPath = (_list.SelectedItem as InstanceContent)?.Path;
        var query = _search.Text?.Trim() ?? "";
        var visible = query.Length == 0 ? _items : _items.Where(item => Matches(item, query)).ToArray();
        _list.ItemsSource = visible;
        _list.SelectedItem = visible.FirstOrDefault(item => selection is null
                ? string.Equals(item.Path, selectedPath, WonderPath.Comparison)
                : item.Enabled == selectionEnabled && string.Equals(item.FileName, selection, WonderPath.Comparison))
            ?? visible.FirstOrDefault();
        _empty.IsVisible = visible.Count == 0;
        _empty.Text = _instance is null ? "选择实例后查看资源" : _read is not null ? "正在读取…" :
            _status.Foreground == Palette.Error ? "未能读取资源" :
            query.Length == 0 || _items.Count == 0 ? "此实例还没有" + CategoryName(_kind) : "没有符合搜索条件的资源";
        if (_read is null && _status.Foreground != Palette.Error)
            SetStatus(_instance is null ? "" : $"显示 {visible.Count} / {_items.Count} 项");
        UpdateDetails();
    }

    private static bool Matches(InstanceContent item, string query) =>
        item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        item.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        item.Details.Any(detail => detail.Value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private Control BuildRow(InstanceContent item)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("36,10,*,12,Auto"),
            MinHeight = 50,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(Preview(item, 36));
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(SingleLine(item.Title, semiBold: true));
        text.Children.Add(SingleLine(item.FileName));
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        if (item.Kind != InstanceContentKind.Screenshot)
        {
            var state = SingleLine(StateText(item));
            state.Foreground = item.Enabled ? Palette.Accent : Palette.Muted;
            state.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(state, 4);
            row.Children.Add(state);
        }
        AutomationProperties.SetName(row, item.Kind == InstanceContentKind.Screenshot ? item.Title : item.Title + "，" + StateText(item));
        return row;
    }

    private void UpdateDetails()
    {
        _details.Children.Clear();
        if (_list.SelectedItem is InstanceContent item)
        {
            var screenshot = item.Kind == InstanceContentKind.Screenshot;
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions(screenshot ? "*" : "60,12,*") };
            if (!screenshot) header.Children.Add(Preview(item, 60));
            var title = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock
            {
                Text = item.Title, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text,
                TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!screenshot)
            {
                var state = PageStyles.Hint(CategoryName(item.Kind) + " · " + StateText(item));
                state.Foreground = item.Enabled ? Palette.Accent : Palette.Muted;
                title.Children.Add(state);
                Grid.SetColumn(title, 2);
            }
            header.Children.Add(title);
            _details.Children.Add(header);
            if (screenshot)
                _details.Children.Add(Preview(item, 60, stretch: true));
            if (!string.IsNullOrWhiteSpace(item.Description))
                _details.Children.Add(new SelectableTextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted });
            foreach (var detail in item.Details) AddDetail(detail.Label, detail.Value);
            AddDetail(item.IsDirectory ? "目录名称" : "文件名称", item.FileName);
            if (item.Size is { } size) AddDetail("文件大小", size.ToString("N0", CultureInfo.CurrentCulture) + " 字节");
            AddDetail("修改时间", item.ModifiedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            AddDetail("本地路径", item.Path);
        }
        else
        {
            _details.Children.Add(PageStyles.Hint("选择一项资源查看详情"));
        }
        UpdateActions();
    }

    private void AddDetail(string label, string value)
    {
        _details.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                PageStyles.Hint(label),
                new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Palette.Text }
            }
        });
    }

    private Control Preview(InstanceContent item, double size, bool stretch = false)
    {
        var icon = new ResourceIconView(size);
        if (stretch)
        {
            icon.Width = double.NaN;
            icon.Height = 170;
            icon.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        icon.SetLocal(item.Kind, null);
        if (item.IconPath is null || _instance is not { } instance) return icon;
        CancellationTokenSource? loading = null;
        icon.AttachedToVisualTree += (_, _) => UiOperation.Start(async () =>
        {
            using var request = new CancellationTokenSource();
            loading = request;
            try
            {
                await _previewReads.WaitAsync(request.Token);
                try
                {
                    var bytes = await _service.ReadContentIconAsync(instance.Id, item, request.Token);
                    if (!_closed && !request.IsCancellationRequested) icon.SetLocal(item.Kind, bytes);
                }
                finally { _previewReads.Release(); }
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            finally
            {
                if (ReferenceEquals(loading, request)) loading = null;
            }
        }, () => _closed);
        icon.DetachedFromVisualTree += (_, _) =>
        {
            loading?.Cancel();
            icon.SetLocal(item.Kind, null);
        };
        return icon;
    }

    private async Task ToggleAsync()
    {
        if (_working || _closed || _read is not null || _service.IsBusy || _instance is not { } instance ||
            _list.SelectedItem is not InstanceContent item || item.Kind == InstanceContentKind.Screenshot) return;
        _working = true;
        UpdateActions();
        SetStatus("正在修改资源状态…");
        try
        {
            if (await _service.SetContentEnabledAsync(instance.Id, item, !item.Enabled))
            {
                if (IsCurrent(instance, item.Kind)) await ReadAsync(item.FileName, !item.Enabled);
            }
            else if (IsCurrent(instance, item.Kind))
            {
                SetStatus(_service.Error ?? "资源状态未修改。", error: true);
            }
        }
        finally { _working = false; UpdateActions(); }
    }

    private async Task ShowScreenshotAsync()
    {
        if (_closed || _instance is not { } instance ||
            _list.SelectedItem is not InstanceContent { Kind: InstanceContentKind.Screenshot, IconPath: not null } item) return;

        using var lifetime = new CancellationTokenSource();
        var image = new Image { Stretch = Stretch.Uniform };
        var message = PageStyles.Hint("正在读取截图…");
        message.HorizontalAlignment = HorizontalAlignment.Center;
        message.VerticalAlignment = VerticalAlignment.Center;
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20),
            Children = { image, message }
        };
        var dialog = new Window
        {
            Title = item.FileName + " · 截图预览", Width = 920, Height = 640, MinWidth = 420, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Palette.Background,
            Foreground = Palette.Text, FontFamily = _owner.FontFamily, Content = body
        };
        WonderMark.ApplyIcon(dialog);
        var close = ActionButton("关闭", dialog.Close, "关闭截图预览");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(close, 1);
        body.Children.Add(close);
        Bitmap? bitmap = null;
        dialog.Opened += (_, _) => UiOperation.Start(async () =>
        {
            try
            {
                var token = lifetime.Token;
                var bytes = await _service.ReadContentIconAsync(instance.Id, item, token);
                if (bytes is not null)
                {
                    var decoded = await Task.Run(() => ResourceIconView.DecodeLocal(bytes, 1920, 1080), token);
                    if (token.IsCancellationRequested) { decoded?.Dispose(); return; }
                    bitmap = decoded;
                }
                if (token.IsCancellationRequested) return;
                image.Source = bitmap;
                message.IsVisible = bitmap is null;
                message.Text = "无法预览这张截图。";
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (!lifetime.IsCancellationRequested) message.Text = "无法读取截图：" + error.Message;
            }
        });
        dialog.Closed += (_, _) =>
        {
            lifetime.Cancel();
            image.Source = null;
            bitmap?.Dispose();
        };
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Escape) return;
            dialog.Close();
            args.Handled = true;
        };
        await dialog.ShowDialog(_owner);
    }

    private async Task DeleteAsync()
    {
        if (_working || _closed || _read is not null || _service.IsBusy || _instance is not { } instance ||
            _list.SelectedItem is not InstanceContent item) return;
        _working = true;
        UpdateActions();
        try
        {
            var description = item.Kind switch
            {
                InstanceContentKind.World => "这会移走整个世界的存档，包括区块、玩家位置和进度。",
                InstanceContentKind.Screenshot => "这会从游戏的截图目录移走这张截图。",
                _ => "这会从游戏目录移走此资源。"
            };
            var dialog = new DeleteConfirmationWindow(item.Title, item.Path, description, async _ =>
            {
                var deleted = await _service.DeleteContentAsync(instance.Id, item);
                return deleted ? null : _service.Error ?? "删除未完成。";
            }, instructions: "请核对文件或目录路径，然后输入小写 sure 确认移入系统回收站。") { FontFamily = _owner.FontFamily };
            if (await dialog.ShowDialog<bool>(_owner) && IsCurrent(instance, item.Kind)) await ReadAsync();
        }
        finally { _working = false; UpdateActions(); }
    }

    private bool IsCurrent(GameInstance instance, InstanceContentKind kind) =>
        !_closed && _instance is { } current && current.Id == instance.Id && _kind == kind &&
        string.Equals(current.EffectiveSettings.GameDirectory, instance.EffectiveSettings.GameDirectory, WonderPath.Comparison);

    private void UpdateActions()
    {
        var canRead = _instance is not null && !_working;
        _categories.IsEnabled = _search.IsEnabled = _refresh.IsEnabled = _list.IsEnabled = canRead;
        _browse.IsVisible = _browseResources is not null && _kind is not (InstanceContentKind.World or InstanceContentKind.Screenshot);
        _browse.IsEnabled = canRead && !_service.IsBusy;
        var item = _list.SelectedItem as InstanceContent;
        _delete.IsVisible = item is not null;
        _toggle.IsVisible = item is not null && item.Kind != InstanceContentKind.Screenshot;
        _largePreview.IsVisible = item?.Kind == InstanceContentKind.Screenshot;
        _largePreview.IsEnabled = canRead && _read is null && item?.IconPath is not null;
        _toggle.IsEnabled = _delete.IsEnabled = canRead && _read is null && !_service.IsBusy;
        _toggle.Content = item?.Kind == InstanceContentKind.World
            ? item.Enabled ? "隐藏世界" : "显示世界"
            : item?.Enabled == true ? "禁用" : "启用";
        ToolTip.SetTip(_toggle, item?.Kind == InstanceContentKind.World ? "控制此存档是否出现在游戏的单人世界列表。" : null);
    }

    private void SetStatus(string message, bool error = false)
    {
        _status.Text = message;
        _status.Foreground = error ? Palette.Error : Palette.Muted;
    }

    private static string CategoryName(InstanceContentKind kind) => kind switch
    {
        InstanceContentKind.Mod => "模组",
        InstanceContentKind.ResourcePack => "资源包（材质）",
        InstanceContentKind.ShaderPack => "光影包",
        InstanceContentKind.World => "地图（存档）",
        InstanceContentKind.Screenshot => "截图",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string StateText(InstanceContent item) => item.Kind == InstanceContentKind.World
        ? item.Enabled ? "地图可见" : "地图已隐藏"
        : item.Enabled ? "已启用" : "已禁用";

    private static TextBlock SingleLine(string text, bool semiBold = false) => new()
    {
        Text = text, Foreground = semiBold ? Palette.Text : Palette.Muted, FontSize = semiBold ? 14 : 12,
        FontWeight = semiBold ? FontWeight.SemiBold : FontWeight.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap
    };

    private static Button ActionButton(string text, Action action, string? automationName = null)
    {
        var button = new Button { Content = text, MinHeight = 34 };
        AutomationProperties.SetName(button, automationName ?? text);
        button.Click += (_, _) => action();
        return button;
    }

    public void Dispose()
    {
        _closed = true;
        _read?.Cancel();
        _read = null;
        _instance = null;
        _items = [];
        _list.ItemsSource = null;
        _details.Children.Clear();
    }
}
