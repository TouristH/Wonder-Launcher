// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Wonder.Core;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed class AccountSkinPreviewTests
{
    [AvaloniaFact]
    public async Task Local_skin_choice_keeps_the_same_accounts_cape_loading()
    {
        var skin = new TaskCompletionSource<byte[]>(); var cape = new TaskCompletionSource<byte[]>();
        using var preview = new AccountSkinPreview((url, _) => url == "skin" ? skin.Task : cape.Task) { Width = 240, Height = 360, Back = true };
        var loading = preview.LoadAsync(Profile(AccountKind.Microsoft));
        preview.SetLocal(Png(64, pixels => Fill(pixels, 0, 0, 64, 64, Base)));
        skin.SetResult(Png(64, pixels => Fill(pixels, 0, 0, 64, 64, Left)));
        cape.SetResult(Png(32, pixels => Fill(pixels, 1, 1, 10, 16, Outer))); await loading;
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, 120, 60, Base);
        AssertColor(frame, 120, 170, Outer);
        Assert.Contains("仅本地预览", preview.Description);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task Cape_card_fetches_only_active_cape_and_clears_when_not_selected(int textureScale)
    {
        var urls = new List<string>();
        using var preview = new AccountSkinPreview((url, _) => { urls.Add(url); return Task.FromResult(Png(32, pixels => Fill(pixels, 1, 1, 10, 16, Outer), textureScale)); })
            { CapeOnly = true, Width = 120, Height = 180 };
        await preview.LoadAsync(Profile(AccountKind.Yggdrasil));
        Assert.Equal("cape", Assert.Single(urls));
        using var host = new PreviewHost(preview); using var withCape = host.Frame(); AssertColor(withCape, 60, 90, Outer);
        Save(withCape, $"cape-card-{64 * textureScale}");
        await preview.LoadAsync(Profile(AccountKind.Yggdrasil) with { ActiveCapeId = null });
        using var empty = host.Frame(); Assert.NotEqual(Outer, Pixel(empty, 60, 90));
        Assert.Contains("未选择披风", preview.Description); Assert.Single(urls);
    }

    private static readonly Color Base = Color.FromRgb(20, 60, 180);
    private static readonly Color Outer = Color.FromRgb(230, 70, 30);
    private static readonly Color Left = Color.FromRgb(20, 200, 90);

    [AvaloniaTheory]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, false, 3)]
    [InlineData(true, true, 3)]
    [InlineData(false, false, 4)]
    [InlineData(false, true, 4)]
    [InlineData(true, false, 4)]
    [InlineData(true, true, 4)]
    [InlineData(false, false, 8)]
    [InlineData(false, true, 8)]
    [InlineData(true, false, 8)]
    [InlineData(true, true, 8)]
    public async Task Every_body_part_composites_transparent_outer_layer_with_visible_expansion(bool back, bool slim, int textureScale)
    {
        var png = Png(64, pixels =>
        {
            // Both faces, each base region is blue; the left half of every outer face is red.
            foreach (var (x, y, w, h) in new[] { (8,8,8,8), (24,8,8,8), (20,20,8,12), (32,20,8,12),
                (44,20,slim ? 3 : 4,12), (slim ? 51 : 52,20,slim ? 3 : 4,12),
                (36,52,slim ? 3 : 4,12), (slim ? 43 : 44,52,slim ? 3 : 4,12),
                (4,20,4,12), (12,20,4,12), (20,52,4,12), (28,52,4,12) })
                Fill(pixels, x, y, w, h, Base);
            foreach (var (x, y, w, h) in new[] { (40,8,8,8), (56,8,8,8), (20,36,8,12), (32,36,8,12),
                (44,36,slim ? 3 : 4,12), (slim ? 51 : 52,36,slim ? 3 : 4,12),
                (52,52,slim ? 3 : 4,12), (slim ? 59 : 60,52,slim ? 3 : 4,12),
                (4,36,4,12), (12,36,4,12), (4,52,4,12), (12,52,4,12) })
                Fill(pixels, x, y, w / 2, h, Outer);
        }, textureScale);
        using var preview = new AccountSkinPreview((_, _) => Task.FromResult(png)) { Width = 240, Height = 360, Slim = slim, Back = back };
        if (textureScale == 1) preview.SetLocal(png);
        else await preview.LoadAsync(Profile(AccountKind.Yggdrasil) with { SkinVariant = slim ? "slim" : "classic", Capes = [], ActiveCapeId = null });
        using var host = new PreviewHost(preview);
        using var frame = host.Frame();
        // Head, torso, screen-left/right arms and legs, expressed independently in rendered pixels.
        foreach (var (outerX, baseX, y) in new[] { (90,140,60), (90,140,150),
            (slim ? 53 : 43,slim ? 73 : 73,150), (163,183,150), (83,113,270), (123,153,270) })
        {
            AssertColor(frame, outerX, y, Outer);
            AssertColor(frame, baseX, y, Base);
        }
        // Hat and sleeves extend beyond the base silhouette, rather than flattening onto it.
        AssertColor(frame, 77, 60, Outer);
        AssertColor(frame, slim ? 48 : 38, 150, Outer);
        Save(frame, $"skin-layers-{(back ? "back" : "front")}-{(slim ? "slim" : "classic")}-{64 * textureScale}");
    }

    [AvaloniaFact]
    public void Back_view_places_anatomical_left_arm_and_leg_on_the_left()
    {
        var png = Png(64, pixels =>
        {
            Fill(pixels, 52, 20, 4, 12, Outer); Fill(pixels, 44, 52, 4, 12, Left);
            Fill(pixels, 12, 20, 4, 12, Outer); Fill(pixels, 28, 52, 4, 12, Left);
        });
        using var preview = new AccountSkinPreview { Width = 240, Height = 360, Back = true };
        preview.SetLocal(png); using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, 60, 150, Left); AssertColor(frame, 180, 150, Outer);
        AssertColor(frame, 100, 270, Left); AssertColor(frame, 140, 270, Outer);
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public async Task Legacy_skin_mirrors_right_arm_and_leg_for_the_left_limbs(bool back, int textureScale)
    {
        var png = Png(32, pixels =>
        {
            Fill(pixels, back ? 52 : 44, 20, 2, 12, Outer);
            Fill(pixels, back ? 54 : 46, 20, 2, 12, Left);
            Fill(pixels, back ? 12 : 4, 20, 2, 12, Outer);
            Fill(pixels, back ? 14 : 6, 20, 2, 12, Left);
        }, textureScale);
        using var preview = new AccountSkinPreview((_, _) => Task.FromResult(png)) { Width = 240, Height = 360, Back = back };
        if (textureScale == 1) preview.SetLocal(png);
        else await preview.LoadAsync(Profile(AccountKind.Yggdrasil) with { SkinVariant = "slim", Capes = [], ActiveCapeId = null });
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, back ? 50 : 170, 150, Left);
        AssertColor(frame, back ? 70 : 190, 150, Outer);
        AssertColor(frame, back ? 90 : 130, 270, Left);
        AssertColor(frame, back ? 110 : 150, 270, Outer);
        Save(frame, $"skin-legacy-{(back ? "back" : "front")}-{64 * textureScale}");
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public async Task Avatar_preserves_transparent_hat_pixels_and_expands_hat_outside_face(bool back, int textureScale)
    {
        var png = Png(64, pixels => { Fill(pixels, back ? 24 : 8, 8, 8, 8, Base); Fill(pixels, back ? 56 : 40, 8, 4, 8, Outer); }, textureScale);
        using var preview = new AccountSkinPreview((_, _) => Task.FromResult(png)) { Width = 90, Height = 90, HeadOnly = true, Back = back };
        if (textureScale == 1) preview.SetLocal(png);
        else await preview.LoadAsync(Profile(AccountKind.Yggdrasil));
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, 2, 45, Outer); AssertColor(frame, 60, 45, Base);
        AssertColor(frame, 87, 45, Colors.Black);
        Save(frame, $"skin-avatar-layers-{(back ? "back" : "front")}-{64 * textureScale}");
    }

    [AvaloniaTheory]
    [InlineData(120, 80, 45, 35, 75, 35, 55, 55, 15, 35)]
    [InlineData(80, 120, 25, 55, 55, 55, 35, 75, 25, 15)]
    public void Rectangular_placeholder_centers_features_inside_the_square_face(
        int width, int height, int firstEyeX, int firstEyeY, int secondEyeX, int secondEyeY,
        int mouthX, int mouthY, int outsideX, int outsideY)
    {
        using var preview = new AccountSkinPreview { Width = width, Height = height, HeadOnly = true };
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, firstEyeX, firstEyeY, Palette.Accent.Color);
        AssertColor(frame, secondEyeX, secondEyeY, Palette.Accent.Color);
        AssertColor(frame, mouthX, mouthY, Palette.Muted.Color);
        AssertColor(frame, outsideX, outsideY, Colors.Black);
        Save(frame, $"placeholder-avatar-{width}x{height}");
    }

    [AvaloniaTheory]
    [InlineData(120, 80, 22, 40, 75, 40, 15, 40, 1)]
    [InlineData(80, 120, 2, 60, 55, 60, 40, 15, 1)]
    [InlineData(120, 80, 22, 40, 75, 40, 15, 40, 4)]
    [InlineData(80, 120, 2, 60, 55, 60, 40, 15, 4)]
    public async Task Rectangular_textured_avatar_preserves_centered_face_and_hat(
        int width, int height, int hatX, int hatY, int faceX, int faceY, int outsideX, int outsideY, int textureScale)
    {
        var png = Png(64, pixels => { Fill(pixels, 8, 8, 8, 8, Base); Fill(pixels, 40, 8, 4, 8, Outer); }, textureScale);
        using var preview = new AccountSkinPreview((_, _) => Task.FromResult(png)) { Width = width, Height = height, HeadOnly = true };
        await preview.LoadAsync(Profile(AccountKind.Yggdrasil) with { Capes = [], ActiveCapeId = null });
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, hatX, hatY, Outer);
        AssertColor(frame, faceX, faceY, Base);
        AssertColor(frame, outsideX, outsideY, Colors.Black);
        Save(frame, $"textured-avatar-{width}x{height}-{64 * textureScale}");
    }

    [AvaloniaTheory]
    [InlineData(AccountKind.Microsoft, 1)]
    [InlineData(AccountKind.Yggdrasil, 1)]
    [InlineData(AccountKind.Yggdrasil, 2)]
    [InlineData(AccountKind.Yggdrasil, 4)]
    [InlineData(AccountKind.Yggdrasil, 8)]
    public async Task Selected_cape_still_renders_when_skin_download_fails(AccountKind kind, int textureScale)
    {
        var cape = Png(32, pixels => Fill(pixels, 1, 1, 10, 16, Outer), textureScale);
        using var preview = new AccountSkinPreview((url, _) => url == "skin" ? Task.FromException<byte[]>(new IOException()) : Task.FromResult(cape))
            { Width = 240, Height = 360, Back = true };
        await preview.LoadAsync(Profile(kind));
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, 120, 170, Outer); Save(frame, $"cape-with-unavailable-skin-{64 * textureScale}");
    }

    [AvaloniaTheory]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    [InlineData(4, 1)]
    [InlineData(8, 2)]
    public async Task Skin_and_cape_use_their_own_texture_resolution(int skinScale, int capeScale)
    {
        var skin = Png(64, pixels => Fill(pixels, 24, 8, 8, 8, Base), skinScale);
        var cape = Png(32, pixels => { Fill(pixels, 1, 1, 5, 16, Outer); Fill(pixels, 6, 1, 5, 16, Left); }, capeScale);
        using var preview = new AccountSkinPreview((url, _) => Task.FromResult(url == "skin" ? skin : cape))
            { Width = 240, Height = 360, Back = true };
        await preview.LoadAsync(Profile(AccountKind.Yggdrasil));
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, 120, 60, Base);
        AssertColor(frame, 90, 170, Outer); AssertColor(frame, 150, 170, Left);
        Save(frame, $"skin-cape-mixed-{64 * skinScale}-{64 * capeScale}");
    }

    [AvaloniaFact]
    public async Task Hd_avatar_preserves_details_smaller_than_one_standard_texture_pixel()
    {
        // Four source pixels in each standard pixel carry alternating colors. Reducing to 64 first loses these details.
        var png = RawPng(256, 256, pixels =>
        {
            for (var x = 32; x < 64; x++) Fill(pixels, x, 32, 1, 32, x % 2 == 0 ? Outer : Left, 256);
        });
        using var preview = new AccountSkinPreview((_, _) => Task.FromResult(png)) { HeadOnly = true, Width = 180, Height = 180 };
        await preview.LoadAsync(Profile(AccountKind.Yggdrasil));
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        // The face spans 160 pixels after the hat inset, giving each HD texel exactly five rendered pixels.
        for (var column = 0; column < 32; column++)
            AssertColor(frame, 12 + 5 * column, 90, column % 2 == 0 ? Outer : Left);
        Save(frame, "skin-avatar-hd-subpixel-detail");
    }

    [AvaloniaFact]
    public async Task Hd_cape_preserves_details_smaller_than_one_standard_texture_pixel()
    {
        var png = RawPng(256, 128, pixels =>
        {
            for (var x = 4; x < 44; x++) Fill(pixels, x, 4, 1, 64, x % 2 == 0 ? Outer : Left, 256);
        });
        using var preview = new AccountSkinPreview((_, _) => Task.FromResult(png)) { CapeOnly = true, Width = 240, Height = 360 };
        await preview.LoadAsync(Profile(AccountKind.Yggdrasil));
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        for (var column = 0; column < 40; column++)
            AssertColor(frame, 22 + 5 * column, 180, column % 2 == 0 ? Outer : Left);
        Save(frame, "cape-hd-subpixel-detail");
    }

    [AvaloniaFact]
    public async Task Switching_to_account_without_textures_clears_previous_skin_and_cape()
    {
        var png = Png(32, pixels => Fill(pixels, 0, 0, 64, 32, Outer));
        using var preview = new AccountSkinPreview((_, _) => Task.FromResult(png)) { Width = 240, Height = 360, Back = true };
        await preview.LoadAsync(Profile(AccountKind.Microsoft)); using var host = new PreviewHost(preview);
        using var before = host.Frame(); AssertColor(before, 120, 170, Outer);
        await preview.LoadAsync(new("empty", AccountKind.Microsoft, "Empty", ""));
        using var after = host.Frame(); Assert.NotEqual(Outer, Pixel(after, 120, 170)); Assert.NotEqual(Outer, Pixel(after, 120, 60));
    }

    [AvaloniaFact]
    public async Task Late_download_cannot_restore_previous_account_after_switch_or_dispose()
    {
        var release = new TaskCompletionSource<byte[]>();
        using var preview = new AccountSkinPreview((_, _) => release.Task) { Width = 240, Height = 360, Back = true };
        var pending = preview.LoadAsync(Profile(AccountKind.Microsoft));
        await preview.LoadAsync(new("empty", AccountKind.Microsoft, "Empty", ""));
        preview.Dispose(); var description = preview.Description;
        release.SetResult(Png(32, pixels => Fill(pixels, 0, 0, 64, 32, Outer))); await pending;
        Assert.Equal(description, preview.Description);
    }

    private static AccountProfile Profile(AccountKind kind) => new("test", kind, "SyntheticPlayer", "")
    { SkinUrl = "skin", Capes = [new("cape-id", "Synthetic cape", "cape")], ActiveCapeId = "cape-id" };

    private static byte[] Png(int height, Action<byte[]> paint, int textureScale = 1)
    {
        var bytes = new byte[64 * height * 4]; paint(bytes);
        return RawPng(64 * textureScale, height * textureScale, pixels =>
        {
            for (var y = 0; y < height * textureScale; y++) for (var x = 0; x < 64 * textureScale; x++)
                Array.Copy(bytes, (y / textureScale * 64 + x / textureScale) * 4, pixels, (y * 64 * textureScale + x) * 4, 4);
        });
    }
    private static byte[] RawPng(int width, int height, Action<byte[]> paint)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        var bytes = new byte[width * height * 4]; paint(bytes);
        using (var pixels = bitmap.Lock()) Marshal.Copy(bytes, 0, pixels.Address, bytes.Length);
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); return stream.ToArray();
    }
    private static void Fill(byte[] pixels, int x, int y, int width, int height, Color color, int imageWidth = 64)
    {
        for (var py = y; py < y + height; py++) for (var px = x; px < x + width; px++)
        { var offset = (py * imageWidth + px) * 4; pixels[offset] = color.R; pixels[offset + 1] = color.G; pixels[offset + 2] = color.B; pixels[offset + 3] = color.A; }
    }
    private static Color Pixel(WriteableBitmap frame, int x, int y)
    {
        using var pixels = frame.Lock(); var value = Marshal.ReadInt32(pixels.Address, y * pixels.RowBytes + x * 4);
        return frame.Format == PixelFormat.Rgba8888
            ? Color.FromArgb((byte)(value >> 24), (byte)value, (byte)(value >> 8), (byte)(value >> 16))
            : Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
    private static void AssertColor(WriteableBitmap frame, int x, int y, Color expected) => Assert.Equal(expected, Pixel(frame, x, y));
    private static void Save(WriteableBitmap frame, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WONDER_ACCOUNT_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private sealed class PreviewHost : IDisposable
    {
        private readonly Window _window;
        public PreviewHost(AccountSkinPreview preview)
        {
            _window = new Window { Width = preview.Width, Height = preview.Height, WindowDecorations = WindowDecorations.None, Background = Brushes.Black, Content = preview };
            _window.Show(); Dispatcher.UIThread.RunJobs();
        }
        public WriteableBitmap Frame() { Dispatcher.UIThread.RunJobs(); return _window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered preview frame."); }
        public void Dispose() => _window.Close();
    }
}
