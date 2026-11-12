// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Controls;
using Avalonia.Threading;
using Wonder.Core;
using Wonder.Workspace;

namespace Wonder.Desktop;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _workspaceSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private WorkspaceSaveQueue _workspaceSaves = null!;
    private long _workspaceRevision;
    private bool _workspaceStorageClosed;
    private bool _waitingToClose;
    private bool _forceClose;
    private string? _closeError;

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
            LauncherDiagnostics.Warn("CaptureWorkspace", exception);
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

    protected override void OnClosing(WindowClosingEventArgs args)
    {
        base.OnClosing(args);
        if (args.Cancel || _forceClose) return;
        if (_waitingToClose) { args.Cancel = true; return; }

        _closeError = null;
        try
        {
            var accounts = _accounts.WaitForIdleAsync();
            var instances = _instances.WaitForPersistenceAsync();
            if (!accounts.IsCompletedSuccessfully || !instances.IsCompletedSuccessfully)
            {
                args.Cancel = true;
                _accounts.Cancel();
                _waitingToClose = true;
                UpdateStorageStatus();
                UiOperation.Start(() => CloseAfterStorageAsync(accounts, instances), () => _workspaceStorageClosed);
                return;
            }
            CloseWorkspace(args);
            if (args.Cancel) SetCloseError("布局保存未完成。");
        }
        catch (Exception error)
        {
            _waitingToClose = false;
            args.Cancel = true;
            LauncherDiagnostics.Warn("CloseLauncher", error);
            SetCloseError("本地存储操作异常。");
        }
    }

    internal async Task CloseAfterStorageAsync(Task accounts, Task instances)
    {
        var pending = Task.WhenAll(accounts, instances);
        try
        {
            await pending;
            // The canceled closing event must finish before requesting close again.
            await Dispatcher.Yield();
        }
        catch (Exception error)
        {
            LauncherDiagnostics.Warn("WaitForStorageOnClose", pending.Exception ?? error);
            if (!_workspaceStorageClosed) SetCloseError("等待账户或实例存储操作时失败。");
            return;
        }
        finally
        {
            _waitingToClose = false;
            if (!_workspaceStorageClosed) UpdateStorageStatus();
        }
        if (!_workspaceStorageClosed) Close();
    }

    private void SetCloseError(string message)
    {
        _closeError = message + " 可以重试关闭，或强制退出。未完成保存的更改可能丢失。";
        UpdateStorageStatus();
    }

    private void ForceClose()
    {
        _forceClose = true;
        try { Close(); }
        finally { _forceClose = false; }
    }

    private void CloseWorkspace(WindowClosingEventArgs args)
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
