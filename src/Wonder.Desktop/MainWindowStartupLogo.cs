// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Wonder.Desktop;

public sealed partial class MainWindow
{
    private bool _startupLogoPrepared;

    /// <summary>Called by the application before showing its main window, after saved colours are applied.</summary>
    internal void PrepareDailyStartupLogo(DateOnly? today = null, Func<Window, bool>? supportsTransparency = null)
    {
        if (_startupLogoPrepared) return;
        _startupLogoPrepared = true;
        var date = today ?? DateOnly.FromDateTime(DateTime.Now);
        if (_settingsLoadFailed || !_settings.ShowDailyStartupLogo || _settings.LastStartupLogoDate == date ||
            Content is not Control desktop) return;

        supportsTransparency ??= static window => window.ActualTransparencyLevel == WindowTransparencyLevel.Transparent;
        var transparency = TransparencyLevelHint;
        // Avalonia negotiates this hint with the native window synchronously, before its first Show.
        // A compositor without alpha support must open the normal desktop, never an opaque splash.
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        if (!supportsTransparency(this))
        {
            TransparencyLevelHint = transparency;
            return;
        }

        var width = Width;
        var height = Height;
        var minWidth = MinWidth;
        var minHeight = MinHeight;
        var decorations = WindowDecorations;
        var extendClientArea = ExtendClientAreaToDecorationsHint;
        var canResize = CanResize;
        var showInTaskbar = ShowInTaskbar;
        var topmost = Topmost;
        var background = Background;
        var fallback = TransparencyBackgroundFallback;
        var startupLocation = WindowStartupLocation;
        var position = Position;
        var state = WindowState;
        var desktopWasVisible = desktop.IsVisible;
        var desktopWasEnabled = desktop.IsEnabled;

        var mark = new StartupCometLogo(280, _settings.StartupLogoAnimation);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        var overlay = new Grid { Name = "DailyStartupLogo", Background = Brushes.Transparent };
        AutomationProperties.SetName(overlay, "Wonder 启动标志");
        overlay.Children.Add(mark);
        desktop.IsVisible = false;
        desktop.IsEnabled = false;
        Content = overlay;
        WindowState = WindowState.Normal;
        WindowDecorations = WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = false;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        MinWidth = MinHeight = 0;
        Width = Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var clock = new Stopwatch();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var finished = false;
        void Stop()
        {
            finished = true;
            timer.Stop();
            clock.Stop();
            timer.Tick -= Tick;
            Opened -= Start;
            Closed -= Close;
            PropertyChanged -= TransparencyChanged;
        }
        void Finish(bool centerDesktop)
        {
            if (finished) return;
            Stop();
            var screen = centerDesktop ? Screens.ScreenFromWindow(this) ?? Screens.Primary : null;
            WindowDecorations = decorations;
            ExtendClientAreaToDecorationsHint = extendClientArea;
            CanResize = canResize;
            MinWidth = minWidth;
            MinHeight = minHeight;
            Background = background;
            TransparencyBackgroundFallback = fallback;
            TransparencyLevelHint = transparency;
            Topmost = topmost;
            ShowInTaskbar = showInTaskbar;
            WindowStartupLocation = startupLocation;
            desktop.IsVisible = desktopWasVisible;
            desktop.IsEnabled = desktopWasEnabled;
            Content = desktop;
            // CenterScreen is only applied on the first Show. Restore the desktop on this monitor
            // using its current DPI rather than leaving its top-left at the small splash's corner.
            Position = centerDesktop && startupLocation != WindowStartupLocation.Manual && screen is not null
                ? new PixelPoint(screen.WorkingArea.X + (int)Math.Round((screen.WorkingArea.Width - width * screen.Scaling) / 2),
                    screen.WorkingArea.Y + (int)Math.Round((screen.WorkingArea.Height - height * screen.Scaling) / 2))
                : position;
            WindowState = state;
            // Win32 restores the taskbar entry by reparenting its native window. That synchronously
            // reports its old client size and can overwrite pending Width/Height layout requests.
            // Apply our saved size after every native chrome, visibility and position change.
            Width = width;
            Height = height;
        }
        void Tick(object? sender, EventArgs args)
        {
            var elapsed = clock.Elapsed.TotalMilliseconds;
            mark.SetElapsed(elapsed);
            var fadeStart = StartupCometLogo.FlightMilliseconds + StartupCometLogo.SparkMilliseconds + StartupCometLogo.SettleMilliseconds;
            if (elapsed < fadeStart) return;
            var progress = Math.Clamp((elapsed - fadeStart) / StartupCometLogo.FadeMilliseconds, 0, 1);
            overlay.Opacity = 1 - progress * progress * (3 - 2 * progress);
            if (progress >= 1) Finish(centerDesktop: true);
        }
        void Start(object? sender, EventArgs args)
        {
            Opened -= Start;
            if (!supportsTransparency(this))
            {
                Finish(centerDesktop: true);
                return;
            }
            _settings = _settings with { LastStartupLogoDate = date };
            SaveSettings();
            clock.Start();
            timer.Start();
        }
        // Closed has no native platform window left. Only detach the playback resources here.
        void Close(object? sender, EventArgs args) => Stop();
        void TransparencyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == ActualTransparencyLevelProperty && !supportsTransparency(this))
                Finish(centerDesktop: IsVisible);
        }
        timer.Tick += Tick;
        Opened += Start;
        Closed += Close;
        PropertyChanged += TransparencyChanged;
    }
}
