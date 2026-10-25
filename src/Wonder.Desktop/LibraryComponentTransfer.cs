// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Avalonia;
using Avalonia.Input;
using Wonder.Workspace;

namespace Wonder.Desktop;

internal sealed record LibraryComponentTransfer(ComponentItem Application, Vector GrabOffset)
{
    // Only this running application can supply live library components.
    public static readonly DataFormat<LibraryComponentTransfer> Format =
        DataFormat.CreateInProcessFormat<LibraryComponentTransfer>("Wonder.ComponentLibrary");
}
