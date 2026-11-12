// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Extensibility;

namespace Wonder.Desktop;

internal sealed partial class PluginManagerWindow
{
    private async Task EditConfigurationAsync(InstalledPlugin plugin)
    {
        if (_closed || _busy) return;
        try
        {
            var editor = new Window
            {
                Title = "修改配置 · " + plugin.Manifest.Name, Width = 680, Height = 520, MinWidth = 480, MinHeight = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Palette.Background,
                Foreground = Palette.Text, FontFamily = FontFamily
            };
            var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(22) };
            layout.Children.Add(Hint("编辑插件的 JSON 配置。保存后，正在运行的插件会使用新配置重新启用。"));
            var input = new TextBox
            {
                Text = _manager.ReadConfiguration(plugin.Manifest.Id), AcceptsReturn = true, AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
                VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 14, 0, 12)
            };
            AutomationProperties.SetName(input, "插件 JSON 配置");
            ScrollViewer.SetHorizontalScrollBarVisibility(input, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto);
            Grid.SetRow(input, 1);
            layout.Children.Add(input);
            var status = Hint("");
            status.Foreground = Palette.Error;
            AutomationProperties.SetName(status, "插件配置错误");
            Grid.SetRow(status, 2);
            layout.Children.Add(status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(ActionButton("取消", editor.Close));
            buttons.Children.Add(ActionButton("保存配置", () =>
            {
                try
                {
                    _manager.SaveConfiguration(plugin.Manifest.Id, input.Text ?? "");
                    var current = _manager.Plugins.FirstOrDefault(item => item.Manifest.Id == plugin.Manifest.Id);
                    ShowStatus(current?.Error ?? "插件配置已保存。", current?.Error is not null);
                    editor.Close();
                }
                catch (Exception error) { status.Text = error.Message; }
            }));
            Grid.SetRow(buttons, 3);
            layout.Children.Add(buttons);
            editor.Content = layout;
            await editor.ShowDialog(this);
            if (!_closed) RefreshInstalled();
        }
        catch (Exception error) { ShowStatus(error.Message, true); }
    }
}
