// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Desktop;

/// <summary>A local, pixel-aligned front/back view. Texture downloads never carry credentials.</summary>
internal sealed class AccountSkinPreview : Control, IDisposable
{
    private Bitmap? _skin;
    private Bitmap? _cape;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<string, CancellationToken, Task<byte[]>>? _download;
    private readonly AccountTextureClient _textures = new();
    private bool _back, _slim, _disposed;
    private int _revision;
    private int _capeRevision;
    public string Description { get; private set; } = "默认角色示意";
    public event Action? LoadedTexture;
    public AccountSkinPreview(Func<string, CancellationToken, Task<byte[]>>? download = null)
    {
        _download = download;
        Width = 192; Height = 256;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }
    public bool Back { get => _back; set { _back = value; InvalidateVisual(); } }
    public bool HeadOnly { get; init; }
    public bool CapeOnly { get; init; }
    public bool Slim { get => _slim; set { _slim = value; InvalidateVisual(); } }
    public void SetLocal(byte[] png)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bitmap = Decode(png, cape: false);
        _revision++; _skin?.Dispose(); _skin = bitmap; Description = "待上传皮肤 · 仅本地预览"; InvalidateVisual();
    }
    public async Task LoadAsync(AccountProfile profile)
    {
        if (_disposed) return;
        var revision = ++_revision;
        var capeRevision = ++_capeRevision;
        ClearTextures();
        Slim = profile.SkinVariant == "slim";
        var selectedCape = profile.Capes.FirstOrDefault(x => x.Id == profile.ActiveCapeId);
        Description = CapeOnly && selectedCape is null ? "未选择披风" : "正在读取角色外观…";
        InvalidateVisual(); LoadedTexture?.Invoke();
        var skinFailed = false; var capeFailed = false;
        // Independent downloads let a valid selected cape remain visible when its skin is unavailable.
        await Task.WhenAll(LoadTexture(profile.SkinUrl, cape: false), LoadTexture(selectedCape?.Url, cape: true));
        if (_disposed || revision != _revision) return;
        Description = CapeOnly
            ? selectedCape is null ? "未选择披风" : capeFailed ? "披风暂不可用 · 可刷新账户重试" : selectedCape.Name + " · 已选披风"
            : skinFailed || capeFailed ? (skinFailed ? "皮肤暂不可用" : "已保存的角色外观") + (capeFailed ? " · 披风暂不可用" : _cape is not null ? " · 已选披风" : "") + " · 可刷新账户重试"
            : _skin is null ? (profile.Kind == AccountKind.Yggdrasil ? "未取得皮肤资料 · 请刷新账户" : "默认角色示意") + (_cape is not null ? " · 已选披风" : "") : "已保存的角色外观";
        InvalidateVisual(); LoadedTexture?.Invoke();

        async Task LoadTexture(string? url, bool cape)
        {
            if (url is null || (cape ? HeadOnly : CapeOnly)) return;
            try
            {
                var bytes = await (_download is not null ? _download(url, _lifetime.Token)
                    : profile.Kind == AccountKind.Yggdrasil ? _textures.DownloadThirdPartyAsync(url, _lifetime.Token)
                    : _textures.DownloadAsync(url, _lifetime.Token));
                if (_disposed || (cape ? capeRevision != _capeRevision : revision != _revision)) return;
                var bitmap = Decode(bytes, cape);
                if (cape) { _cape?.Dispose(); _cape = bitmap; }
                else { _skin?.Dispose(); _skin = bitmap; }
                InvalidateVisual();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch
            {
                if (cape) capeFailed = true; else skinFailed = true;
            }
        }
    }
    public async Task LoadDefaultAsync(GameInstance? instance, string textureName, string variant, bool legacy)
    {
        if (_disposed) return;
        var revision = ++_revision;
        _capeRevision++;
        ClearTextures(); Slim = variant == "slim";
        Description = textureName + " · 正在读取所选实例的默认纹理…"; InvalidateVisual(); LoadedTexture?.Invoke();
        try
        {
            var png = await Task.Run(() => DefaultSkinTextureReader.TryRead(instance, textureName, variant, legacy), _lifetime.Token);
            if (_disposed || revision != _revision) return;
            if (png is not null) { _skin = Decode(png, cape: false); Description = textureName + " · 来自所选实例"; }
            else Description = textureName + " · 纹理未安装，显示模型示意";
        }
        catch { if (!_disposed && revision == _revision) Description = textureName + " · 纹理不可用，显示模型示意"; }
        if (!_disposed && revision == _revision) { InvalidateVisual(); LoadedTexture?.Invoke(); }
    }
    private static Bitmap Decode(byte[] png, bool cape)
    {
        // Validate bounded dimensions, container and CRC before the decoder sees remote pixels.
        MinecraftSkinImage.Validate(png);
        if (cape && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) != 32)
            throw new InvalidDataException("披风应为 64×32 PNG。");
        using var stream = new MemoryStream(png);
        var bitmap = new Bitmap(stream);
        if (bitmap.PixelSize.Width != 64 || bitmap.PixelSize.Height is not (32 or 64) || (cape && bitmap.PixelSize.Height != 32))
        { bitmap.Dispose(); throw new InvalidDataException(); }
        return bitmap;
    }
    private void ClearTextures()
    {
        _skin?.Dispose(); _skin = null; _cape?.Dispose(); _cape = null;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_disposed) return;
        if (CapeOnly)
        {
            var capeScale = Math.Min(Bounds.Width / 12, Bounds.Height / 18);
            var capeRect = new Rect((Bounds.Width - 10 * capeScale) / 2, (Bounds.Height - 16 * capeScale) / 2, 10 * capeScale, 16 * capeScale);
            if (_cape is not null) context.DrawImage(_cape, new Rect(1, 1, 10, 16), capeRect);
            else context.DrawRectangle(Palette.Hover, null, capeRect);
            return;
        }
        if (HeadOnly)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            var rect = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
            if (_skin is not null)
            {
                context.DrawImage(_skin, new Rect(_back ? 24 : 8, 8, 8, 8), rect.Deflate(size / 18));
                context.DrawImage(_skin, new Rect(_back ? 56 : 40, 8, 8, 8), rect);
            }
            else
            {
                context.DrawRectangle(Palette.Active, null, rect);
                var unit = Bounds.Width / 8;
                context.DrawRectangle(Palette.Accent, null, new Rect(2 * unit, 3 * unit, unit, unit));
                context.DrawRectangle(Palette.Accent, null, new Rect(5 * unit, 3 * unit, unit, unit));
                context.DrawRectangle(Palette.Muted, null, new Rect(3 * unit, 5 * unit, 2 * unit, unit));
            }
            return;
        }
        var scale = Math.Min(Bounds.Width / 24, Bounds.Height / 36);
        var x = (Bounds.Width - 16 * scale) / 2; var y = (Bounds.Height - 32 * scale) / 2;
        Rect Dest(double px, double py, double w, double h) => new(x + px * scale, y + py * scale, w * scale, h * scale);
        context.DrawEllipse(Palette.Line, null, new Point(Bounds.Width / 2, y + 33 * scale), 8 * scale, scale);
        var modern = _skin?.PixelSize.Height != 32;
        var arm = _slim && modern ? 3 : 4;
        if (_skin is null)
        {
            context.DrawRectangle(Palette.Accent, null, Dest(4, 0, 8, 8));
            context.DrawRectangle(Palette.Hover, null, Dest(4, 8, 8, 12));
            context.DrawRectangle(Palette.Active, null, Dest(4 - arm, 8, arm, 12));
            context.DrawRectangle(Palette.Active, null, Dest(12, 8, arm, 12));
            context.DrawRectangle(Palette.Line, null, Dest(4, 20, 4, 12));
            context.DrawRectangle(Palette.Line, null, Dest(8, 20, 4, 12));
            if (_back && _cape is not null) context.DrawImage(_cape, new Rect(1, 1, 10, 16), Dest(3, 8, 10, 16));
            return;
        }
        void Part(int u, int v, int w, int h, double dx, double dy, double expansion = 0, bool mirror = false)
        {
            var destination = Dest(dx - expansion, dy - expansion, w + expansion * 2, h + expansion * 2);
            if (mirror)
            {
                using var transform = context.PushTransform(new Matrix(-1, 0, 0, 1, destination.X * 2 + destination.Width, 0));
                context.DrawImage(_skin, new Rect(u, v, w, h), destination);
            }
            else context.DrawImage(_skin, new Rect(u, v, w, h), destination);
        }
        // UVs are anatomical: a back view reverses where left/right limbs appear on screen.
        var rightArmX = _back ? 12 : 4 - arm; var leftArmX = _back ? 4 - arm : 12;
        var rightLegX = _back ? 8 : 4; var leftLegX = _back ? 4 : 8;
        Part(_back ? 32 : 20, 20, 8, 12, 4, 8);
        Part(_back ? 48 + arm : 44, 20, arm, 12, rightArmX, 8);
        Part(modern ? (_back ? 40 + arm : 36) : (_back ? 48 + arm : 44), modern ? 52 : 20, arm, 12, leftArmX, 8, mirror: !modern);
        Part(_back ? 12 : 4, 20, 4, 12, rightLegX, 20);
        Part(modern ? (_back ? 28 : 20) : (_back ? 12 : 4), modern ? 52 : 20, 4, 12, leftLegX, 20, mirror: !modern);
        Part(_back ? 24 : 8, 8, 8, 8, 4, 0);
        if (modern)
        {
            Part(_back ? 12 : 4, 36, 4, 12, rightLegX, 20, .25);
            Part(_back ? 12 : 4, 52, 4, 12, leftLegX, 20, .25);
            Part(_back ? 48 + arm : 44, 36, arm, 12, rightArmX, 8, .25);
            Part(_back ? 56 + arm : 52, 52, arm, 12, leftArmX, 8, .25);
            Part(_back ? 32 : 20, 36, 8, 12, 4, 8, .25);
        }
        Part(_back ? 56 : 40, 8, 8, 8, 4, 0, .5);
        if (_back && _cape is not null) context.DrawImage(_cape, new Rect(1, 1, 10, 16), Dest(3, 8, 10, 16));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _revision++; _lifetime.Cancel(); _lifetime.Dispose(); ClearTextures(); LoadedTexture = null;
    }
}
