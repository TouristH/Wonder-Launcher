// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Controls;
using Avalonia.Input;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed class ComponentLibraryWindow : Window
{
    private readonly ComponentLibraryView _library;

    public ComponentLibraryWindow(WorkspaceView workspace, ComponentItem[] items)
    {
        Title = "组件库 · Wonder";
        Width = 690;
        Height = 670;
        MinWidth = 390;
        MinHeight = 440;
        Background = Palette.Background;
        Foreground = Palette.Text;
        _library = new ComponentLibraryView(workspace, items, Close);
        Content = _library;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty)
                _library.SetRuntimeVisible(IsVisible && WindowState != WindowState.Minimized);
        };
        Closed += (_, _) => _library.Detach();
    }

}
