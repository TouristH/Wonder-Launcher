// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Workspace;

public readonly record struct WorkspaceSaveStatus(long RequestedRevision, long SavedRevision, bool IsSaving, Exception? Error);

/// <summary>Writes detached snapshots in order, coalescing pending work to its latest revision.</summary>
public sealed class WorkspaceSaveQueue(Action<WorkspaceDocument> save) : IDisposable
{
    private readonly object gate = new();
    private (long Revision, WorkspaceDocument Document)? pending;
    private Task worker = Task.CompletedTask;
    private long requestedRevision;
    private long savedRevision;
    private bool running;
    private bool disposed;
    private Exception? error;
    public event EventHandler? Changed;

    public WorkspaceSaveStatus Status
    {
        get { lock (gate) return new(requestedRevision, savedRevision, running, error); }
    }

    /// <summary>The caller transfers ownership of a detached snapshot; it must no longer mutate it.</summary>
    public void Enqueue(long revision, WorkspaceDocument snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (revision < requestedRevision || revision <= savedRevision) return;
            requestedRevision = revision;
            pending = (revision, snapshot);
            if (!running)
            {
                running = true;
                worker = Task.Run(WritePending);
            }
        }
    }

    /// <summary>Waits for accepted work. A failed revision is retained until a later Enqueue retries it.</summary>
    public async Task FlushAsync()
    {
        Task current;
        lock (gate) current = worker;
        await current.ConfigureAwait(false);
        Exception? failure;
        lock (gate) failure = error;
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void WritePending()
    {
        while (true)
        {
            (long Revision, WorkspaceDocument Document) item;
            lock (gate)
            {
                if (pending is not { } next) { running = false; return; }
                item = next;
                pending = null;
            }
            Exception? failure = null;
            try { save(item.Document); }
            catch (Exception exception) { failure = exception; }
            bool stop;
            lock (gate)
            {
                if (failure is null)
                {
                    savedRevision = item.Revision;
                    if (item.Revision >= requestedRevision) error = null;
                    stop = pending is null;
                }
                else
                {
                    error = failure;
                    stop = pending is null;
                    // Retain failed data without repeatedly hitting an unavailable disk.
                    if (stop) pending = item;
                }
                if (stop) running = false;
            }
            var handlers = Changed;
            if (handlers is not null)
                foreach (EventHandler handler in handlers.GetInvocationList())
                    try { handler(this, EventArgs.Empty); } catch { }
            if (stop) return;
        }
    }

    public void Dispose()
    {
        Task current;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Changed = null;
            current = worker;
        }
        // No dispatcher continuation is involved, so synchronous window shutdown cannot deadlock.
        current.GetAwaiter().GetResult();
    }
}
