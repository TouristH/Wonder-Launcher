// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Wonder.Extensibility;

namespace Wonder.Desktop;

internal sealed partial class PluginManagerWindow
{
    private readonly Dictionary<string, (string Fingerprint, string Sha256)> _downloadedPackages = new(StringComparer.Ordinal);

    private void RefreshAvailable()
    {
        _available.Children.Clear();
        if (_catalog.Count == 0)
        {
            _available.Children.Add(Hint("这个仓库暂时没有可下载的插件。"));
            return;
        }
        _available.Children.Add(Hint("人工验证按版本记录；所有已收录版本均可下载。启用前会展示插件分析结果与风险提示。"));
        foreach (var group in _catalog.GroupBy(entry => entry.Id))
        {
            var versions = group.OrderByDescending(entry => Version.Parse(entry.Version)).ToArray();
            var latest = versions[0];
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(Heading(latest.Name));
            body.Children.Add(Hint($"{latest.Author} · {latest.Id}"));
            body.Children.Add(Hint(latest.Description));
            var installed = _manager.Plugins.FirstOrDefault(plugin => plugin.Manifest.Id == latest.Id);
            if (installed is not null) body.Children.Add(Hint("本地版本 " + installed.Manifest.Version));
            foreach (var entry in versions)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
                var details = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 12, 0) };
                details.Children.Add(Heading(entry.Version, 14));
                details.Children.Add(ReviewLabel(entry.Review));
                row.Children.Add(details);
                var installedSameVersion = installed?.Manifest.Version == entry.Version;
                var download = ActionButton(installedSameVersion ? "已安装" : "安装此版本",
                    () => UiOperation.Start(() => InstallCatalogEntryAsync(entry), () => _closed),
                    $"安装在线插件 {entry.Name} {entry.Version}");
                download.IsEnabled = !installedSameVersion;
                download.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(download, 1);
                row.Children.Add(download);
                body.Children.Add(row);
            }
            body.Children.Add(Hint("项目来源 " + latest.SourceUrl));
            _available.Children.Add(Card(body));
        }
    }

    private Task InstallCatalogEntryAsync(PluginCatalogEntry entry) => RunAsync("正在下载并校验 " + entry.Name + "…", async token =>
    {
        await _manager.InstallAsync(entry, token);
        _downloadedPackages.Remove(entry.Id);
        if (await Task.Run(() => _manager.GetConsentRequest(entry.Id), token) is { } request)
            _downloadedPackages[entry.Id] = (request.Fingerprint, entry.Sha256);
        ShowStatus($"{entry.Name} {entry.Version} 已安装，可在“已安装”中启用。");
    });

    private static TextBlock ReviewLabel(PluginReview? review)
    {
        var label = Hint(review switch
        {
            { Status: "verified" } value => $"人工验证：已验证 · {value.Reviewer} · {value.ReviewedAt:yyyy-MM-dd}",
            { Status: "revoked" } value => $"人工验证：已撤销 · {value.Reviewer} · {value.ReviewedAt:yyyy-MM-dd}",
            _ => "人工验证：未验证"
        });
        label.Foreground = review?.Status == "verified" ? Palette.PluginVerified : Palette.PluginUnverified;
        return label;
    }
}
