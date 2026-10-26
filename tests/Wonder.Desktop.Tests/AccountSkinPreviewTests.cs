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

    [AvaloniaFact]
    public async Task Cape_card_fetches_only_active_cape_and_clears_when_not_selected()
    {
        var urls = new List<string>();
        using var preview = new AccountSkinPreview((url, _) => { urls.Add(url); return Task.FromResult(Png(32, pixels => Fill(pixels, 1, 1, 10, 16, Outer))); })
            { CapeOnly = true, Width = 120, Height = 180 };
        await preview.LoadAsync(Profile(AccountKind.Yggdrasil));
        Assert.Equal("cape", Assert.Single(urls));
        using var host = new PreviewHost(preview); using var withCape = host.Frame(); AssertColor(withCape, 60, 90, Outer);
        await preview.LoadAsync(Profile(AccountKind.Yggdrasil) with { ActiveCapeId = null });
        using var empty = host.Frame(); Assert.NotEqual(Outer, Pixel(empty, 60, 90));
        Assert.Contains("未选择披风", preview.Description); Assert.Single(urls);
    }

    private static readonly Color Base = Color.FromRgb(20, 60, 180);
    private static readonly Color Outer = Color.FromRgb(230, 70, 30);
    private static readonly Color Left = Color.FromRgb(20, 200, 90);

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Every_body_part_composites_transparent_outer_layer_with_visible_expansion(bool back, bool slim)
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
        });
        using var preview = new AccountSkinPreview { Width = 240, Height = 360, Slim = slim, Back = back };
        preview.SetLocal(png);
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
        Save(frame, $"skin-layers-{(back ? "back" : "front")}-{(slim ? "slim" : "classic")}");
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
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_skin_mirrors_right_arm_and_leg_for_the_left_limbs(bool back)
    {
        var png = Png(32, pixels =>
        {
            Fill(pixels, back ? 52 : 44, 20, 2, 12, Outer);
            Fill(pixels, back ? 54 : 46, 20, 2, 12, Left);
            Fill(pixels, back ? 12 : 4, 20, 2, 12, Outer);
            Fill(pixels, back ? 14 : 6, 20, 2, 12, Left);
        });
        using var preview = new AccountSkinPreview { Width = 240, Height = 360, Back = back };
        preview.SetLocal(png); using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, back ? 50 : 170, 150, Left);
        AssertColor(frame, back ? 70 : 190, 150, Outer);
        AssertColor(frame, back ? 90 : 130, 270, Left);
        AssertColor(frame, back ? 110 : 150, 270, Outer);
        Save(frame, $"skin-legacy-{(back ? "back" : "front")}");
    }

    [AvaloniaFact]
    public void Avatar_preserves_transparent_hat_pixels_and_expands_hat_outside_face()
    {
        var png = Png(64, pixels => { Fill(pixels, 8, 8, 8, 8, Base); Fill(pixels, 40, 8, 4, 8, Outer); });
        using var preview = new AccountSkinPreview { Width = 90, Height = 90, HeadOnly = true };
        preview.SetLocal(png); using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, 2, 45, Outer); AssertColor(frame, 60, 45, Base);
        AssertColor(frame, 87, 45, Colors.Black);
        Save(frame, "skin-avatar-layers");
    }

    [AvaloniaTheory]
    [InlineData(AccountKind.Microsoft)]
    [InlineData(AccountKind.Yggdrasil)]
    public async Task Selected_cape_still_renders_when_skin_download_fails(AccountKind kind)
    {
        var cape = Png(32, pixels => Fill(pixels, 1, 1, 10, 16, Outer));
        using var preview = new AccountSkinPreview((url, _) => url == "skin" ? Task.FromException<byte[]>(new IOException()) : Task.FromResult(cape))
            { Width = 240, Height = 360, Back = true };
        await preview.LoadAsync(Profile(kind));
        using var host = new PreviewHost(preview); using var frame = host.Frame();
        AssertColor(frame, 120, 170, Outer); Save(frame, "cape-with-unavailable-skin");
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

    private static byte[] Png(int height, Action<byte[]> paint)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(64, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        var bytes = new byte[64 * height * 4]; paint(bytes);
        using (var pixels = bitmap.Lock()) Marshal.Copy(bytes, 0, pixels.Address, bytes.Length);
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); return stream.ToArray();
    }
    private static void Fill(byte[] pixels, int x, int y, int width, int height, Color color)
    {
        for (var py = y; py < y + height; py++) for (var px = x; px < x + width; px++)
        { var offset = (py * 64 + px) * 4; pixels[offset] = color.R; pixels[offset + 1] = color.G; pixels[offset + 2] = color.B; pixels[offset + 3] = color.A; }
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
