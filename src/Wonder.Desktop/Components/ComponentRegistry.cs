// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Extensibility.Contracts;

namespace Wonder.Desktop.Components;

// Native presentation remains a host detail. The public contracts project does not reference Avalonia.
internal sealed class ComponentRegistry
{
    private readonly Dictionary<string, ComponentRegistration> _registrations = new(StringComparer.Ordinal);
    public IReadOnlyList<ComponentDefinition> Definitions => _registrations.Values.Select(value => value.Definition).ToArray();

    public void Register(ComponentDefinition definition, Func<IComponentState, IComponentRuntime> runtime,
        Func<ComponentPresentationContext, ComponentContent> presentation)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(presentation);
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 160 ||
            !definition.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-') ||
            string.IsNullOrWhiteSpace(definition.Title) ||
            !double.IsFinite(definition.Width) || !double.IsFinite(definition.Height) ||
            definition.Width is <= 0 or > 4096 || definition.Height is <= 0 or > 4096 ||
            definition.Outline is null || definition.Outline.Count is 1 or 2 ||
            definition.Outline.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                point.X is < 0 or > 1 || point.Y is < 0 or > 1))
            throw new ArgumentException("组件定义的标识、尺寸或轮廓无效。", nameof(definition));
        definition = definition with { Outline = Array.AsReadOnly(definition.Outline.ToArray()) };
        if (!_registrations.TryAdd(definition.Id, new(definition, runtime, presentation)))
            throw new ArgumentException("组件定义已经注册。", nameof(definition));
    }

    internal ComponentRegistration? Find(string id) => _registrations.GetValueOrDefault(id);
}

internal sealed record ComponentRegistration(ComponentDefinition Definition,
    Func<IComponentState, IComponentRuntime> CreateRuntime,
    Func<ComponentPresentationContext, ComponentContent> CreatePresentation);
