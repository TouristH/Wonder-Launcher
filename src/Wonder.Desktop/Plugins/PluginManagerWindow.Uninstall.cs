// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Wonder.Extensibility;

namespace Wonder.Desktop;

internal sealed partial class PluginManagerWindow
{
    private Task DeletePluginAsync(InstalledPlugin plugin) => RunAsync("请确认要删除的插件。", async token =>
    {
        var accepted = await ConfirmDeletePluginAsync(plugin, token);
        token.ThrowIfCancellationRequested();
        if (!accepted)
        {
            ShowStatus("已取消删除插件。");
            return;
        }

        ShowStatus("正在停止并删除 " + plugin.Manifest.Name + "…");
        _cancel.IsVisible = false;
        await Task.Run(() => _manager.Uninstall(plugin.Manifest.Id), token);
        _downloadedPackages.Remove(plugin.Manifest.Id);
        ShowStatus("已删除 " + plugin.Manifest.Name + "，配置和数据已保留。");
    });

    private async Task<bool> ConfirmDeletePluginAsync(InstalledPlugin plugin, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new Window
        {
            Title = "删除插件 · " + plugin.Manifest.Name,
            Width = 560, Height = 380, MinWidth = 460, MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.Background, Foreground = Palette.Text, FontFamily = FontFamily
        };
        WonderMark.ApplyIcon(dialog);
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        var details = new StackPanel { Spacing = 12 };
        details.Children.Add(PageStyles.DialogTitle("删除已安装插件？"));
        details.Children.Add(Heading(plugin.Manifest.Name));
        details.Children.Add(Hint(plugin.Manifest.Version + " · " + plugin.Manifest.Id));
        details.Children.Add(PageStyles.Code(plugin.Directory, selectable: true));
        details.Children.Add(Hint("将停止此插件并删除上述目录中的程序文件。重新使用时需要再次安装并确认启用。"));
        details.Children.Add(Hint("插件配置和数据会保留，已有桌面卡片的位置与状态也会保留；重新安装并启用后可恢复。"));
        layout.Children.Add(Scroll(details));
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = ActionButton("取消", () => dialog.Close(false), "取消删除插件");
        cancel.IsCancel = true;
        cancel.IsDefault = true;
        actions.Children.Add(cancel);
        var confirm = ActionButton("删除插件", () => dialog.Close(true), "确认删除插件");
        confirm.Foreground = Palette.Error;
        actions.Children.Add(confirm);
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        dialog.Content = layout;
        using var cancellation = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await dialog.ShowDialog<bool>(this);
    }
}
