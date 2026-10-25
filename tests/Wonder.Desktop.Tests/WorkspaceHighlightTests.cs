// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wonder.Workspace;
using Xunit;

namespace Wonder.Desktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaFact]
    public async Task Component_press_hold_and_cross_region_drag_never_highlight_any_area()
    {
        using var desktop = new TestDesktop();
        var component = desktop.Component(0);
        var areas = desktop.View.GetVisualDescendants().OfType<AreaView>().ToArray();
        var before = areas.Select(area => (area.BorderBrush, area.Background, area.Opacity)).ToArray();
        void Unchanged() => Assert.Equal(before, areas.Select(area => (area.BorderBrush, area.Background, area.Opacity)));

        desktop.Press(desktop.View.GetComponentBounds(component.Id).Center);
        Unchanged();
        await Task.Delay(WorkspaceView.HoldMilliseconds + 100);
        Dispatcher.UIThread.RunJobs();
        Unchanged();
        var target = Center(desktop.View.Snapshot.Areas[desktop.View.Document.Areas.Last().Id]);
        desktop.Move(target);
        Unchanged();
        desktop.Escape();
        Unchanged();
    }
}
