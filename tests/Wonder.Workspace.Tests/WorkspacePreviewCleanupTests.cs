// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json.Nodes;
using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class WorkspacePreviewCleanupTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Wonder.Workspace.Tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "workspace.json");

    [Fact]
    public void MissingWorkspaceStartsWithOneSettingsShortcutInOneWorkspaceArea()
    {
        var document = new WorkspaceStore(FilePath).Load();

        var application = Assert.Single(document.Library);
        Assert.Equal(ComponentIds.Settings, application.DefinitionId);
        var area = Assert.Single(document.Areas);
        Assert.Equal("工作区", area.Title);
        Assert.Same(application, Assert.Single(area.Components).Application);
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacyPreviewCleanupPreservesLayoutSettingsIdentityOffsetsAndOriginalBytes(int version)
    {
        var source = $$"""
            { "version": {{version}}, "library": [
              { "id": "old-hexagon", "kind": 0, "value": 4 },
              { "id": "old-diamond", "kind": 1, "value": 5 },
              { "id": "old-buttons", "kind": 2, "value": 6 },
              { "id": "settings-app", "kind": 3 }
            ], "root": { "id": "split", "axis": 0, "ratio": 0.37,
              "first": { "id": "left", "area": {
                "id": "left-area", "title": "我的区域", "sidebarEdge": 1, "sidebarExtent": 375,
                "components": [
                  { "id": "hexagon-shortcut", "kind": 0, "applicationId": "old-hexagon", "value": 4 },
                  { "id": "settings-shortcut", "kind": 3, "applicationId": "settings-app", "x": 32.5, "y": 19.25 }
                ]
              } },
              "second": { "id": "right", "axis": 1, "ratio": 0.62,
                "first": { "id": "upper", "area": {
                  "id": "upper-area", "title": "保留空区域", "components": [
                    { "id": "diamond-shortcut", "kind": 1, "applicationId": "old-diamond", "value": 5 }
                  ]
                } },
                "second": { "id": "lower", "area": {
                  "id": "lower-area", "title": "另一区域", "components": [
                    { "id": "buttons-shortcut", "kind": 2, "applicationId": "old-buttons", "value": 6 }
                  ]
                } }
              }
            } }
            """;
        WriteSource(source);

        var store = new WorkspaceStore(FilePath);
        var document = store.Load();

        Assert.Equal(source, File.ReadAllText(FilePath));
        Assert.Equal(version, document.Version);
        Assert.Equal(.37, document.Root!.Ratio);
        Assert.Equal(.62, document.Root.Second!.Ratio);
        Assert.Equal(SplitAxis.Vertical, document.Root.Second.Axis);
        Assert.Equal(new[] { "left-area", "upper-area", "lower-area" }, document.Areas.Select(area => area.Id));
        Assert.Equal(new[] { "我的区域", "保留空区域", "另一区域" }, document.Areas.Select(area => area.Title));
        var left = document.FindArea("left-area")!;
        Assert.Equal(SidebarEdge.Left, left.SidebarEdge);
        Assert.Equal(375, left.SidebarExtent);
        Assert.Empty(document.FindArea("upper-area")!.Components);
        Assert.Empty(document.FindArea("lower-area")!.Components);
        var settings = Assert.Single(left.Components);
        Assert.Equal("settings-shortcut", settings.Id);
        Assert.Equal(32.5, settings.X);
        Assert.Equal(19.25, settings.Y);
        Assert.Same(Assert.Single(document.Library), settings.Application);
        Assert.Equal("settings-app", settings.ApplicationId);

        // The desktop converts v1 coordinates before asking the store to persist the workspace.
        document.Version = 2;
        store.Save(document);
        var saved = File.ReadAllText(FilePath);
        Assert.DoesNotContain("old-", saved);
        Assert.DoesNotContain("\"value\"", saved);
        var restored = store.Load();
        Assert.Equal(document.Areas.Select(area => area.Id), restored.Areas.Select(area => area.Id));
        Assert.Single(restored.Areas.SelectMany(area => area.Components));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PreviewWithoutLibraryRemovesAllSampleKindsWithoutAddingShortcuts(int version)
    {
        var source = $$"""
            { "version": {{version}}, "root": { "id": "root", "area": {
              "id": "area", "title": "自选区域", "components": [
                { "id": "one", "kind": 0, "value": 10 },
                { "id": "two", "kind": 1, "value": 20 },
                { "id": "three", "kind": 2, "value": 30 }
              ]
            } } }
            """;
        WriteSource(source);

        var document = new WorkspaceStore(FilePath).Load();

        Assert.Equal("自选区域", Assert.Single(document.Areas).Title);
        Assert.Empty(Assert.Single(document.Areas).Components);
        Assert.Equal(ComponentIds.Settings, Assert.Single(document.Library).DefinitionId);
        Assert.Equal(source, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptyPreviewStaysEmptyWhileReplacingItsSampleLibrary(int version)
    {
        var source = $$"""
            { "version": {{version}}, "root": null, "library": [
              { "id": "one", "kind": 0, "value": 9 },
              { "id": "two", "kind": 1, "value": 8 },
              { "id": "three", "kind": 2, "value": 7 }
            ] }
            """;
        WriteSource(source);
        var store = new WorkspaceStore(FilePath);

        var document = store.Load();
        document.Version = 2;
        store.Save(document);
        var restored = store.Load();

        Assert.Null(restored.Root);
        Assert.Empty(restored.Areas);
        Assert.Equal(ComponentIds.Settings, Assert.Single(restored.Library).DefinitionId);
    }

    [Theory]
    [InlineData("unknown-kind")]
    [InlineData("unknown-application-kind")]
    [InlineData("duplicate-identity")]
    [InlineData("unknown-reference")]
    [InlineData("mismatched-reference")]
    [InlineData("invalid-offset")]
    [InlineData("invalid-value")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-application-kind")]
    public void InvalidLegacyDataCannotBeHiddenBySampleCleanup(string defect)
    {
        var json = JsonNode.Parse("""
            { "version": 2, "library": [{ "id": "application", "kind": 0, "value": 7 }],
              "root": { "id": "root", "area": { "id": "area", "components": [
                { "id": "shortcut", "kind": 0, "applicationId": "application", "value": 7 }
              ] } } }
            """)!;
        var shortcut = json["root"]!["area"]!["components"]![0]!;
        switch (defect)
        {
            case "unknown-kind": shortcut["kind"] = 99; break;
            case "unknown-application-kind": json["library"]![0]!["kind"] = 99; break;
            case "duplicate-identity": shortcut["id"] = "area"; break;
            case "unknown-reference": shortcut["applicationId"] = "missing"; break;
            case "mismatched-reference": shortcut["kind"] = 1; break;
            case "invalid-offset": shortcut["x"] = -1_000_001; break;
            case "invalid-value": shortcut["value"] = "bad"; break;
            case "unknown-field": shortcut["surprise"] = true; break;
            case "duplicate-application-kind": json["library"]!.AsArray().Add(new JsonObject { ["id"] = "duplicate", ["kind"] = 0 }); break;
        }
        var source = json.ToJsonString();
        WriteSource(source);

        Assert.Throws<InvalidDataException>(() => new WorkspaceStore(FilePath).Load());

        Assert.Equal(source, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    public void GenericSchemaRejectsLegacyNumericKindsWithoutChangingTheFile(int kind)
    {
        var original = $$"""{ "version": 3, "root": null, "library": [{ "id": "application", "kind": {{kind}} }] }""";
        WriteSource(original);

        Assert.Throws<InvalidDataException>(() => new WorkspaceStore(FilePath).Load());

        Assert.Equal(original, File.ReadAllText(FilePath));
    }

    private void WriteSource(string source)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, source);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
