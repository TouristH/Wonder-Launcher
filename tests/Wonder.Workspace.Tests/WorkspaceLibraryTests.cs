// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class WorkspaceLibraryTests
{
    [Fact]
    public void EnsuringDefinitionsAddsOnlyMissingApplicationsWithoutAddingAreasOrShortcuts()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var settings = Assert.Single(document.Library);
        var notes = document.CreateApplication(ComponentIds.Notes);
        notes.State["text"] = "保留";

        document.EnsureLibrary([ComponentIds.Settings, ComponentIds.Notes, ComponentIds.LauncherStatus]);
        document.EnsureLibrary([ComponentIds.Settings, ComponentIds.Notes, ComponentIds.LauncherStatus]);

        Assert.Equal(3, document.Library.Count);
        Assert.Same(settings, document.Library.Single(item => item.DefinitionId == ComponentIds.Settings));
        Assert.Same(notes, document.Library.Single(item => item.DefinitionId == ComponentIds.Notes));
        Assert.Equal("保留", notes.State["text"]);
        Assert.Null(document.Root);
    }

    [Fact]
    public void SeparateApplicationsHaveIndependentStateWhileTheirShortcutsShareIt()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var first = document.CreateApplication(ComponentIds.Notes);
        var second = document.CreateApplication(ComponentIds.Notes);
        var shortcut = document.CreateShortcut(first);
        var duplicateShortcut = document.CreateShortcut(first);

        shortcut.State["text"] = "第一份笔记";
        second.State["text"] = "第二份笔记";
        document.EnsureLibrary();

        Assert.NotEqual(first.Id, second.Id);
        Assert.Same(first.State, shortcut.State);
        Assert.Same(first.State, duplicateShortcut.State);
        Assert.Equal("第一份笔记", duplicateShortcut.State["text"]);
        Assert.Equal("第二份笔记", second.State["text"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad/id")]
    public void InvalidDefinitionCannotCreateApplicationOrAlterLibrary(string definitionId)
    {
        var document = WorkspaceDocument.CreateEmpty();

        Assert.Throws<ArgumentException>(() => document.CreateApplication(definitionId));

        Assert.Single(document.Library);
    }

    [Fact]
    public void EmptyWorkspaceIncludesSettingsApplicationWithoutCreatingAreas()
    {
        var document = WorkspaceDocument.CreateEmpty();

        Assert.Null(document.Root);
        var settings = Assert.Single(document.Library);
        Assert.Equal(ComponentIds.Settings, settings.DefinitionId);
        Assert.Same(settings, settings.Application);
    }

    [Fact]
    public void LibraryAndShortcutsShareApplicationIdentityWithIndependentPositions()
    {
        var source = new ComponentItem { DefinitionId = ComponentIds.Settings };
        var document = new WorkspaceDocument { Library = [source] };
        var first = document.CreateShortcut(source);
        var second = document.CreateShortcut(source);

        first.X = 24;
        second.Y = -32;

        Assert.Same(source, first.Application);
        Assert.Same(source, second.Application);
        Assert.Equal(source.Id, first.ApplicationId);
        Assert.NotEqual(source.Id, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(ComponentIds.Settings, first.DefinitionId);
        Assert.Equal(24, first.X);
        Assert.Equal(0, second.X);
        Assert.Equal(-32, second.Y);
        Assert.Equal(0, source.X);
        Assert.Equal(0, source.Y);
    }

    [Fact]
    public void RemovingTheLastShortcutPreservesApplicationIdentity()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var application = Assert.Single(document.Library);
        var area = document.AddArea();
        var shortcut = document.CreateShortcut(application);
        area.Components.Add(shortcut);

        Assert.True(document.RemoveArea(area.Id));

        Assert.Null(document.Root);
        Assert.Same(application, Assert.Single(document.Library));
        Assert.Same(application, document.CreateShortcut(application).Application);
    }

    [Fact]
    public void MissingLibraryBindsExistingSettingsShortcutsWithoutChangingCoordinatesOrVersion()
    {
        var document = new WorkspaceDocument { Version = 1 };
        var first = new ComponentItem { DefinitionId = ComponentIds.Settings, X = 32, Y = 24 };
        var second = new ComponentItem { DefinitionId = ComponentIds.Settings };
        document.AddArea().Components.Add(first);
        document.AddArea().Components.Add(second);

        document.EnsureLibrary();
        document.EnsureLibrary();

        Assert.Equal(1, document.Version);
        var application = Assert.Single(document.Library);
        Assert.Same(application, first.Application);
        Assert.Same(application, second.Application);
        Assert.Equal(32, first.X);
        Assert.Equal(24, first.Y);
    }

    [Fact]
    public void ExplicitApplicationReferenceIsRestoredWithoutChangingItsIdentity()
    {
        var application = new ComponentItem { DefinitionId = ComponentIds.Settings };
        var document = new WorkspaceDocument { Library = [application] };
        var shortcut = new ComponentItem { DefinitionId = ComponentIds.Settings, ApplicationId = application.Id };
        document.AddArea().Components.Add(shortcut);

        document.EnsureLibrary();

        Assert.Same(application, shortcut.Application);
        Assert.Equal(application.Id, shortcut.ApplicationId);
    }

    [Fact]
    public void UnknownExplicitApplicationIdIsRejectedWithoutReplacingItsIdentity()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var shortcut = new ComponentItem { DefinitionId = ComponentIds.Settings, ApplicationId = "unknown-application" };
        document.AddArea().Components.Add(shortcut);

        Assert.Throws<InvalidDataException>(() => document.EnsureLibrary());

        Assert.Equal("unknown-application", shortcut.ApplicationId);
    }
}
