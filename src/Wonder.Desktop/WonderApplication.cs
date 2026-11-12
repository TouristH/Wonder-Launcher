// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Wonder.Core;

namespace Wonder.Desktop;

public sealed class WonderApplication : Avalonia.Application
{
    public override void Initialize()
    {
        Dispatcher.UIThread.UnhandledException += (_, args) =>
            LauncherDiagnostics.Error("Avalonia.Dispatcher", args.Exception);
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        HoverTipBehavior.Initialize();
        ButtonClickRipples.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
