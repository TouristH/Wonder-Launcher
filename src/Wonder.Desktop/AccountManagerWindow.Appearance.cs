// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Wonder.Core;

namespace Wonder.Desktop;

internal sealed partial class AccountManagerWindow
{
    private void BuildAppearance(StackPanel body, AccountProfile profile)
    {
        body.Children.Add(Heading("皮肤与披风"));
        var area = new Grid { ColumnDefinitions = new ColumnDefinitions("190,20,*") };
        var previewColumn = BuildCharacterPreview(profile);
        var preview = _preview!;
        var caption = previewColumn.Children.OfType<TextBlock>().Single();
        area.Children.Add(previewColumn);
        var controls = new StackPanel { Spacing = 12 }; Grid.SetColumn(controls, 2); area.Children.Add(controls);
        controls.Children.Add(PageStyles.Hint("选择 PNG 皮肤后先在本地预览，点击上传才会修改 Minecraft 账户。支持 64×64 和 64×32，最大 1 MiB。"));
        var variant = new ComboBox { ItemsSource = new[] { "经典 · 4 像素手臂", "纤细 · 3 像素手臂" }, SelectedIndex = profile.SkinVariant == "slim" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(variant, "皮肤模型"); _idle.Add(variant); controls.Children.Add(variant);
        variant.SelectionChanged += (_, _) => preview.Slim = variant.SelectedIndex == 1;
        byte[]? pendingSkin = null;
        var fileLabel = PageStyles.Hint("尚未选择本地皮肤");
        var choose = IdleButton("选择皮肤文件…", "选择正版皮肤文件", async () =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title = "选择 Minecraft PNG 皮肤", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Minecraft 皮肤") { Patterns = ["*.png"], MimeTypes = ["image/png"] }] });
                if (_closed || files.FirstOrDefault() is not { } file) return;
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream(); var buffer = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(buffer)) != 0)
                {
                    if (memory.Length + read > 1024 * 1024) throw new InvalidDataException();
                    memory.Write(buffer, 0, read);
                }
                if (_closed || !ReferenceEquals(_preview, preview)) return;
                var bytes = memory.ToArray(); preview.SetLocal(bytes); pendingSkin = bytes;
                caption.Text = preview.Description; fileLabel.Text = file.Name + " · 待上传";
            }
            catch { if (!_closed) _status.Text = "无法读取此皮肤，请选择有效的 64×64 或 64×32 PNG（不超过 1 MiB）。"; }
        });
        controls.Children.Add(choose); controls.Children.Add(fileLabel);
        controls.Children.Add(IdleButton("上传到 Minecraft", "上传正版皮肤", async () =>
        {
            if (pendingSkin is null) { _status.Text = "请先选择皮肤文件并查看预览。"; return; }
            await Run(() => _service.UploadSkinAsync(profile.Id, pendingSkin, variant.SelectedIndex == 1 ? "slim" : "classic"));
        }));
        var resetConfirm = new StackPanel { Spacing = 8, IsVisible = false };
        resetConfirm.Children.Add(PageStyles.Hint("恢复默认皮肤将移除当前自定义皮肤。确认后同步到 Minecraft 账户。"));
        resetConfirm.Children.Add(IdleButton("确认恢复默认", "确认恢复默认皮肤", async () => await Run(() => _service.ResetSkinAsync(profile.Id))));
        resetConfirm.Children.Add(Button("保留当前皮肤", "取消恢复默认皮肤", () => resetConfirm.IsVisible = false));
        controls.Children.Add(IdleButton("恢复默认皮肤", "恢复默认皮肤", () => resetConfirm.IsVisible = true)); controls.Children.Add(resetConfirm);
        body.Children.Add(PageStyles.Card(area, new Thickness(16)));
        var capePanel = new StackPanel { Spacing = 10 };
        capePanel.Children.Add(PageStyles.Hint("我的披风 · " + profile.Capes.Count + " 件。只能使用此 Minecraft 账户已经拥有的披风。"));
        var capes = profile.Capes.ToArray();
        var capeChoice = new ComboBox { ItemsSource = new[] { "不显示披风" }.Concat(capes.Select(x => x.Name)).ToArray(), SelectedIndex = Array.FindIndex(capes, x => x.Id == profile.ActiveCapeId) + 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(capeChoice, "正版披风"); _idle.Add(capeChoice); capePanel.Children.Add(capeChoice);
        capePanel.Children.Add(IdleButton("应用披风选择", "应用正版披风", async () =>
        { await Run(() => _service.SetCapeAsync(profile.Id, capeChoice.SelectedIndex <= 0 ? null : capes[capeChoice.SelectedIndex - 1].Id)); }));
        body.Children.Add(PageStyles.Card(capePanel));
        BuildActiveCape(body, profile);
    }

    private StackPanel BuildCharacterPreview(AccountProfile profile)
    {
        var column = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        var preview = _preview = new AccountSkinPreview(_textureDownload) { Width = 180, Height = 240 };
        AutomationProperties.SetName(preview, "账户全身皮肤预览");
        column.Children.Add(preview);
        var caption = PageStyles.Hint(preview.Description); caption.MaxWidth = 220;
        caption.HorizontalAlignment = HorizontalAlignment.Center; column.Children.Add(caption);
        preview.LoadedTexture += () => caption.Text = preview.Description;
        var turn = Button("查看背面", "切换角色预览方向", () => preview.Back = !preview.Back);
        turn.Click += (_, _) => turn.Content = preview.Back ? "查看正面" : "查看背面";
        turn.HorizontalAlignment = HorizontalAlignment.Center; column.Children.Add(turn);
        _ = preview.LoadAsync(profile);
        return column;
    }

    private void BuildActiveCape(StackPanel body, AccountProfile profile)
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("90,16,*") };
        var preview = _capePreview = new AccountSkinPreview(_textureDownload) { CapeOnly = true, Width = 90, Height = 144 };
        AutomationProperties.SetName(preview, "已选披风预览"); layout.Children.Add(preview);
        var info = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(info, 2); layout.Children.Add(info);
        info.Children.Add(new TextBlock { Text = "已选披风", FontSize = 16 });
        var status = PageStyles.Hint(""); info.Children.Add(status);
        preview.LoadedTexture += () => status.Text = preview.Description;
        info.Children.Add(PageStyles.Hint("角色背面同步显示当前佩戴的披风。"));
        body.Children.Add(PageStyles.Card(layout));
        _ = preview.LoadAsync(profile);
    }
}
