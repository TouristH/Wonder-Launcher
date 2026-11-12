// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia.Controls;
using Wonder.Extensibility;

namespace Wonder.Desktop;

public sealed partial class MainWindow
{
    private readonly PluginManager _plugins;
    private PluginManagerWindow? _pluginManagerWindow;

    private void OpenPluginManager()
    {
        _workspace.CancelGesture();
        if (_pluginManagerWindow is not null)
        {
            if (_pluginManagerWindow.WindowState == WindowState.Minimized)
                _pluginManagerWindow.WindowState = WindowState.Normal;
            _pluginManagerWindow.Activate();
            return;
        }
        _pluginManagerWindow = new PluginManagerWindow(_plugins) { FontFamily = FontFamily };
        _pluginManagerWindow.Closed += (_, _) => _pluginManagerWindow = null;
        _pluginManagerWindow.Show(this);
    }
}
