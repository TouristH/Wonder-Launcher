// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Wonder.Application;

namespace Wonder.Desktop;

internal sealed class LaunchCrashWindow : Window
{
    private bool _saving;

    private LaunchCrashWindow(LaunchCrashReport report)
    {
        Title = report.ExitCode is null ? "游戏出错" : "游戏异常退出";
        Width = 550;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Background;
        Foreground = Palette.Text;

        var details = new StackPanel { Spacing = 10 };
        details.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeight.SemiBold });
        details.Children.Add(PageStyles.Hint(report.ExitCode is { } code ? $"{report.InstanceName} · 退出代码 {code}" : report.InstanceName));
        details.Children.Add(PageStyles.Hint(report.Reason));
        var prompt = PageStyles.Hint("是否将本次启动记录、游戏日志、崩溃报告和运行环境信息打包成 ZIP 保存到本机？");
        details.Children.Add(prompt);

        var path = new SelectableTextBlock { Text = report.DestinationDirectory, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Text };
        AutomationProperties.SetName(path, "错误日志保存位置");
        var pathInfo = new StackPanel { Spacing = 4 };
        pathInfo.Children.Add(PageStyles.Hint("保存位置"));
        pathInfo.Children.Add(path);
        details.Children.Add(PageStyles.Card(pathInfo));
        var status = PageStyles.Hint("");
        status.IsVisible = false;
        AutomationProperties.SetName(status, "错误日志保存状态");
        details.Children.Add(status);

        var dismiss = new Button { Content = "不保存", IsCancel = true, MinHeight = 34 };
        AutomationProperties.SetName(dismiss, "不保存错误日志");
        dismiss.Click += (_, _) => Close();
        var save = new Button { Content = "保存错误日志", IsDefault = true, MinHeight = 34 };
        AutomationProperties.SetName(save, "保存错误日志");
        var openDirectory = new Button { Content = "打开保存目录", IsVisible = false, MinHeight = 34 };
        AutomationProperties.SetName(openDirectory, "打开错误日志目录");
        openDirectory.Click += (_, _) =>
        {
            try { using var opened = Process.Start(new ProcessStartInfo(report.DestinationDirectory) { UseShellExecute = true }); }
            catch (Exception error) { ShowStatus("无法打开保存目录：" + error.Message, Palette.Error); }
        };

        save.Click += (_, _) => UiOperation.Start(async () =>
        {
            if (_saving || !save.IsVisible) return;
            _saving = true;
            dismiss.IsEnabled = save.IsEnabled = false;
            ShowStatus("正在打包错误日志…", Palette.Muted);
            try
            {
                path.Text = await report.SaveAsync();
                ShowStatus("错误日志已保存。", Palette.Accent);
                prompt.IsVisible = false;
                openDirectory.IsVisible = true;
                save.IsDefault = false;
                save.IsVisible = false;
                dismiss.Content = "关闭";
                dismiss.IsDefault = true;
                AutomationProperties.SetName(dismiss, "关闭错误日志窗口");
            }
            catch (Exception error) { ShowStatus("保存失败，请检查保存目录的权限和磁盘空间后重试。\n" + error.Message, Palette.Error); }
            finally
            {
                _saving = false;
                dismiss.IsEnabled = save.IsEnabled = true;
            }
        });
        Closing += (_, args) => { if (_saving) args.Cancel = true; };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(openDirectory);
        actions.Children.Add(dismiss);
        actions.Children.Add(save);
        var layout = new StackPanel { Margin = new Thickness(22), Spacing = 16 };
        layout.Children.Add(new ScrollViewer { Content = details, MaxHeight = 340, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        layout.Children.Add(actions);
        Content = layout;

        void ShowStatus(string text, IBrush foreground)
        {
            status.Text = text;
            status.Foreground = foreground;
            status.IsVisible = true;
        }
    }

    internal static Task OfferAsync(Window owner, LaunchCrashReport report)
    {
        return Dispatcher.UIThread.CheckAccess() ? Show() : Dispatcher.UIThread.InvokeAsync(Show);

        Task Show()
        {
            if (!owner.IsVisible) return Task.CompletedTask;
            if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
            return new LaunchCrashWindow(report) { FontFamily = owner.FontFamily }.ShowDialog(owner);
        }
    }
}
