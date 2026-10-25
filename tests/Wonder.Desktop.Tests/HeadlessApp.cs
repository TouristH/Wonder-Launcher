// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Wonder.Desktop.Tests.HeadlessApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Wonder.Desktop.Tests;

public sealed class HeadlessApp : Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        HoverTipBehavior.Initialize();
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
