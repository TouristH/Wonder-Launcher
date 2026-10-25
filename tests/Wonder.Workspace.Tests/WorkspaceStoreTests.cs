// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;
using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Wonder.Workspace.Tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "workspace.json");

    [Fact]
    public void MissingFileLoadsDefaultWorkspaceWithoutWritingAFile()
    {
        var document = new WorkspaceStore(FilePath).Load();

        Assert.Single(document.Areas);
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData(-32.5, 19.25)]
    [InlineData(32.5, -19.25)]
    [InlineData(-1_000_000, 1_000_000)]
    public void RestartPreservesSignedCenterOffsetsNestedLayoutAndApplicationIdentity(double x, double y)
    {
        var store = new WorkspaceStore(FilePath);
        var document = TestWorkspaceFactory.CreateThreeAreas();
        document.Root!.Ratio = .37;
        var component = document.Areas.First().Components.Single();
        component.X = x;
        component.Y = y;
        store.Save(document);

        var restored = new WorkspaceStore(FilePath).Load();
        var restoredComponent = restored.Areas.SelectMany(area => area.Components).Single(item => item.Id == component.Id);

        Assert.Equal(3, restored.Version);
        Assert.Equal(.37, restored.Root!.Ratio);
        Assert.Equal(x, restoredComponent.X);
        Assert.Equal(y, restoredComponent.Y);
        Assert.Equal(component.ApplicationId, restoredComponent.ApplicationId);
        Assert.Same(Assert.Single(restored.Library), restoredComponent.Application);
        Assert.Equal(document.Areas.Select(area => area.Id), restored.Areas.Select(area => area.Id));
    }

    [Fact]
    public void SavingEmptyWorkspaceReplacesExistingLayoutAndRemainsEmptyAfterRestart()
    {
        var store = new WorkspaceStore(FilePath);
        store.Save(TestWorkspaceFactory.CreateThreeAreas());
        store.Save(WorkspaceDocument.CreateEmpty());

        var restored = new WorkspaceStore(FilePath).Load();

        Assert.Null(restored.Root);
        Assert.Empty(restored.Areas);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":4,\"root\":null}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"root\":{\"id\":\"root\",\"ratio\":0.5}}")]
    public void CorruptOrUnsupportedDocumentIsReportedWithoutChangingSource(string source)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, source);

        var error = Assert.Throws<InvalidDataException>(() => new WorkspaceStore(FilePath).Load());

        Assert.Contains("工作区", error.Message);
        Assert.Equal(source, File.ReadAllText(FilePath));
    }

    [Fact]
    public void DuplicateIdentityIsRejectedBeforeReplacingExistingFile()
    {
        var store = new WorkspaceStore(FilePath);
        store.Save(WorkspaceDocument.CreateEmpty());
        var original = File.ReadAllText(FilePath);
        var document = TestWorkspaceFactory.CreateThreeAreas();
        document.Areas.Last().Id = document.Areas.First().Id;

        Assert.Throws<InvalidDataException>(() => store.Save(document));

        Assert.Equal(original, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-.1)]
    [InlineData(1.1)]
    public void InvalidSplitRatioCannotBeSaved(double ratio)
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        document.Root!.Ratio = ratio;

        Assert.Throws<InvalidDataException>(() => new WorkspaceStore(FilePath).Save(document));
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(1, 1_000_001)]
    [InlineData(2, -1_000_001)]
    [InlineData(2, 1_000_001)]
    public void OutOfRangeComponentCoordinateCannotBeLoaded(int version, double x)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, $$"""
            { "version": {{version}}, "root": { "id": "root", "area": { "id": "area", "components": [
              { "id": "component", "kind": 3, "x": {{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}} }
            ] } } }
            """);

        Assert.Throws<InvalidDataException>(() => new WorkspaceStore(FilePath).Load());
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(-1_000_001, 0)]
    [InlineData(0, 1_000_001)]
    public void InvalidV2OffsetCannotOverwriteAnExistingWorkspace(double x, double y)
    {
        var store = new WorkspaceStore(FilePath);
        store.Save(WorkspaceDocument.CreateEmpty());
        var original = File.ReadAllBytes(FilePath);
        var document = TestWorkspaceFactory.CreateThreeAreas();
        document.Areas.First().Components.Single().X = x;
        document.Areas.First().Components.Single().Y = y;

        Assert.Throws<InvalidDataException>(() => store.Save(document));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void LoadingV1PreservesVersionTopLeftCoordinatesAndOriginalBytes()
    {
        const string source = """
            { "version": 1, "root": { "id": "node-1", "area": {
              "id": "area-1", "title": "existing", "components": [
                { "id": "component-1", "kind": 3, "x": 32.5, "y": 19.25 }
              ]
            } } }
            """;
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, source);
        var original = File.ReadAllBytes(FilePath);

        var document = new WorkspaceStore(FilePath).Load();

        Assert.Equal(1, document.Version);
        var component = Assert.Single(Assert.Single(document.Areas).Components);
        Assert.Equal(32.5, component.X);
        Assert.Equal(19.25, component.Y);
        Assert.Same(Assert.Single(document.Library), component.Application);
        Assert.Equal(original, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void SavingBeforeV1LayoutRequiresCoordinateConversion()
    {
        var document = WorkspaceDocument.CreateEmpty();
        document.Version = 1;
        document.AddArea().Components.Add(new ComponentItem { DefinitionId = ComponentIds.Settings, X = 32, Y = 24 });
        var store = new WorkspaceStore(FilePath);

        Assert.Throws<InvalidDataException>(() => store.Save(document));
        Assert.Equal(1, document.Version);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void RestartBindsAllSettingsShortcutsToOneAuthoritativeApplication()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var application = document.Library.Single(item => item.DefinitionId == ComponentIds.Settings);
        document.AddArea().Components.Add(document.CreateShortcut(application));
        document.AddArea().Components.Add(document.CreateShortcut(application));
        var store = new WorkspaceStore(FilePath);
        store.Save(document);

        var restored = store.Load();
        var restoredApplication = restored.Library.Single(item => item.Id == application.Id);
        var shortcuts = restored.Areas.SelectMany(area => area.Components).ToArray();

        Assert.Equal(2, shortcuts.Length);
        Assert.All(shortcuts, item => Assert.Same(restoredApplication, item.Application));
        Assert.NotEqual(shortcuts[0].Id, shortcuts[1].Id);
    }

    [Fact]
    public void EmptyWorkspaceRetainsApplicationIdentityAfterRestart()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        var applicationId = Assert.Single(document.Library).Id;
        document.Root = null;
        var store = new WorkspaceStore(FilePath);

        store.Save(document);
        var restored = store.Load();

        Assert.Null(restored.Root);
        var application = restored.Library.Single(item => item.DefinitionId == ComponentIds.Settings);
        Assert.Equal(applicationId, application.Id);
        Assert.Same(application, restored.CreateShortcut(application).Application);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("self")]
    [InlineData("duplicate-id")]
    [InlineData("source-link")]
    [InlineData("wrong-definition")]
    public void InvalidLibraryReferencesCannotReplaceExistingFile(string defect)
    {
        var store = new WorkspaceStore(FilePath);
        store.Save(WorkspaceDocument.CreateEmpty());
        var original = File.ReadAllBytes(FilePath);
        var document = TestWorkspaceFactory.CreateThreeAreas();
        var shortcut = document.Areas.First().Components.Single();
        var application = document.Library.Single(item => item.DefinitionId == shortcut.DefinitionId);
        switch (defect)
        {
            case "unknown": shortcut.ApplicationId = "unknown-application"; break;
            case "self": shortcut.ApplicationId = shortcut.Id; break;
            case "duplicate-id": application.Id = shortcut.Id; break;
            case "source-link": application.ApplicationId = document.Library.Last().Id; break;
            case "wrong-definition": shortcut.DefinitionId = ComponentIds.Notes; break;
        }

        Assert.Throws<InvalidDataException>(() => store.Save(document));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void LoadingUnknownApplicationReferenceKeepsOriginalFile()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        var store = new WorkspaceStore(FilePath);
        store.Save(document);
        var json = JsonNode.Parse(File.ReadAllText(FilePath))!;
        json["root"]!["first"]!["area"]!["components"]![0]!["applicationId"] = "unknown-application";
        var source = json.ToJsonString();
        File.WriteAllText(FilePath, source);

        Assert.Throws<InvalidDataException>(() => store.Load());
        Assert.Equal(source, File.ReadAllText(FilePath));
    }

    [Fact]
    public void SidebarEdgeAndExpandedExtentSurviveRestart()
    {
        var document = TestWorkspaceFactory.CreateThreeAreas();
        var area = document.Areas.First();
        area.SidebarEdge = SidebarEdge.Left;
        area.SidebarExtent = 375;
        var store = new WorkspaceStore(FilePath);

        store.Save(document);
        var restored = store.Load();

        Assert.Equal(SidebarEdge.Left, restored.FindArea(area.Id)!.SidebarEdge);
        Assert.Equal(375, restored.FindArea(area.Id)!.SidebarExtent);
    }

    [Theory]
    [InlineData(99, 320)]
    [InlineData(1, double.NaN)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(1, 79)]
    [InlineData(1, 1_000_001)]
    public void InvalidSidebarMetadataCannotOverwriteExistingFile(int edge, double extent)
    {
        var store = new WorkspaceStore(FilePath);
        store.Save(WorkspaceDocument.CreateEmpty());
        var original = File.ReadAllBytes(FilePath);
        var document = TestWorkspaceFactory.CreateThreeAreas();
        document.Areas.First().SidebarEdge = (SidebarEdge)edge;
        document.Areas.First().SidebarExtent = extent;

        Assert.Throws<InvalidDataException>(() => store.Save(document));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
