// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Wonder.Desktop;

public sealed partial class MainWindow
{
    private bool _startupLogoPrepared;

    /// <summary>Called by the application before showing its main window, after saved colours are applied.</summary>
    internal void PrepareDailyStartupLogo(DateOnly? today = null)
    {
        if (_startupLogoPrepared) return;
        _startupLogoPrepared = true;
        var date = today ?? DateOnly.FromDateTime(DateTime.Now);
        if (_settingsLoadFailed || !_settings.ShowDailyStartupLogo || _settings.LastStartupLogoDate == date ||
            Content is not Control desktop) return;

        var mark = new StartupCometLogo(280, _settings.StartupLogoAnimation);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        var overlay = new Grid { Name = "DailyStartupLogo", Background = Palette.Background };
        AutomationProperties.SetName(overlay, "Wonder 启动标志");
        overlay.Children.Add(mark);
        var shell = new Grid();
        Content = null;
        desktop.IsVisible = false;
        desktop.IsEnabled = false;
        shell.Children.Add(desktop);
        shell.Children.Add(overlay);
        Content = shell;

        var clock = new Stopwatch();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        void Finish()
        {
            timer.Stop();
            timer.Tick -= Tick;
            Opened -= Start;
            Closed -= Close;
            desktop.IsVisible = true;
            desktop.IsEnabled = true;
            shell.Children.Clear();
            Content = desktop;
        }
        void Tick(object? sender, EventArgs args)
        {
            var elapsed = clock.Elapsed.TotalMilliseconds;
            mark.SetElapsed(elapsed);
            var fadeStart = StartupCometLogo.FlightMilliseconds + StartupCometLogo.SparkMilliseconds + StartupCometLogo.SettleMilliseconds;
            if (elapsed < fadeStart) return;
            desktop.IsVisible = true;
            var progress = Math.Clamp((elapsed - fadeStart) / StartupCometLogo.FadeMilliseconds, 0, 1);
            overlay.Opacity = 1 - progress * progress * (3 - 2 * progress);
            if (progress >= 1) Finish();
        }
        void Start(object? sender, EventArgs args)
        {
            Opened -= Start;
            _settings = _settings with { LastStartupLogoDate = date };
            SaveSettings();
            clock.Start();
            timer.Start();
        }
        void Close(object? sender, EventArgs args) => Finish();
        timer.Tick += Tick;
        Opened += Start;
        Closed += Close;
    }
}
