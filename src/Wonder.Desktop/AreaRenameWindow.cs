// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Wonder.Desktop;

internal sealed class AreaRenameWindow : Window
{
    public AreaRenameWindow(string title)
    {
        Title = "重命名功能区";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Background;
        Foreground = Palette.Text;

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "功能区名称", FontSize = 16, FontWeight = FontWeight.SemiBold });
        var input = new TextBox { Text = title, PlaceholderText = "输入功能区名称", AcceptsReturn = false };
        AutomationProperties.SetName(input, "功能区名称");
        body.Children.Add(input);
        body.Children.Add(new TextBlock { Text = "1–120 个字符，名称随布局自动保存。", Foreground = Palette.Muted, FontSize = 12 });
        var error = new TextBlock { Foreground = Palette.Error, FontSize = 12, MinHeight = 18, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(error, "名称校验提示");
        body.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var cancel = new Button { Content = "取消", IsCancel = true };
        var save = new Button { Content = "保存", IsDefault = true };
        cancel.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            var name = input.Text?.Trim() ?? "";
            if (name.Length == 0) error.Text = "请输入功能区名称，不能只包含空格。";
            else if (name.Length > 120) error.Text = "名称不能超过 120 个字符。";
            else if (name.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029')) error.Text = "名称须为单行，不能包含换行或控制字符。";
            else { Close(name); return; }
            input.Focus();
        };
        input.TextChanged += (_, _) => error.Text = "";
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        body.Children.Add(actions);
        Content = body;
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            input.Focus();
            input.SelectAll();
        });
    }
}
