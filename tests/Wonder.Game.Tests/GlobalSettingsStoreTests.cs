// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class GlobalSettingsStoreTests
{
    [Fact]
    public void Global_settings_persist_once_and_legacy_catalogs_keep_inheritance()
    {
        using var fixture = new ImporterFixtures();
        var path = fixture.Write("instances.json", "{\"version\":1,\"instances\":[]}");
        var store = new InstanceStore(path);
        Assert.Equal(new InstanceSettings(), store.Load().GlobalSettings);
        var instance = InstanceSettingsTests.ValidInstance() with { Overrides = new() { MinimumMemoryMb = 8192 } };
        var catalog = new InstanceCatalog { Instances = [instance], GlobalSettings = new() { MaximumMemoryMb = 16384 } };
        store.Save(catalog);
        var restored = store.Load();
        Assert.Equal(16384, restored.GlobalSettings.MaximumMemoryMb);
        Assert.Equal(8192, Assert.Single(restored.Instances).Overrides.MinimumMemoryMb);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(json.RootElement.TryGetProperty("globalSettings", out _));
        Assert.False(json.RootElement.GetProperty("instances")[0].TryGetProperty("globalSettings", out _));
        catalog.Instances[0] = instance with { GlobalSettings = new() { MaximumMemoryMb = 1024 } };
        catalog.Validate(); // The catalog context takes precedence over stale snapshot context.
    }
}
