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
            var heading = new StackPanel { Spacing = 8 };
            heading.Children.Add(Hint("保存后，正在运行的插件会使用新配置重新启用。"));
            layout.Children.Add(heading);
            var input = new TextBox
            {
                Text = _manager.ReadConfiguration(plugin.Manifest.Id), AcceptsReturn = true, AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
                VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 14, 0, 12)
            };
            AutomationProperties.SetName(input, "插件 JSON 配置");
            ScrollViewer.SetHorizontalScrollBarVisibility(input, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto);
            var content = new ContentControl { Content = input };
            Grid.SetRow(content, 1);
            layout.Children.Add(content);
            var status = Hint("");
            status.Foreground = Palette.Error;
            AutomationProperties.SetName(status, "插件配置错误");
            Grid.SetRow(status, 2);
            layout.Children.Add(status);
            PluginConfigurationForm? form = null;
            if (plugin.Manifest.ConfigurationSchema is { } schema)
            {
                var toggle = ActionButton("编辑 JSON", () => { });
                void UseForm()
                {
                    form = new PluginConfigurationForm(schema, input.Text ?? "");
                    content.Content = new ScrollViewer { Content = form, Margin = new Thickness(0, 14, 0, 12),
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                    toggle.Content = "编辑 JSON";
                    AutomationProperties.SetName(toggle, "编辑 JSON");
                    status.Text = "";
                }
                toggle.Click += (_, _) =>
                {
                    try
                    {
                        if (form is null) UseForm();
                        else
                        {
                            input.Text = form.Serialize();
                            form = null;
                            content.Content = input;
                            toggle.Content = "可视化编辑";
                            AutomationProperties.SetName(toggle, "可视化编辑");
                            status.Text = "";
                        }
                    }
                    catch (Exception error) { status.Text = error.Message; }
                };
                heading.Children.Add(toggle);
                try { UseForm(); }
                catch (InvalidDataException error)
                {
                    toggle.Content = "可视化编辑";
                    AutomationProperties.SetName(toggle, "可视化编辑");
                    status.Text = "当前配置不符合声明，请先在 JSON 中修正：" + error.Message;
                }
            }
            else heading.Children.Add(Hint("此插件未提供配置字段声明，使用 JSON 编辑。"));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(ActionButton("取消", editor.Close));
            buttons.Children.Add(ActionButton("保存配置", () =>
            {
                try
                {
                    _manager.SaveConfiguration(plugin.Manifest.Id, form?.Serialize() ?? input.Text ?? "");
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
