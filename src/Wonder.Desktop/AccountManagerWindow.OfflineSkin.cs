// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class AccountManagerWindow
{
    private void BuildOfflineSkinPicker(StackPanel body, AccountProfile profile)
    {
        body.Children.Add(Heading("游戏默认皮肤"));
        body.Children.Add(PageStyles.Hint("通过离线 UUID 选择游戏内置皮肤，不安装模组、不上传图片。旧版和新版规则不同；联机服务器或皮肤模组可能覆盖此选择。"));
        var choices = OfflineDefaultSkins.Choices.ToArray();
        var selector = new ComboBox
        {
            ItemsSource = new[] { "自动分配 · 恢复按玩家名生成的 UUID" }.Concat(choices.Select(x => x.Name)).ToArray(),
            SelectedIndex = Array.FindIndex(choices, x => x.Id == profile.OfflineSkinId) + 1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(selector, "离线默认皮肤"); _idle.Add(selector); body.Children.Add(selector);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,16,*") };
        var preview = _preview = new AccountSkinPreview(_textureDownload) { Width = 160, Height = 208 };
        row.Children.Add(preview);
        var info = new StackPanel { Spacing = 10 }; Grid.SetColumn(info, 2); row.Children.Add(info);
        var compatibility = PageStyles.Hint(""); info.Children.Add(compatibility);
        var textureStatus = PageStyles.Hint(""); info.Children.Add(textureStatus);
        preview.LoadedTexture += () => textureStatus.Text = preview.Description;
        var uuid = new SelectableTextBlock { FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap }; AutomationProperties.SetName(uuid, "默认皮肤候选 UUID");
        info.Children.Add(PageStyles.Hint("应用后的 UUID")); info.Children.Add(uuid);
        body.Children.Add(PageStyles.Card(row, new Thickness(14)));
        var confirmation = new StackPanel { Spacing = 10, IsVisible = false };
        confirmation.Children.Add(PageStyles.Hint("修改 UUID 会改变游戏身份，原背包、成就及服务器权限可能不再对应。Wonder 不迁移或删除存档。确认使用上方新 UUID？"));
        string? DraftId() => selector.SelectedIndex > 0 ? choices[selector.SelectedIndex - 1].Id : null;
        void PreviewChoice()
        {
            confirmation.IsVisible = false;
            var draftId = DraftId(); var draftUuid = _service.GetOfflineSkinUuid(profile.Id, draftId); uuid.Text = draftUuid;
            var choice = draftId is null ? OfflineDefaultSkins.ForUuid(draftUuid) : OfflineDefaultSkins.Get(draftId);
            compatibility.Text = (draftId is null ? "自动分配。" : choice.Description + "\n") +
                "1.19.3+：" + OfflineDefaultSkins.ForUuid(draftUuid).Name + "\n1.8–1.19.2：" + OfflineDefaultSkins.LegacySkinName(draftUuid) + "\n早于 1.8 通常只有 Steve。";
            _ = preview.LoadDefaultAsync(_selectedInstance?.Invoke(), choice.TextureName, choice.Variant, choice.IsLegacy);
        }
        selector.SelectionChanged += (_, _) => PreviewChoice();
        body.Children.Add(IdleButton("应用默认皮肤…", "应用离线默认皮肤", () =>
        {
            if (DraftId() == profile.OfflineSkinId) { _status.Text = "已使用此默认皮肤选项。"; return; }
            confirmation.IsVisible = true;
        }));
        confirmation.Children.Add(IdleButton("确认更改皮肤与 UUID", "确认离线皮肤 UUID 变化", () => _service.SetOfflineSkin(profile.Id, DraftId())));
        confirmation.Children.Add(Button("保留当前身份", "取消离线皮肤变化", () => confirmation.IsVisible = false));
        body.Children.Add(confirmation); PreviewChoice();
    }
}
