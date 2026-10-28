// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Controls;
using Avalonia.Threading;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _workspaceSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private WorkspaceSaveQueue _workspaceSaves = null!;
    private long _workspaceRevision;
    private bool _workspaceStorageClosed;

    private void InitializeWorkspaceStorage(Action<WorkspaceDocument> saveWorkspace)
    {
        _workspaceSaves = new WorkspaceSaveQueue(saveWorkspace);
        _workspaceSaves.Changed += WorkspaceSaveCompleted;
        _workspaceSaveTimer.Tick += SaveWorkspaceAfterDelay;
    }

    private void WorkspaceChanged(object? sender, EventArgs args)
    {
        if (_loadFailed || _workspaceStorageClosed) return;
        _workspaceRevision++;
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }

    private void SaveWorkspaceAfterDelay(object? sender, EventArgs args)
    {
        // Keep the dirty revision pending while a divider or other layout gesture is a preview.
        // Cancellation rolls back without Changed; the next tick captures only committed state.
        if (_workspace.HasPendingGesture) return;
        _workspaceSaveTimer.Stop();
        QueueWorkspaceSave();
    }

    private bool QueueWorkspaceSave()
    {
        if (_loadFailed || _workspaceStorageClosed) return false;
        try
        {
            var snapshot = WorkspaceStore.CaptureSnapshot(_workspace.Document);
            _workspaceSaves.Enqueue(_workspaceRevision, snapshot);
            return true;
        }
        catch (Exception exception) when (IsWorkspaceStorageError(exception))
        {
            _error = $"布局尚未保存。\n{exception.Message}";
            UpdateStorageStatus();
            return false;
        }
    }

    private void RetryWorkspaceSave()
    {
        if (_loadFailed || _workspaceStorageClosed) return;
        _workspace.CancelGesture();
        _workspaceSaveTimer.Stop();
        _workspaceRevision++;
        QueueWorkspaceSave();
    }

    private void WorkspaceSaveCompleted(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() =>
    {
        if (!_workspaceStorageClosed) ApplyWorkspaceSaveStatus();
    });

    private void ApplyWorkspaceSaveStatus()
    {
        var status = _workspaceSaves.Status;
        if (status.Error is not null) _error = $"布局尚未保存。\n{status.Error.Message}";
        else if (status.SavedRevision >= _workspaceRevision)
        {
            _error = null;
            if (_workspace.Document.Version == 2) _workspace.Document.Version = 3;
        }
        UpdateStorageStatus();
    }

    private void CloseWorkspace(object? sender, WindowClosingEventArgs args)
    {
        _workspace.CancelGesture();
        if (_loadFailed || args.Cancel) return;
        _workspaceSaveTimer.Stop();
        _workspaceRevision++;
        if (!QueueWorkspaceSave()) { args.Cancel = true; return; }
        try { _workspaceSaves.FlushAsync().GetAwaiter().GetResult(); }
        catch (Exception exception) when (IsWorkspaceStorageError(exception))
        {
            _error = $"布局尚未保存。\n{exception.Message}";
            args.Cancel = true;
        }
        ApplyWorkspaceSaveStatus();
    }

    private void DisposeWorkspaceStorage()
    {
        _workspaceStorageClosed = true;
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Tick -= SaveWorkspaceAfterDelay;
        _workspace.Changed -= WorkspaceChanged;
        _workspaceSaves.Changed -= WorkspaceSaveCompleted;
        _workspaceSaves.Dispose();
    }

    private static bool IsWorkspaceStorageError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or ArgumentException;
}
