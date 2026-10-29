// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Workspace;

public static class ComponentIds
{
    public const string Settings = "wonder.settings";
    public const string Notes = "wonder.notes";
    public const string LauncherStatus = "wonder.launcher-status";
    public const string Instances = "wonder.instances";
    public const string Launch = "wonder.launch";
    public const string LaunchLog = "wonder.launch-log";
    public const string Accounts = "wonder.accounts";

    internal static bool IsValid(string? value) => value is { Length: > 0 and <= 160 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
