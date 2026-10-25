// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wonder.Desktop;

internal sealed class DeleteConfirmationWindow : Window
{
    private bool _working;

    public DeleteConfirmationWindow(string title, string path, string description, Func<string, Task<string?>> delete)
    {
        Title = "删除确认 · Wonder Launcher"; Width = 560; Height = 440; MinWidth = 460; MinHeight = 360;
        Background = Palette.Background; Foreground = Palette.Text; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "移入系统回收站", FontSize = 23, FontWeight = FontWeight.SemiBold, Foreground = Palette.Error });
        content.Children.Add(new TextBlock { Text = title, FontSize = 16, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new SelectableTextBlock { Text = path, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Accent });
        content.Children.Add(PageStyles.Hint(description));
        content.Children.Add(PageStyles.Hint("将移走上述目录中的文件并移除登记。请核对路径，然后准确输入小写 sure。取消不会修改文件。"));
        var word = new TextBox { MaxLength = 32, PlaceholderText = "sure" }; AutomationProperties.SetName(word, "删除确认词"); content.Children.Add(word);
        var error = PageStyles.Hint(""); error.Foreground = Palette.Error; content.Children.Add(error);
        layout.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "取消" }; AutomationProperties.SetName(cancel, "取消删除"); cancel.Click += (_, _) => Close(false);
        var confirm = new Button { Content = "移入回收站", IsEnabled = false }; AutomationProperties.SetName(confirm, "确认移入回收站");
        word.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) confirm.IsEnabled = !_working && word.Text == "sure"; };
        confirm.Click += async (_, _) =>
        {
            if (_working || word.Text != "sure") return;
            _working = true; confirm.IsEnabled = cancel.IsEnabled = word.IsEnabled = false;
            try
            {
                var problem = await delete(word.Text);
                if (problem is null) { _working = false; Close(true); return; }
                error.Text = problem;
            }
            catch (Exception ex) { error.Text = ex.Message; }
            finally { _working = false; cancel.IsEnabled = word.IsEnabled = true; confirm.IsEnabled = word.Text == "sure"; }
        };
        actions.Children.Add(cancel); actions.Children.Add(confirm); Grid.SetRow(actions, 1); layout.Children.Add(actions); Content = layout;
        Closing += (_, e) => { if (_working) e.Cancel = true; };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape && !_working) { Close(false); e.Handled = true; } };
    }
}
