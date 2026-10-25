// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json.Nodes;
using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class ComponentPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Wonder.Workspace.Tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "workspace.json");

    [Fact]
    public void MissingProviderAndItsStateSurviveRestart()
    {
        WriteSource("""
            { "version": 3, "library": [
              { "id": "missing-app", "definitionId": "example.missing", "state": { "text": "保留笔记" } }
            ], "root": { "id": "root", "area": { "id": "area", "components": [
              { "id": "shortcut", "definitionId": "example.missing", "applicationId": "missing-app" }
            ] } } }
            """);
        var store = new WorkspaceStore(FilePath);

        var document = store.Load();
        store.Save(document);
        var restored = store.Load();

        Assert.Equal("missing-app", Assert.Single(Assert.Single(restored.Areas).Components).Application.Id);
        var saved = JsonNode.Parse(File.ReadAllText(FilePath))!;
        var application = saved["library"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "missing-app")!;
        Assert.Equal("example.missing", application["definitionId"]!.GetValue<string>());
        Assert.Equal("保留笔记", application["state"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void SameDefinitionApplicationsRetainDistinctStateAndShortcutTargets()
    {
        WriteSource("""
            { "version": 3, "library": [
              { "id": "first", "definitionId": "wonder.notes", "state": { "text": "第一份" } },
              { "id": "second", "definitionId": "wonder.notes", "state": { "text": "第二份" } }
            ], "root": { "id": "root", "area": { "id": "area", "components": [
              { "id": "shortcut-one", "definitionId": "wonder.notes", "applicationId": "first" },
              { "id": "shortcut-two", "definitionId": "wonder.notes", "applicationId": "second" }
            ] } } }
            """);
        var store = new WorkspaceStore(FilePath);

        store.Save(store.Load());
        var restored = store.Load();

        Assert.Equal(new[] { "first", "second" }, Assert.Single(restored.Areas).Components.Select(item => item.Application.Id));
        var saved = JsonNode.Parse(File.ReadAllText(FilePath))!;
        var applications = saved["library"]!.AsArray();
        Assert.Equal("第一份", applications.Single(item => item!["id"]!.GetValue<string>() == "first")!["state"]!["text"]!.GetValue<string>());
        Assert.Equal("第二份", applications.Single(item => item!["id"]!.GetValue<string>() == "second")!["state"]!["text"]!.GetValue<string>());
        Assert.All(saved["root"]!["area"]!["components"]!.AsArray(), shortcut => Assert.Null(shortcut!["state"]));
    }

    [Fact]
    public void SavingV2MigratesToGenericV3WithoutChangingExistingApplicationIdentity()
    {
        WriteSource("""
            { "version": 2, "library": [{ "id": "settings", "kind": 3 }],
              "root": { "id": "root", "area": { "id": "area", "components": [
                { "id": "shortcut", "kind": 3, "applicationId": "settings", "x": -25, "y": 30 }
              ] } } }
            """);
        var store = new WorkspaceStore(FilePath);
        var document = store.Load();

        store.Save(document);

        var saved = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal(3, saved["version"]!.GetValue<int>());
        Assert.Equal("wonder.settings", saved["library"]![0]!["definitionId"]!.GetValue<string>());
        Assert.Null(saved["library"]![0]!["kind"]);
        Assert.Equal("settings", Assert.Single(Assert.Single(store.Load().Areas).Components).ApplicationId);
    }

    [Fact]
    public void V1CoordinatesCannotBeRelabeledBeforeLayoutConversion()
    {
        const string source = """
            { "version": 1, "root": { "id": "root", "area": { "id": "area", "components": [
              { "id": "shortcut", "kind": 3, "x": 20, "y": 30 }
            ] } } }
            """;
        WriteSource(source);
        var store = new WorkspaceStore(FilePath);
        var document = store.Load();

        Assert.Throws<InvalidDataException>(() => store.Save(document));

        Assert.Equal(1, document.Version);
        Assert.Equal(source, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("missing-definition")]
    [InlineData("blank-definition")]
    [InlineData("invalid-definition")]
    [InlineData("long-definition")]
    [InlineData("numeric-kind")]
    [InlineData("null-state")]
    [InlineData("null-state-value")]
    [InlineData("numeric-state-value")]
    [InlineData("empty-state-key")]
    [InlineData("long-state-key")]
    [InlineData("long-state-value")]
    [InlineData("too-many-state-values")]
    [InlineData("shortcut-state")]
    [InlineData("mismatched-reference")]
    [InlineData("unknown-metadata")]
    public void MalformedGenericMetadataPreservesOriginalFile(string defect)
    {
        var json = JsonNode.Parse("""
            { "version": 3, "library": [
              { "id": "application", "definitionId": "wonder.notes", "state": { "text": "笔记" } }
            ], "root": { "id": "root", "area": { "id": "area", "components": [
              { "id": "shortcut", "definitionId": "wonder.notes", "applicationId": "application" }
            ] } } }
            """)!;
        var application = json["library"]![0]!.AsObject();
        var shortcut = json["root"]!["area"]!["components"]![0]!;
        switch (defect)
        {
            case "missing-definition": application.Remove("definitionId"); break;
            case "blank-definition": application["definitionId"] = " "; break;
            case "invalid-definition": application["definitionId"] = "example/component"; break;
            case "long-definition": application["definitionId"] = new string('x', 161); break;
            case "numeric-kind": application["kind"] = 3; break;
            case "null-state": application["state"] = null; break;
            case "null-state-value": application["state"]!["text"] = null; break;
            case "numeric-state-value": application["state"]!["text"] = 42; break;
            case "empty-state-key": application["state"]![""] = "bad"; break;
            case "long-state-key": application["state"]![new string('x', 81)] = "bad"; break;
            case "long-state-value": application["state"]!["text"] = new string('x', 65_537); break;
            case "too-many-state-values":
                application["state"] = new JsonObject(Enumerable.Range(0, 65)
                    .Select(index => KeyValuePair.Create<string, JsonNode?>($"key{index}", JsonValue.Create("value"))));
                break;
            case "shortcut-state": shortcut["state"] = new JsonObject(); break;
            case "mismatched-reference": shortcut["definitionId"] = "wonder.settings"; break;
            case "unknown-metadata": application["surprise"] = true; break;
        }
        var source = json.ToJsonString();
        WriteSource(source);

        Assert.Throws<InvalidDataException>(() => new WorkspaceStore(FilePath).Load());

        Assert.Equal(source, File.ReadAllText(FilePath));
    }

    [Fact]
    public void StateAtPerEntryLimitsSurvivesRestart()
    {
        var document = WorkspaceDocument.CreateEmpty();
        var application = document.CreateApplication(ComponentIds.Notes);
        application.State[new string('k', 80)] = new string('x', 65_536);
        var store = new WorkspaceStore(FilePath);

        store.Save(document);

        var restored = store.Load().Library.Single(item => item.Id == application.Id);
        Assert.Equal(65_536, restored.State[new string('k', 80)].Length);
    }

    [Fact]
    public void OversizedAggregateStateCannotReplaceFileOrMarkVersionMigrated()
    {
        var store = new WorkspaceStore(FilePath);
        store.Save(WorkspaceDocument.CreateEmpty());
        var original = File.ReadAllBytes(FilePath);
        var document = WorkspaceDocument.CreateEmpty();
        document.Version = 2;
        var application = document.CreateApplication(ComponentIds.Notes);
        for (var index = 0; index < 64; index++)
            application.State[$"key{index}"] = new string('x', 65_536);

        Assert.Throws<InvalidDataException>(() => store.Save(document));

        Assert.Equal(2, document.Version);
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("{ \"version\": 3, \"version\": 2, \"root\": null }")]
    [InlineData("{ \"version\": 3, \"root\": null, \"library\": [{ \"id\": \"app\", \"definitionId\": \"wonder.notes\", \"state\": { \"text\": \"first\", \"text\": \"second\" } }] }")]
    public void DuplicateJsonPropertiesCannotSilentlyReplaceMetadataOrState(string source)
    {
        WriteSource(source);

        Assert.Throws<InvalidDataException>(() => new WorkspaceStore(FilePath).Load());

        Assert.Equal(source, File.ReadAllText(FilePath));
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
