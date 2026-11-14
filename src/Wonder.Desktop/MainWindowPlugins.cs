// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using Avalonia.Controls;
using Avalonia.Threading;
using Wonder.Extensibility;
using Wonder.Desktop.Components;

namespace Wonder.Desktop;

public sealed partial class MainWindow
{
    private readonly PluginManager _plugins;
    private PluginManagerWindow? _pluginManagerWindow;
    private PluginComponentBridge? _pluginComponents;
    private readonly PluginObservationHub _pluginObservations = new();
    private PluginDesktopObservation? _pluginInput;

    private void InitializePluginComponents()
    {
        _pluginComponents = new PluginComponentBridge(_workspace);
        _pluginInput = new PluginDesktopObservation(this, _pluginObservations);
        _workspace.InteractionObserved += PluginInteractionObserved;
        _workspace.Components.ActionInvoked += PluginComponentActionObserved;
        _instances.Changed += PluginInstanceChanged;
        PluginInstanceChanged(this, EventArgs.Empty);
        _plugins.Changed += PluginComponentsChanged;
        Closed += (_, _) =>
        {
            _plugins.Changed -= PluginComponentsChanged;
            _instances.Changed -= PluginInstanceChanged;
            _workspace.InteractionObserved -= PluginInteractionObserved;
            _workspace.Components.ActionInvoked -= PluginComponentActionObserved;
            _pluginInput.Dispose();
        };
    }

    private void PluginInteractionObserved(string kind, string target, string? detail) =>
        _pluginObservations.RecordInteraction(kind, target, detail);

    private void PluginComponentActionObserved(string definitionId, string actionId) =>
        _pluginObservations.RecordInteraction("component.action",
            _workspace.Components.Registry.Find(definitionId)?.Definition.Title ?? definitionId, actionId);

    private void PluginInstanceChanged(object? sender, EventArgs args)
    {
        var selected = _instances.SelectedInstance;
        _pluginObservations.SetSelectedInstance(selected is null ? null : new(selected.Id, selected.Name));
    }

    private void PluginComponentsChanged(object? sender, EventArgs args)
    {
        void Refresh()
        {
            if (_workspaceStorageClosed) return;
            if (_pluginComponents!.Synchronize(_plugins.Components))
                _library?.RefreshItems(_workspace.Document.Library.ToArray());
        }
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

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
