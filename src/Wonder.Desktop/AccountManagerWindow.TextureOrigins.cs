// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class AccountManagerWindow
{
    private void BuildTextureOrigins(StackPanel body, AccountProfile profile)
    {
        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(Heading("纹理来源许可"));
        section.Children.Add(PageStyles.Hint("公网皮肤与披风无需添加。仅在信任来源时，明确允许它连接内网或回环地址。许可只适用于当前账户的此 HTTPS 主机与端口，不含子域；认证服务器地址不会自动获准。"));
        section.Children.Add(PageStyles.Hint($"已允许 {profile.AllowedPrivateTextureOrigins.Count} / {TextureOrigin.MaximumAllowedOrigins} 个来源"));
        foreach (var origin in profile.AllowedPrivateTextureOrigins)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new SelectableTextBlock { Text = origin, TextWrapping = TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var remove = IdleButton("移除许可", "移除纹理来源 " + origin, () => _service.SetPrivateTextureOrigin(profile.Id, origin, false));
            remove.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(remove, 1); row.Children.Add(remove); section.Children.Add(row);
        }
        var input = Field(section, "明确的 HTTPS 来源（不含路径）", "当前账户纹理 HTTPS 来源", "", 2048);
        input.PlaceholderText = "https://textures.example:443";
        section.Children.Add(IdleButton("允许当前账户使用此来源连接内网", "允许当前账户纹理来源", () => _service.SetPrivateTextureOrigin(profile.Id, (input.Text ?? "").Trim(), true)));
        body.Children.Add(PageStyles.Card(section, new Thickness(16, 12)));
    }
}
