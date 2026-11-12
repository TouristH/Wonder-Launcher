// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Wonder.Extensibility;

namespace Wonder.Desktop;

internal sealed partial class PluginManagerWindow
{
    private Task SetPluginEnabledAsync(InstalledPlugin plugin) => RunAsync("正在切换插件状态…", async token =>
    {
        var request = plugin.Enabled ? null : await Task.Run(() => _manager.GetConsentRequest(plugin.Manifest.Id), token);
        token.ThrowIfCancellationRequested();
        if (request is not null)
        {
            var accepted = await ConfirmPluginAsync(request);
            token.ThrowIfCancellationRequested();
            if (!accepted)
            {
                ShowStatus("未同意启用，插件保持关闭。");
                return;
            }
            _manager.AcceptAndEnable(plugin.Manifest.Id, request.Fingerprint);
        }
        else
            _manager.SetEnabled(plugin.Manifest.Id, !plugin.Enabled);
        var current = _manager.Plugins.FirstOrDefault(item => item.Manifest.Id == plugin.Manifest.Id);
        ShowStatus(current?.Error ?? (current?.Enabled == true ? "插件已启用。" : "插件已关闭。"), current?.Error is not null);
    });

    private Task<bool> ConfirmPluginAsync(PluginConsentRequest request)
    {
        var manifest = request.Manifest;
        var dialog = new Window
        {
            Title = "启用插件前确认 · " + manifest.Name,
            Width = 700, Height = 650, MinWidth = 500, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.Background, Foreground = Palette.Text, FontFamily = FontFamily
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        var title = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 18) };
        title.Children.Add(Heading("启用前请了解这个插件", 23));
        title.Children.Add(Hint($"{manifest.Name} · {manifest.Version} · {manifest.Author}"));
        var notice = Hint("同意后，插件可用当前系统用户权限访问文件、联网和启动程序。分析与人工验证不保证安全。不同意则保持关闭。");
        notice.Foreground = Palette.PluginUnverified;
        title.Children.Add(notice);
        layout.Children.Add(title);
        var details = new StackPanel { Spacing = 12 };
        var reviewed = _downloadedPackages.TryGetValue(manifest.Id, out var downloaded) && downloaded.Fingerprint == request.Fingerprint
            ? _catalog.FirstOrDefault(entry => entry.Id == manifest.Id && entry.Version == manifest.Version &&
                string.Equals(entry.Sha256, downloaded.Sha256, StringComparison.OrdinalIgnoreCase))
            : null;
        if (reviewed is not null)
            details.Children.Add(ReviewLabel(reviewed.Review));
        else
        {
            var status = Hint("人工验证：尚未确认此本地安装包的验证记录。");
            status.Foreground = Palette.PluginUnverified;
            details.Children.Add(status);
        }
        details.Children.Add(Heading("启动器静态分析"));
        if (request.Analysis.Findings.Count == 0)
            details.Children.Add(Hint("未识别到常见敏感调用；这不表示插件没有这些能力。"));
        else
            foreach (var finding in request.Analysis.Findings)
                details.Children.Add(Hint("• " + finding));
        foreach (var limitation in request.Analysis.Limitations)
            details.Children.Add(Hint(limitation));
        details.Children.Add(Hint("静态分析不能完整识别反射、动态代码和本地库的行为。"));
        if (manifest.Permissions is { Length: > 0 } permissions)
        {
            details.Children.Add(Heading("作者补充说明"));
            foreach (var permission in permissions)
            {
                details.Children.Add(Heading(permission.Name, 14));
                details.Children.Add(Hint(permission.Description));
            }
        }
        var warning = new StackPanel { Spacing = 8 };
        warning.Children.Add(Heading("同意后，插件将以当前系统用户权限运行", 15));
        warning.Children.Add(Hint("插件可以访问文件、网络并启动其他程序。Wonder 不对这些行为逐项限制。插件可能读取或修改你的数据，请自行决定是否信任它。"));
        warning.Children.Add(Hint("静态分析、作者说明和人工验证均不构成安全保证。不同意则保持关闭。"));
        details.Children.Add(Card(warning));
        var scroll = Scroll(details);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var decline = ActionButton("不同意，保持关闭", () => dialog.Close(false));
        decline.IsCancel = true;
        buttons.Children.Add(decline);
        buttons.Children.Add(ActionButton("同意并启用", () => dialog.Close(true)));
        Grid.SetRow(buttons, 2);
        layout.Children.Add(buttons);
        dialog.Content = layout;
        return dialog.ShowDialog<bool>(this);
    }
}
