// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia.Input;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed record WorkspaceComponentTransfer(WorkspaceView Source, ComponentItem Shortcut)
{
    public WorkspaceDocument Document { get; } = Source.Document;
    public static readonly DataFormat<WorkspaceComponentTransfer> Format =
        DataFormat.CreateInProcessFormat<WorkspaceComponentTransfer>("Wonder.WorkspaceShortcut");
}
