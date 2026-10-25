// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using Wonder.Core;
using Wonder.Infrastructure;

namespace Wonder.Game.Tests;

public sealed class InstanceStoreTests
{
    [Fact]
    public void Missing_catalog_is_empty()
    {
        using var f = new ImporterFixtures();
        Assert.Empty(new InstanceStore(Path.Combine(f.Root, "instances.json")).Load().Instances);
    }

    [Fact]
    public async Task Catalog_roundtrip_preserves_source_and_wonder_overrides()
    {
        using var f = new ImporterFixtures(); f.Version();
        var instance = Assert.Single((await new InstanceDiscovery().DiscoverAsync(f.DirectoryAt(".minecraft"))).Instances);
        instance = instance with { Overrides = new InstanceSettings { MaximumMemoryMb = 4096 } };
        var catalog = new InstanceCatalog { Instances = [instance], SelectedInstanceId = instance.Id, OfflineUsername = "PlayerOne" };
        var store = new InstanceStore(Path.Combine(f.Root, "catalog", "instances.json"));
        store.Save(catalog);
        var loaded = store.Load();
        Assert.Equal(instance.Id, loaded.SelectedInstanceId);
        Assert.Equal(4096, Assert.Single(loaded.Instances).Overrides.MaximumMemoryMb);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"version\":99,\"instances\":[]}")]
    [InlineData("{\"version\":1,\"version\":1,\"instances\":[]}")]
    public void Invalid_catalog_is_reported_without_modification(string json)
    {
        using var f = new ImporterFixtures(); var path = f.Write("instances.json", json);
        Assert.Throws<InvalidDataException>(() => new InstanceStore(path).Load());
        Assert.Equal(json, File.ReadAllText(path));
    }
}
