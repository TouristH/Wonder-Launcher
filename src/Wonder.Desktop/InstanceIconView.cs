// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Desktop;

/// <summary>Owns decoded local artwork; embedded official defaults are shared for the application lifetime.</summary>
internal sealed class InstanceIconView : Image
{
    private static readonly Dictionary<string, Bitmap> Defaults = new(StringComparer.Ordinal);
    private Bitmap? _preset;
    private string? _path, _stamp;
    private string _key = "minecraft";

    public InstanceIconView(double size = 40)
    {
        Width = Height = size; Stretch = Stretch.Uniform;
        AttachedToVisualTree += (_, _) => Update();
        DetachedFromVisualTree += (_, _) => { Source = null; _preset?.Dispose(); _preset = null; _stamp = null; };
    }

    public void SetInstance(GameInstance? instance) => SetIcon(instance is null ? null : InstanceIcons.FindPreset(instance),
        instance is null ? "minecraft" : InstanceIcon.DefaultKey(instance));

    public void SetIcon(string? path, string key)
    {
        _path = string.IsNullOrWhiteSpace(path) ? null : path;
        _key = key is "fabric" or "quilt" or "forge" or "neoforge" ? key : "minecraft";
        Update();
    }

    private void Update()
    {
        string stamp = _key;
        try
        {
            if (_path is not null)
            {
                var file = new FileInfo(_path);
                stamp += "|" + _path + "|" + (file.Exists ? file.LastWriteTimeUtc.Ticks + ":" + file.Length : "missing");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { stamp += "|unavailable"; }
        if (_stamp == stamp) return;
        _stamp = stamp;
        Source = null; _preset?.Dispose(); _preset = null;
        if (_path is not null)
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length is > 0 and <= 4 * 1024 * 1024)
                {
                    // Inspect dimensions before allocating decoded pixels, including unusually tall images.
                    using var codec = SkiaSharp.SKCodec.Create(stream);
                    if (codec is not null && codec.Info.Width is > 0 and <= 4096 && codec.Info.Height is > 0 and <= 4096)
                    {
                        stream.Position = 0;
                        _preset = Bitmap.DecodeToWidth(stream, Math.Min(128, codec.Info.Width));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException) { }
        }
        if (_preset is not null) Source = _preset;
        else
        {
            if (!Defaults.TryGetValue(_key, out var bitmap))
            {
                using var resource = AssetLoader.Open(new Uri($"avares://Wonder.Desktop/Assets/InstanceIcons/{_key}.png"));
                Defaults[_key] = bitmap = new Bitmap(resource);
            }
            Source = bitmap;
        }
        AutomationProperties.SetName(this, _preset is not null ? "实例预设图标" : _key + " 官方图标");
    }
}
