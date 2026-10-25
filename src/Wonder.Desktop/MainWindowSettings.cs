// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class MainWindow
{
    private readonly string _settingsPath;
    private readonly LauncherSettingsStore _settingsStore;
    private LauncherSettings _settings = new();
    private SettingsWindow? _settingsWindow;
    private string? _settingsError;
    private bool _settingsLoadFailed;

    private void InitializeSettings()
    {
        ReadSettings();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        ApplySettings();
    }

    private void ReadSettings()
    {
        try
        {
            _settings = _settingsStore.Load();
            _settingsLoadFailed = false;
            _settingsError = null;
        }
        catch (Exception ex) when (IsSettingsStorageError(ex))
        {
            _settingsLoadFailed = true;
            _settingsError = $"设置读取失败，原文件已保留。当前使用默认设置。\n{ex.Message}\n可修复文件后重试，或确认恢复默认设置。";
        }
    }

    private void OpenSettings()
    {
        _workspace.CancelGesture();
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _settingsPath, ChangeSettings, ResetSettings, RetrySettings, _instances) { FontFamily = FontFamily };
        _settingsWindow.UpdateStatus(_settingsError, _settingsLoadFailed);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    private void ChangeSettings(LauncherSettings settings)
    {
        if (_settingsLoadFailed) return;
        _settings = settings;
        _settingsWindow?.SetValues(_settings);
        ApplySettings();
        SaveSettings();
    }

    private void ApplySettings()
    {
        var variant = _settings.Theme switch
        {
            LauncherTheme.Light => ThemeVariant.Light,
            LauncherTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        if (Avalonia.Application.Current is { } application) application.RequestedThemeVariant = variant;
        ApplyPalette();
        _workspace.ApplySettings(_settings);
        _instanceManager?.SetLayout(_settings.InstanceLayout);
        _workspace.Margin = new Thickness(_settings.WorkspacePadding);
    }

    private void ApplyPalette()
    {
        Palette.Apply(ActualThemeVariant == ThemeVariant.Light, _settings.Accent);
        if (Avalonia.Application.Current is not { } application) return;
        foreach (var theme in application.Styles.OfType<FluentTheme>())
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                if (!theme.Palettes.TryGetValue(variant, out var palette))
                    theme.Palettes[variant] = palette = new ColorPaletteResources();
                palette.Accent = Palette.AccentFor(_settings.Accent, variant == ThemeVariant.Light);
            }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
            _settingsLoadFailed = false;
            _settingsError = null;
        }
        catch (Exception ex) when (IsSettingsStorageError(ex))
        {
            _settingsError = $"设置尚未保存。检查文件和目录的写入权限后点击重试。\n{ex.Message}";
        }
        _settingsWindow?.UpdateStatus(_settingsError, _settingsLoadFailed);
        UpdateStorageStatus();
    }

    private void RetrySettings()
    {
        if (_settingsLoadFailed)
        {
            ReadSettings();
            ApplySettings();
            _settingsWindow?.SetValues(_settings);
            _settingsWindow?.UpdateStatus(_settingsError, _settingsLoadFailed);
            UpdateStorageStatus();
        }
        else SaveSettings();
    }

    private void ResetSettings()
    {
        // The window asks for confirmation before replacing an unreadable file.
        // From this point Retry means retry the confirmed write, not reload the bad file.
        _settingsLoadFailed = false;
        _settings = new LauncherSettings();
        ApplySettings();
        _settingsWindow?.SetValues(_settings);
        SaveSettings();
    }

    private static bool IsSettingsStorageError(Exception ex) => ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or ArgumentException;
}
