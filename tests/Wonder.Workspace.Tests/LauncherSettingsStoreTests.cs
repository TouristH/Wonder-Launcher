// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;
using Wonder.Workspace;

namespace Wonder.Workspace.Tests;

public sealed class LauncherSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Wonder.Settings.Tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "settings.json");

    [Fact]
    public void MissingFileLoadsDefaultsWithoutCreatingTheDirectory()
    {
        var settings = new LauncherSettingsStore(FilePath).Load();

        Assert.Equal(1, settings.Version);
        Assert.Equal(LauncherTheme.Dark, settings.Theme);
        Assert.Equal(AccentColor.Mint, settings.Accent);
        Assert.Equal(22, settings.WorkspacePadding);
        Assert.False(settings.ShowAreaNumbers);
        Assert.Equal(450, settings.HoldDelayMilliseconds);
        Assert.Equal(320, settings.SidebarExtent);
        Assert.Equal(280, settings.SidebarCloseDelayMilliseconds);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void SettingsWithoutAreaNumbersLoadWithNumbersHiddenAndPreserveOriginalBytes()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, """
            {"version":1,"theme":2,"accent":1,"workspacePadding":30,"holdDelayMilliseconds":500,"sidebarExtent":400,"sidebarCloseDelayMilliseconds":300}
            """);
        var original = File.ReadAllBytes(FilePath);

        var restored = new LauncherSettingsStore(FilePath).Load();

        Assert.False(restored.ShowAreaNumbers);
        Assert.Equal(InstanceLayout.Grid, restored.InstanceLayout);
        Assert.Equal(LauncherTheme.Light, restored.Theme);
        Assert.Equal(30, restored.WorkspacePadding);
        Assert.Equal(original, File.ReadAllBytes(FilePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AreaNumberPreferenceSurvivesRestart(bool showNumbers)
    {
        var settings = new LauncherSettings { ShowAreaNumbers = showNumbers };
        new LauncherSettingsStore(FilePath).Save(settings);

        Assert.Equal(settings, new LauncherSettingsStore(FilePath).Load());
    }

    [Theory]
    [InlineData(LauncherTheme.System, AccentColor.Blue, 0, 250, 200, 100)]
    [InlineData(LauncherTheme.Light, AccentColor.Rose, 40, 1000, 600, 1000)]
    [InlineData(LauncherTheme.Dark, AccentColor.Amber, 17, 625, 410, 430)]
    public void SavedSettingsSurviveRestart(LauncherTheme theme, AccentColor accent, int padding, int holdDelay, int sidebarExtent, int closeDelay)
    {
        var settings = new LauncherSettings
        {
            Theme = theme,
            Accent = accent,
            WorkspacePadding = padding,
            HoldDelayMilliseconds = holdDelay,
            SidebarExtent = sidebarExtent,
            SidebarCloseDelayMilliseconds = closeDelay
        };

        new LauncherSettingsStore(FilePath).Save(settings);
        var restored = new LauncherSettingsStore(FilePath).Load();

        Assert.Equal(settings, restored);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void SavingReplacesPreviousSettingsWithoutLeavingTemporaryFiles()
    {
        var store = new LauncherSettingsStore(FilePath);
        store.Save(new LauncherSettings { Theme = LauncherTheme.Light, WorkspacePadding = 0 });

        store.Save(new LauncherSettings { Accent = AccentColor.Rose, HoldDelayMilliseconds = 750 });

        var restored = new LauncherSettingsStore(FilePath).Load();
        Assert.Equal(LauncherTheme.Dark, restored.Theme);
        Assert.Equal(AccentColor.Rose, restored.Accent);
        Assert.Equal(22, restored.WorkspacePadding);
        Assert.Equal(750, restored.HoldDelayMilliseconds);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("version", 0)]
    [InlineData("version", 2)]
    [InlineData("theme", -1)]
    [InlineData("theme", 3)]
    [InlineData("accent", -1)]
    [InlineData("accent", 4)]
    [InlineData("instanceLayout", -1)]
    [InlineData("instanceLayout", 2)]
    [InlineData("workspacePadding", -1)]
    [InlineData("workspacePadding", 41)]
    [InlineData("holdDelayMilliseconds", 249)]
    [InlineData("holdDelayMilliseconds", 1001)]
    [InlineData("sidebarExtent", 199)]
    [InlineData("sidebarExtent", 601)]
    [InlineData("sidebarCloseDelayMilliseconds", 99)]
    [InlineData("sidebarCloseDelayMilliseconds", 1001)]
    public void InvalidSettingCannotOverwriteExistingFile(string property, int value)
    {
        var store = new LauncherSettingsStore(FilePath);
        store.Save(new LauncherSettings { Accent = AccentColor.Blue });
        var original = File.ReadAllBytes(FilePath);
        var json = JsonNode.Parse(original)!;
        json[property] = value;
        var invalid = json.Deserialize<LauncherSettings>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Throws<InvalidDataException>(() => invalid.Validate());
        Assert.Throws<InvalidDataException>(() => store.Save(invalid));

        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("")]
    public void CorruptFileIsReportedWithoutChangingItsBytes(string source)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, source);
        var original = File.ReadAllBytes(FilePath);

        Assert.Throws<InvalidDataException>(() => new LauncherSettingsStore(FilePath).Load());

        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("version", "2")]
    [InlineData("theme", "42")]
    [InlineData("accent", "-1")]
    [InlineData("workspacePadding", "41")]
    [InlineData("holdDelayMilliseconds", "1001")]
    [InlineData("sidebarExtent", "199")]
    [InlineData("sidebarCloseDelayMilliseconds", "99")]
    [InlineData("workspacePadding", "\"22\"")]
    [InlineData("holdDelayMilliseconds", "450.5")]
    [InlineData("theme", "\"Dark\"")]
    [InlineData("accent", "null")]
    [InlineData("unknownOption", "true")]
    [InlineData("showAreaNumbers", "null")]
    [InlineData("showAreaNumbers", "1")]
    [InlineData("showAreaNumbers", "\"true\"")]
    public void InvalidJsonValueIsReportedWithoutChangingSource(string property, string value)
    {
        var store = new LauncherSettingsStore(FilePath);
        store.Save(new LauncherSettings());
        var json = JsonNode.Parse(File.ReadAllText(FilePath))!;
        json[property] = JsonNode.Parse(value);
        File.WriteAllText(FilePath, json.ToJsonString());
        var original = File.ReadAllBytes(FilePath);

        Assert.Throws<InvalidDataException>(() => store.Load());

        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("theme")]
    [InlineData("accent")]
    [InlineData("workspacePadding")]
    [InlineData("holdDelayMilliseconds")]
    [InlineData("sidebarExtent")]
    [InlineData("sidebarCloseDelayMilliseconds")]
    public void MissingRequiredFieldDoesNotSilentlyRestoreDefaults(string property)
    {
        var store = new LauncherSettingsStore(FilePath);
        store.Save(new LauncherSettings());
        var json = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        json.Remove(property);
        File.WriteAllText(FilePath, json.ToJsonString());
        var original = File.ReadAllBytes(FilePath);

        Assert.Throws<InvalidDataException>(() => store.Load());

        Assert.Equal(original, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void OversizedFileIsRejectedWithoutChangingIt()
    {
        var store = new LauncherSettingsStore(FilePath);
        store.Save(new LauncherSettings());
        File.AppendAllText(FilePath, new string(' ', 64 * 1024));
        var original = File.ReadAllBytes(FilePath);

        Assert.Throws<InvalidDataException>(() => store.Load());

        Assert.Equal(original, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void SaveFailureRemovesTemporaryFile()
    {
        Directory.CreateDirectory(FilePath);

        Assert.ThrowsAny<IOException>(() => new LauncherSettingsStore(FilePath).Save(new LauncherSettings()));

        Assert.True(Directory.Exists(FilePath));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void ExistingDirectoryIsReportedInsteadOfLoadingDefaults()
    {
        Directory.CreateDirectory(FilePath);

        Assert.ThrowsAny<UnauthorizedAccessException>(() => new LauncherSettingsStore(FilePath).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
