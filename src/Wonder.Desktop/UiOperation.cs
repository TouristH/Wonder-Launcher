// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Runtime.CompilerServices;
using Wonder.Core;

namespace Wonder.Desktop;

internal static class UiOperation
{
    internal static void Start(Func<Task> action, Func<bool>? closed = null, [CallerMemberName] string operation = "") =>
        _ = ObserveAsync(action, closed, operation);

    private static async Task ObserveAsync(Func<Task> action, Func<bool>? closed, string operation)
    {
        try
        {
            if (closed?.Invoke() != true) await action();
        }
        catch (OperationCanceledException error) when (error.CancellationToken.IsCancellationRequested) { }
        catch (Exception error) { LauncherDiagnostics.Warn(operation, error); }
    }
}
