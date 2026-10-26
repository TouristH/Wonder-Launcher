// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Application;
using Wonder.Core;
using Wonder.Desktop.Components;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class AccountComponentsTests
{
    [AvaloniaFact]
    public void Existing_account_component_loads_selected_avatar_refreshes_same_url_and_keeps_layout_empty()
    {
        var first = new AccountProfile("first", AccountKind.Yggdrasil, "FirstSkin", "0123456789abcdef0123456789abcdef", "https://auth.example.test/")
        { SkinUrl = "https://cdn.example.test/skin.png" };
        var second = new AccountProfile("second", AccountKind.Microsoft, "SecondSkin", "abcdef0123456789abcdef0123456789", ClientId: WonderMicrosoftApplication.ClientId)
        { SkinUrl = "https://textures.minecraft.net/texture/second" };
        var vault = new AccountVault { Accounts = [new(first, new() { AccessToken = "synthetic-secret" }), new(second, new() { AccessToken = "synthetic-secret-2" })], SelectedAccountId = first.Id };
        var store = new Store(vault); var service = new AccountService(store, new Microsoft(), new Yggdrasil());
        var requests = new List<string>(); var registry = new ComponentRegistry();
        AccountComponents.Register(registry, service, () => { }, textureDownload: (url, _) => { requests.Add(url); return Task.FromResult(LayeredAvatarPng()); });
        var definition = registry.Find(ComponentIds.Accounts)!;
        using (var library = definition.CreatePresentation(new(definition.Definition, true, () => new Dictionary<string, string>(), (_, _) => { })))
            Assert.Empty(requests);
        var document = WorkspaceDocument.CreateEmpty(); document.EnsureLibrary([ComponentIds.Accounts]);
        document.AddArea().Components.Add(document.CreateShortcut(document.Library.Single(x => x.DefinitionId == ComponentIds.Accounts)));
        var workspace = new WorkspaceView(document, null, registry);
        var window = new Window { Width = 800, Height = 520, Content = workspace }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); Assert.Equal(first.SkinUrl, Assert.Single(requests));
            var avatar = window.GetVisualDescendants().OfType<AccountSkinPreview>().Single();
            Assert.Contains("已保存", avatar.Description);
            service.Select(second.Id); Dispatcher.UIThread.RunJobs(); Assert.Equal(second.SkinUrl, requests[^1]);
            Assert.NotSame(avatar, window.GetVisualDescendants().OfType<AccountSkinPreview>().Single());
            var refreshed = second with { LastValidatedAt = DateTimeOffset.UtcNow };
            var currentVault = store.Load(); currentVault.Accounts[1] = new(refreshed, new() { AccessToken = "synthetic-secret-2" }); store.Save(currentVault); service.Reload(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, requests.Count(x => x == second.SkinUrl));
            Assert.All(document.Library, x => Assert.Empty(x.State));
            SaveFrame(window, "account-component-real-avatar");
            workspace.DisposeComponents(); var count = requests.Count;
            service.Select(first.Id); Dispatcher.UIThread.RunJobs(); Assert.Equal(count, requests.Count);
        }
        finally { window.Close(); workspace.DisposeComponents(); }
    }

    [AvaloniaFact]
    public void Removing_account_component_cancels_pending_avatar_download()
    {
        var profile = new AccountProfile("pending", AccountKind.Microsoft, "PendingSkin", "0123456789abcdef0123456789abcdef", ClientId: WonderMicrosoftApplication.ClientId)
        { SkinUrl = "https://textures.minecraft.net/texture/pending" };
        var service = new AccountService(new Store(new() { Accounts = [new(profile, new() { AccessToken = "synthetic" })], SelectedAccountId = profile.Id }), new Microsoft(), new Yggdrasil());
        var tokens = new List<CancellationToken>(); var registry = new ComponentRegistry();
        AccountComponents.Register(registry, service, () => { }, textureDownload: async (_, ct) => { tokens.Add(ct); await Task.Delay(Timeout.Infinite, ct); return []; });
        var document = WorkspaceDocument.CreateEmpty(); document.EnsureLibrary([ComponentIds.Accounts]);
        document.AddArea().Components.Add(document.CreateShortcut(document.Library.Single(x => x.DefinitionId == ComponentIds.Accounts)));
        var workspace = new WorkspaceView(document, null, registry);
        var window = new Window { Width = 800, Height = 520, Content = workspace }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); Assert.Single(tokens); Assert.False(tokens[0].IsCancellationRequested);
            workspace.DisposeComponents(); Assert.True(tokens[0].IsCancellationRequested);
        }
        finally { window.Close(); workspace.DisposeComponents(); }
    }

    [AvaloniaTheory]
    [InlineData(AccountKind.Yggdrasil)]
    [InlineData(AccountKind.Microsoft)]
    public void Online_details_load_skin_and_show_active_cape_without_opening_browser(AccountKind kind)
    {
        var profile = new AccountProfile("online-preview", kind, "PreviewPlayer", "0123456789abcdef0123456789abcdef",
            kind == AccountKind.Yggdrasil ? "https://auth.example.test/api/" : null, kind == AccountKind.Microsoft ? WonderMicrosoftApplication.ClientId : null)
        {
            SkinUrl = "https://textures.minecraft.net/texture/skin", SkinVariant = "slim",
            Capes = [new(AppearanceMicrosoft.CapeId, "测试已选披风", "https://textures.minecraft.net/texture/cape")], ActiveCapeId = AppearanceMicrosoft.CapeId
        };
        var service = new AccountService(new Store(new() { Accounts = [new(profile, new() { AccessToken = "synthetic" })], SelectedAccountId = profile.Id }), new Microsoft(), new Yggdrasil());
        var requests = new List<string>(); var opened = new List<Uri>();
        var window = new AccountManagerWindow(service, openBrowser: opened.Add, textureDownload: (url, _) =>
        { requests.Add(url); return Task.FromResult(SyntheticPng(url.EndsWith("cape", StringComparison.Ordinal) ? 32 : 64)); });
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(requests.Count(x => x == profile.SkinUrl) >= 2); // Both account card and full character request the skin.
            Assert.Contains(profile.Capes[0].Url, requests);
            Assert.Contains(window.GetVisualDescendants().OfType<Control>(), x => AutomationProperties.GetName(x) == "已选披风预览");
            Click(window, "切换角色预览方向");
            Assert.Empty(opened);
            foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.Extent.Height > x.Viewport.Height)) scroll.Offset = new Vector(0, 420);
            SaveFrame(window, "account-" + kind.ToString().ToLowerInvariant() + "-skin-and-cape");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_idle_account_window_does_not_cancel_launch_account_refresh()
    {
        var provider = new WaitingMicrosoft();
        var service = new AccountService(new Store(), provider, new Yggdrasil());
        await service.SignInMicrosoftAsync();
        var window = new AccountManagerWindow(service); window.Show();
        var session = service.GetSelectedSessionAsync();
        window.Close(); provider.Release.SetResult();
        Assert.Equal(AccountKind.Microsoft, (await session).Kind);
    }

    [AvaloniaFact]
    public void Offline_account_can_be_added_selected_and_deleted_in_shared_window()
    {
        var service = new AccountService(new Store(), new Microsoft(), new Yggdrasil());
        var window = new AccountManagerWindow(service); window.Show();
        try
        {
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            Box(window, "离线玩家名").Text = "Wonder_Player"; Click(window, "添加离线账户");
            Assert.Equal("Wonder_Player", service.SelectedAccount!.Username);
            Click(window, "添加账户");
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            Box(window, "离线玩家名").Text = "PlayerTwo"; Click(window, "添加离线账户");
            Click(window, "查看账户 Wonder_Player");
            Assert.Equal("PlayerTwo", service.SelectedAccount!.Username); // Browsing does not change launch identity.
            Click(window, "选择账户 Wonder_Player"); Assert.Equal("Wonder_Player", service.SelectedAccount!.Username);
            Click(window, "删除账户 Wonder_Player"); Click(window, "确认删除账户");
            Assert.Equal("PlayerTwo", Assert.Single(service.Accounts).Username);
            SaveFrame(window, "account-manager-offline");
            Click(window, "添加账户");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), x => (AutomationProperties.GetName(x) ?? "").Contains("Client ID"));
            SaveFrame(window, "account-manager-microsoft");
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "account-manager-third-party");
            window.Width = 880; window.Height = 660; Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "account-manager-compact");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Account_details_support_cape_apply_reset_refresh_and_bound_reauthentication()
    {
        var provider = new AppearanceMicrosoft(); var service = new AccountService(new Store(), provider, new Yggdrasil());
        await service.SignInMicrosoftAsync(); var online = service.SelectedAccount!;
        service.AddOffline("LocalPlayer");
        var window = new AccountManagerWindow(service, openBrowser: _ => { }, textureDownload: (_, _) => Task.FromException<byte[]>(new IOException("Synthetic unavailable texture"))); window.Show();
        try
        {
            Click(window, "查看账户 OnlinePlayer");
            var cape = window.GetVisualDescendants().OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "正版披风");
            cape.SelectedIndex = 1; Click(window, "应用正版披风");
            Assert.Equal(AppearanceMicrosoft.CapeId, service.Accounts.Single(x => x.Id == online.Id).ActiveCapeId);
            Assert.Equal("LocalPlayer", service.SelectedAccount!.Username);
            Click(window, "恢复默认皮肤"); Assert.Equal(0, provider.Resets);
            Click(window, "确认恢复默认皮肤"); Assert.Equal(1, provider.Resets);
            Click(window, "刷新账户"); Assert.True(provider.Refreshes >= 3);
            SaveFrame(window, "account-center-online-details");
            foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.Extent.Height > x.Viewport.Height)) scroll.Offset = new Vector(0, 400);
            Dispatcher.UIThread.RunJobs(); SaveFrame(window, "account-center-appearance");
            Click(window, "重新登录账户"); Click(window, "开始微软登录");
            Assert.Equal("LocalPlayer", service.SelectedAccount!.Username);
            Assert.Equal(2, provider.SignIns); Assert.Equal(2, service.Accounts.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Device_code_opens_only_official_page_once_and_closing_cancels_without_saving()
    {
        var provider = new DeviceMicrosoft(); var service = new AccountService(new Store(), provider, new Yggdrasil());
        var opened = new List<Uri>(); var window = new AccountManagerWindow(service, openBrowser: opened.Add); window.Show();
        Click(window, "开始微软登录"); Dispatcher.UIThread.RunJobs();
        Assert.Single(opened); Assert.Equal("microsoft.com", opened[0].Host);
        SaveFrame(window, "account-center-device-login");
        window.Close(); await provider.Cancelled.Task;
        Assert.Empty(service.Accounts);
    }

    [AvaloniaFact]
    public void Server_discovery_requires_confirmation_and_preserves_saved_address()
    {
        var service = new AccountService(new Store(), new Microsoft(), new Yggdrasil(), new ServerDirectory());
        var window = new AccountManagerWindow(service); window.Show();
        try
        {
            Click(window, "管理认证服务器"); Box(window, "发现认证服务器地址").Text = "https://skin.example.test";
            Click(window, "发现认证服务器"); Assert.Empty(service.Servers); Assert.NotNull(service.PendingServer);
            Box(window, "待保存服务器名称").Text = "Wonder 测试皮肤站"; Click(window, "确认添加认证服务器");
            Assert.Equal("https://skin.example.test/api/yggdrasil/", Assert.Single(service.Servers).ApiUrl);
            SaveFrame(window, "account-center-server-directory");
            Click(window, "返回添加账户"); window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text?.Contains("https://skin.example.test/api/yggdrasil/") == true);
            SaveFrame(window, "account-center-third-party-ready");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Skin_preview_decodes_local_png_and_renders_front_and_back()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using (var pixels = bitmap.Lock())
        {
            var bytes = new byte[pixels.RowBytes * 64];
            for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            { var offset = y * pixels.RowBytes + x * 4; bytes[offset] = (byte)(x * 4); bytes[offset + 1] = (byte)(y * 4); bytes[offset + 2] = 180; bytes[offset + 3] = 255; }
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, pixels.Address, bytes.Length);
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        using var preview = new AccountSkinPreview(); preview.SetLocal(stream.ToArray());
        var window = new Window { Width = 320, Height = 340, Content = preview }; window.Show();
        try
        {
            SaveFrame(window, "account-skin-front"); preview.Back = true; preview.Slim = true;
            SaveFrame(window, "account-skin-back-slim");
            Assert.Contains("仅本地", preview.Description);
            Assert.Throws<Wonder.Application.AccountAuthenticationException>(() => preview.SetLocal(new byte[64]));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Login_completion_does_not_replace_a_newly_opened_server_page()
    {
        var provider = new DeferredMicrosoft(); var service = new AccountService(new Store(), provider, new Yggdrasil());
        var window = new AccountManagerWindow(service, openBrowser: _ => { }); window.Show();
        try
        {
            Click(window, "开始微软登录"); Click(window, "管理认证服务器"); provider.Release.TrySetResult();
            for (var i = 0; i < 100 && service.IsBusy; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Dispatcher.UIThread.RunJobs(); Assert.Single(service.Accounts);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), x => AutomationProperties.GetName(x) == "发现认证服务器");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void External_account_selection_updates_details_without_changing_viewed_account()
    {
        var service = new AccountService(new Store(), new Microsoft(), new Yggdrasil());
        service.AddOffline("FirstPlayer"); var first = service.SelectedAccount!.Id; service.AddOffline("SecondPlayer");
        var window = new AccountManagerWindow(service); window.Show();
        try
        {
            Click(window, "查看账户 FirstPlayer");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "离线  ·  可切换使用");
            service.Select(first); Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "离线  ·  当前启动账户");
            Assert.Equal("FirstPlayer", Box(window, "修改离线玩家名").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Local_preview_wins_over_late_download_and_default_skin_still_renders_owned_cape()
    {
        var release = new TaskCompletionSource<byte[]>();
        using var preview = new AccountSkinPreview((_, _) => release.Task);
        var loading = preview.LoadAsync(new("online", AccountKind.Microsoft, "Test", "") { SkinUrl = "https://textures.minecraft.net/texture/synthetic" });
        preview.SetLocal(SyntheticPng(64)); release.SetResult(SyntheticPng(64)); await loading;
        Assert.Contains("仅本地预览", preview.Description);
        var requests = new List<string>();
        using var capePreview = new AccountSkinPreview((url, _) => { requests.Add(url); return Task.FromResult(SyntheticPng(32)); });
        await capePreview.LoadAsync(new("online", AccountKind.Microsoft, "Test", "")
        { Capes = [new(AppearanceMicrosoft.CapeId, "Synthetic cape", "https://textures.minecraft.net/texture/cape")], ActiveCapeId = AppearanceMicrosoft.CapeId });
        Assert.Single(requests);
        var window = new Window { Width = 320, Height = 340, Content = capePreview }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var front = window.CaptureRenderedFrame(); using var frontBytes = new MemoryStream(); front!.Save(frontBytes, PngBitmapEncoderOptions.Default);
            capePreview.Back = true; Dispatcher.UIThread.RunJobs(); using var back = window.CaptureRenderedFrame(); using var backBytes = new MemoryStream(); back!.Save(backBytes, PngBitmapEncoderOptions.Default);
            Assert.False(frontBytes.ToArray().SequenceEqual(backBytes.ToArray())); SaveFrame(window, "account-default-skin-cape");
        }
        finally { window.Close(); }
    }

    private static byte[] SyntheticPng(int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(64, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using (var pixels = bitmap.Lock())
        {
            var bytes = new byte[pixels.RowBytes * height];
            for (var i = 0; i < bytes.Length; i += 4) { bytes[i] = 210; bytes[i + 1] = 95; bytes[i + 2] = 120; bytes[i + 3] = 255; }
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, pixels.Address, bytes.Length);
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); return stream.ToArray();
    }

    private static byte[] LayeredAvatarPng()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using (var pixels = bitmap.Lock())
        {
            var bytes = new byte[pixels.RowBytes * 64];
            void Pixel(int x, int y, byte r, byte g, byte b)
            { var i = y * pixels.RowBytes + x * 4; bytes[i] = r; bytes[i + 1] = g; bytes[i + 2] = b; bytes[i + 3] = 255; }
            for (var y = 8; y < 16; y++) for (var x = 8; x < 16; x++) Pixel(x, y, 219, 173, 132);
            for (var x = 0; x < 8; x++) { Pixel(40 + x, 8, 55, 75, 96); Pixel(40 + x, 9, 55, 75, 96); }
            for (var y = 10; y < 13; y++) { Pixel(40, y, 55, 75, 96); Pixel(47, y, 55, 75, 96); }
            Pixel(10, 11, 52, 107, 167); Pixel(13, 11, 52, 107, 167);
            Pixel(11, 14, 128, 75, 53); Pixel(12, 14, 128, 75, 53);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, pixels.Address, bytes.Length);
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); return stream.ToArray();
    }

    [AvaloniaFact]
    public void Offline_default_skin_requires_uuid_confirmation_and_can_be_restored()
    {
        var service = new AccountService(new Store(), new Microsoft(), new Yggdrasil()); service.AddOffline("SkinPlayer");
        var original = service.SelectedAccount!; var window = new AccountManagerWindow(service); window.Show();
        try
        {
            var choices = window.GetVisualDescendants().OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "离线默认皮肤");
            choices.SelectedIndex = Array.FindIndex(OfflineDefaultSkins.Choices.ToArray(), x => x.Id == "steve-classic") + 1;
            Dispatcher.UIThread.RunJobs(); Assert.Equal(original.Uuid, service.SelectedAccount!.Uuid);
            Click(window, "应用离线默认皮肤"); Assert.Equal(original.Uuid, service.SelectedAccount.Uuid);
            Click(window, "取消离线皮肤变化"); Assert.Equal(original.Uuid, service.SelectedAccount.Uuid);
            Click(window, "应用离线默认皮肤"); Click(window, "确认离线皮肤 UUID 变化");
            Assert.Equal("steve-classic", service.SelectedAccount.OfflineSkinId); Assert.NotEqual(original.Uuid, service.SelectedAccount.Uuid);
            Assert.Equal(original.Id, service.SelectedAccount.Id);
            foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.Extent.Height > x.Viewport.Height)) scroll.Offset = new Vector(0, 430);
            SaveFrame(window, "account-offline-default-skins");
            choices = window.GetVisualDescendants().OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "离线默认皮肤");
            choices.SelectedIndex = 0; Click(window, "应用离线默认皮肤"); Click(window, "确认离线皮肤 UUID 变化");
            Assert.Equal(original.Uuid, service.SelectedAccount.Uuid); Assert.Null(service.SelectedAccount.OfflineSkinId);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Third_party_skin_button_opens_the_saved_website_only_on_click()
    {
        var vault = new AccountVault
        {
            Accounts = [new(new("third", AccountKind.Yggdrasil, "SkinPlayer", "0123456789abcdef0123456789abcdef", "https://auth.example.test/api/"), new() { AccessToken = "synthetic-secret" })],
            SelectedAccountId = "third", Servers = [new("site", "测试皮肤站", "https://auth.example.test/api/", "https://skin.example.test/user/skin")]
        };
        var service = new AccountService(new Store(vault), new Microsoft(), new Yggdrasil());
        var opened = new List<Uri>(); var window = new AccountManagerWindow(service, openBrowser: opened.Add); window.Show();
        try
        {
            Assert.Empty(opened); Click(window, "选择第三方皮肤");
            Assert.Equal("https://skin.example.test/user/skin", Assert.Single(opened).AbsoluteUri);
            Click(window, "设置第三方皮肤网站"); Box(window, "服务器皮肤网站 site").Text = "https://skin.example.test/profile";
            Click(window, "保存服务器皮肤网站 site"); Click(window, "查看账户 SkinPlayer"); Click(window, "选择第三方皮肤");
            Assert.Equal("https://skin.example.test/profile", opened[1].AbsoluteUri);
            Assert.All(opened, x => Assert.DoesNotContain("synthetic", x.AbsoluteUri));
            foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.Extent.Height > x.Viewport.Height)) scroll.Offset = new Vector(0, 350);
            SaveFrame(window, "account-third-party-skin-link");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Default_skin_preview_reads_only_the_named_installed_texture()
    {
        var root = Path.Combine(Path.GetTempPath(), "Wonder.SkinPreview.Tests", Guid.NewGuid().ToString("N"));
        var version = Path.Combine(root, "versions", "1.20.1"); Directory.CreateDirectory(version);
        try
        {
            var bytes = SyntheticPng(64);
            using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(version, "1.20.1.jar"), System.IO.Compression.ZipArchiveMode.Create))
            {
                using var stream = zip.CreateEntry("assets/minecraft/textures/entity/player/wide/steve.png").Open(); stream.Write(bytes);
            }
            var instance = new GameInstance { Name = "Synthetic", VersionId = "1.20.1", GameVersion = "1.20.1", SourcePath = root,
                RootDirectory = root, DefaultGameDirectory = root, VersionsDirectory = Path.Combine(root, "versions"), LibraryDirectory = Path.Combine(root, "libraries"), AssetsDirectory = Path.Combine(root, "assets") };
            Assert.Equal(bytes, Wonder.Infrastructure.DefaultSkinTextureReader.TryRead(instance, "steve", "classic", false));
            Assert.Null(Wonder.Infrastructure.DefaultSkinTextureReader.TryRead(instance, "alex", "slim", false));
            Assert.Throws<ArgumentException>(() => Wonder.Infrastructure.DefaultSkinTextureReader.TryRead(instance, "../steve", "classic", false));
            using var preview = new AccountSkinPreview(); await preview.LoadDefaultAsync(instance, "steve", "classic", false);
            Assert.Contains("来自所选实例", preview.Description);
            var service = new AccountService(new Store(), new Microsoft(), new Yggdrasil()); service.AddOffline("OfflineSkin");
            service.SetOfflineSkin(service.SelectedAccount!.Id, "steve-classic");
            var second = instance with { Id = "missing-texture", GameVersion = "1.20.2", VersionId = "1.20.2" };
            var instanceStore = new Wonder.Infrastructure.InstanceStore(Path.Combine(root, "synthetic-instances.json"));
            instanceStore.Save(new() { Instances = [instance, second], SelectedInstanceId = instance.Id });
            var instances = new InstanceService(instanceStore, new Wonder.Infrastructure.InstanceDiscovery(), new Wonder.Infrastructure.LocalGameLauncher(), Path.Combine(root, "cache"));
            var registry = new ComponentRegistry(); AccountComponents.Register(registry, service, () => { }, instances);
            var document = WorkspaceDocument.CreateEmpty(); document.EnsureLibrary([ComponentIds.Accounts]);
            document.AddArea().Components.Add(document.CreateShortcut(document.Library.Single(x => x.DefinitionId == ComponentIds.Accounts)));
            var workspace = new WorkspaceView(document, null, registry); var window = new Window { Width = 800, Height = 520, Content = workspace }; window.Show();
            try
            {
                var manager = new AccountManagerWindow(service, instances: instances); manager.Show(window);
                async Task<AccountSkinPreview> LoadedAvatar()
                {
                    var avatar = window.GetVisualDescendants().OfType<AccountSkinPreview>().Single();
                    for (var i = 0; i < 100 && avatar.Description.Contains("正在读取"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                    return avatar;
                }
                Assert.Contains("来自所选实例", (await LoadedAvatar()).Description);
                instances.Select(second.Id); Dispatcher.UIThread.RunJobs();
                Assert.Contains("纹理未安装", (await LoadedAvatar()).Description);
                var managerAvatar = manager.GetVisualDescendants().OfType<AccountSkinPreview>().Single(x => x.HeadOnly);
                for (var i = 0; i < 100 && managerAvatar.Description.Contains("正在读取"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                Assert.Contains("纹理未安装", managerAvatar.Description);
                instances.Select(instance.Id); service.SetOfflineSkin(service.SelectedAccount!.Id, "alex-slim"); Dispatcher.UIThread.RunJobs();
                Assert.StartsWith("alex", (await LoadedAvatar()).Description);
                Assert.All(document.Library, x => Assert.Empty(x.State));
                manager.Close();
            }
            finally { window.Close(); workspace.DisposeComponents(); }
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void Component_uses_shared_selection_and_never_persists_credentials_in_layout()
    {
        var service = new AccountService(new Store(), new Microsoft(), new Yggdrasil());
        service.AddOffline("Wonder_Player");
        var registry = new ComponentRegistry(); AccountComponents.Register(registry, service, () => { });
        var document = WorkspaceDocument.CreateEmpty(); document.EnsureLibrary([ComponentIds.Accounts]);
        document.AddArea().Components.Add(document.CreateShortcut(document.Library.Single(x => x.DefinitionId == ComponentIds.Accounts)));
        var workspace = new WorkspaceView(document, null, registry);
        var window = new Window { Width = 800, Height = 520, Content = workspace }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Wonder_Player");
            service.AddOffline("SecondPlayer"); Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "SecondPlayer");
            Assert.All(document.Library, x => Assert.Empty(x.State)); SaveFrame(window, "account-component");
        }
        finally { window.Close(); workspace.DisposeComponents(); }
    }
    private static TextBox Box(Window window, string name) => window.GetVisualDescendants().OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == name);
    private static void Click(Window window, string name)
    {
        window.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    private static void SaveFrame(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../artifacts/accounts-preview/{name}.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); frame.Save(path, PngBitmapEncoderOptions.Default);
    }
    private sealed class Store(AccountVault? initial = null) : IAccountStore { private AccountVault _vault = initial ?? new(); public AccountVault Load() => _vault; public void Save(AccountVault vault) => _vault = vault; }
    private sealed class Microsoft : IMicrosoftAuthenticator
    {
        public Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Yggdrasil : IYggdrasilAuthenticator
    {
        public Task<ThirdPartyLogin> SignInAsync(string s, string u, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthenticatedAccount> SelectProfileAsync(ThirdPartyLogin l, string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task InvalidateAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class WaitingMicrosoft : IMicrosoftAuthenticator
    {
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default) =>
            Task.FromResult(new AuthenticatedAccount(new("test", AccountKind.Microsoft, "OnlinePlayer", "0123456789abcdef0123456789abcdef", ClientId: clientId),
                new() { AccessToken = "synthetic-access", RefreshToken = "synthetic-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }));
        public async Task<AuthenticatedAccount> RefreshAsync(AccountProfile profile, AccountCredentials credentials, CancellationToken ct = default)
        { await Release.Task.WaitAsync(ct); return new(profile, credentials); }
    }
    private sealed class ServerDirectory : IAuthenticationServerDirectory
    {
        public Task<AuthenticationServer> DiscoverAsync(string address, CancellationToken ct = default) => Task.FromResult(new AuthenticationServer("test-server", "Synthetic server", "https://skin.example.test/api/yggdrasil/"));
    }
    private sealed class AppearanceMicrosoft : IMicrosoftAuthenticator, IMicrosoftAccountAppearance
    {
        public const string CapeId = "ba3a48ec-2681-441d-a11c-e402b85d3944";
        public int SignIns, Refreshes, Resets;
        public Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default)
        {
            SignIns++;
            return Task.FromResult(new AuthenticatedAccount(new AccountProfile("online", AccountKind.Microsoft, "OnlinePlayer", "0123456789abcdef0123456789abcdef", ClientId: clientId)
            { Capes = [new(CapeId, "测试披风", "https://textures.minecraft.net/texture/synthetic")] }, new() { AccessToken = "synthetic", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }));
        }
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default)
        { Refreshes++; return Task.FromResult(new AuthenticatedAccount(p with { LastValidatedAt = DateTimeOffset.UtcNow }, c)); }
        public Task<AccountProfile> ResetSkinAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) { Resets++; return Task.FromResult(p with { SkinUrl = null }); }
        public Task<AccountProfile> UploadSkinAsync(AccountProfile p, AccountCredentials c, byte[] png, string variant, CancellationToken ct = default) => Task.FromResult(p with { SkinVariant = variant });
        public Task<AccountProfile> SetCapeAsync(AccountProfile p, AccountCredentials c, string? capeId, CancellationToken ct = default) => Task.FromResult(p with { ActiveCapeId = capeId });
    }
    private sealed class DeviceMicrosoft : IMicrosoftAuthenticator
    {
        public TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default)
        {
            prompt(new("ABCD-EFGH", new Uri("https://microsoft.com/link"), DateTimeOffset.UtcNow.AddMinutes(10)));
            try { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); }
            finally { Cancelled.TrySetResult(); }
        }
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class DeferredMicrosoft : IMicrosoftAuthenticator
    {
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AuthenticatedAccount> SignInAsync(string clientId, Action<DeviceCodePrompt> prompt, CancellationToken ct = default)
        {
            await Release.Task.WaitAsync(ct);
            return new(new("deferred", AccountKind.Microsoft, "OnlinePlayer", "0123456789abcdef0123456789abcdef", ClientId: clientId),
                new() { AccessToken = "synthetic", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
        }
        public Task<AuthenticatedAccount> RefreshAsync(AccountProfile p, AccountCredentials c, CancellationToken ct = default) => Task.FromResult(new AuthenticatedAccount(p, c));
    }
}
