// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wonder.Desktop;

internal sealed class DirectorySettingsWindow : Window
{
    private bool _working;

    public DirectorySettingsWindow(string path, Func<string, Task<string?>> move)
    {
        Title = "游戏目录设置 · Wonder Launcher"; Width = 610; Height = 480; MinWidth = 480; MinHeight = 400;
        Background = Palette.Background; Foreground = Palette.Text; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "游戏目录设置", FontSize = 23, FontWeight = FontWeight.SemiBold });
        content.Children.Add(PageStyles.Hint("将实际移动或重命名文件夹，并更新 Wonder 中的实例路径。修改前请关闭使用此目录的游戏和其他启动器。"));
        content.Children.Add(new SelectableTextBlock { Text = "当前：" + path, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted });
        content.Children.Add(PageStyles.Hint("父目录路径"));
        var parent = new TextBox { Text = Path.GetDirectoryName(path), MaxLength = 32767 }; AutomationProperties.SetName(parent, "目录父路径"); content.Children.Add(parent);
        content.Children.Add(PageStyles.Hint("目录昵称（实际文件夹名）"));
        var name = new TextBox { Text = Path.GetFileName(path), MaxLength = 120 }; AutomationProperties.SetName(name, "目录昵称"); content.Children.Add(name);
        var preview = PageStyles.Hint(""); preview.Foreground = Palette.Accent; content.Children.Add(preview);
        var error = PageStyles.Hint(""); error.Foreground = Palette.Error; content.Children.Add(error);
        string Target()
        {
            var folderName = name.Text ?? ""; var parentPath = parent.Text ?? "";
            if (string.IsNullOrWhiteSpace(folderName) || folderName is "." or ".." || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folderName.EndsWith('.') || folderName.EndsWith(' '))
                throw new ArgumentException("目录昵称必须是有效的单个文件夹名称。");
            if (!Path.IsPathFullyQualified(parentPath)) throw new ArgumentException("父目录必须填写完整路径。");
            return Path.GetFullPath(Path.Combine(parentPath, folderName));
        }
        void Preview() { try { preview.Text = "修改后：" + Target(); } catch { preview.Text = "请填写有效的目录路径和昵称。"; } }
        parent.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Preview(); }; name.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Preview(); }; Preview();
        layout.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "取消" }; AutomationProperties.SetName(cancel, "取消目录修改"); cancel.Click += (_, _) => Close(false);
        var save = new Button { Content = "保存并修改文件夹" }; AutomationProperties.SetName(save, "保存目录设置");
        save.Click += async (_, _) =>
        {
            if (_working) return;
            _working = true; save.IsEnabled = cancel.IsEnabled = parent.IsEnabled = name.IsEnabled = false;
            try
            {
                var problem = await move(Target());
                if (problem is null) { _working = false; Close(true); return; }
                error.Text = problem;
            }
            catch (Exception ex) { error.Text = ex.Message; }
            finally { _working = false; save.IsEnabled = cancel.IsEnabled = parent.IsEnabled = name.IsEnabled = true; }
        };
        actions.Children.Add(cancel); actions.Children.Add(save); Grid.SetRow(actions, 1); layout.Children.Add(actions); Content = layout;
        Closing += (_, e) => { if (_working) e.Cancel = true; };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape && !_working) { Close(false); e.Handled = true; } };
    }
}
