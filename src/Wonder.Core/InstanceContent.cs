// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Core;

public enum InstanceContentKind { Mod, ResourcePack, ShaderPack, World, Screenshot }

public sealed record InstanceContentDetail(string Label, string Value);

public sealed record InstanceContent(string Path, string FileName, InstanceContentKind Kind, string Title,
    string? Description, bool Enabled, bool IsDirectory, long? Size, DateTimeOffset ModifiedAt,
    IReadOnlyList<InstanceContentDetail> Details, string? IconPath)
{
    public DateTimeOffset CreatedAt { get; init; }
}
