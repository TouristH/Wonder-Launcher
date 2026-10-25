// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Application;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed class LaunchStatusWindow : Window, IDisposable
{
    private readonly InstanceService _service;
    private readonly TextBlock _status = PageStyles.Hint("");
    private readonly TextBox _logs = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _cancel = new() { Content = "取消启动准备" };
    private int _pending;
    private bool _disposed;

    public LaunchStatusWindow(InstanceService service)
    {
        _service = service;
        Title = "启动状态 · Wonder"; Width = 740; Height = 520; MinWidth = 480; MinHeight = 320;
        Background = Palette.Background;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(22), RowSpacing = 14 };
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(new TextBlock { Text = "启动状态", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text });
        AutomationProperties.SetName(_status, "当前启动状态"); header.Children.Add(_status);
        layout.Children.Add(header);
        AutomationProperties.SetName(_logs, "最近启动日志");
        Grid.SetRow(_logs, 1); layout.Children.Add(_logs);
        var footer = new StackPanel { Spacing = 10 };
        footer.Children.Add(PageStyles.Hint("保留最近 200 行日志。关闭此窗口后，启动准备和游戏继续运行。"));
        AutomationProperties.SetName(_cancel, "取消启动准备");
        _cancel.HorizontalAlignment = HorizontalAlignment.Left;
        _cancel.Click += (_, _) => _service.CancelPreparation(); footer.Children.Add(_cancel);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;
        service.Changed += ServiceChanged;
        Closed += (_, _) => Dispose();
        Refresh();
    }

    private void ServiceChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else if (Interlocked.Exchange(ref _pending, 1) == 0) Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _pending, 0);
            Refresh();
        });
    }

    private void Refresh()
    {
        if (_disposed) return;
        _status.Text = _service.Error is { } error ? _service.StatusMessage + "\n" + error : _service.StatusMessage;
        _status.Foreground = _service.Error is null ? Palette.Muted : Palette.Error;
        var text = string.Join(Environment.NewLine, _service.LogLines);
        if (_logs.Text != text) { _logs.Text = text; _logs.CaretIndex = text.Length; }
        _cancel.IsVisible = _service.LaunchPhase == LaunchPhase.Preparing;
        _cancel.IsEnabled = _cancel.IsVisible;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _service.Changed -= ServiceChanged;
    }
}
