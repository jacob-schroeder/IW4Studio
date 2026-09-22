using System.Text.Json.Nodes;
using IW4.Studio.Desktop.Persistence;
using IW4.Studio.Desktop.Themes;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void Missing_settings_use_safe_defaults()
    {
        using var fixture = new SettingsFixture();
        var store = new AppSettingsStore(fixture.SettingsPath);

        Assert.False(store.LoadDebug());
        Assert.Equal(ThemeMode.Dark, store.LoadTheme());
        Assert.Empty(store.LoadRecentFastFiles());
    }

    [Fact]
    public void Inaccessible_settings_use_safe_defaults()
    {
        using var fixture = new SettingsFixture();
        Directory.CreateDirectory(fixture.SettingsPath);
        var store = new AppSettingsStore(fixture.SettingsPath);

        Assert.False(store.LoadDebug());
        Assert.Equal(ThemeMode.Dark, store.LoadTheme());
        Assert.Empty(store.LoadRecentFastFiles());
    }

    [Fact]
    public void Malformed_and_partially_valid_settings_keep_field_defaults()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(fixture.SettingsPath, "not json");
        var malformed = new AppSettingsStore(fixture.SettingsPath);

        Assert.False(malformed.LoadDebug());
        Assert.Equal(ThemeMode.Dark, malformed.LoadTheme());
        Assert.Empty(malformed.LoadRecentFastFiles());

        File.WriteAllText(
            fixture.SettingsPath,
            """
            {
              "debug": "not-a-boolean",
              "Theme": "42",
              "Recent": ["first.ff", "FIRST.ff", 7, "skip.txt", "second.ff", "third.ff", "fourth.ff"]
            }
            """);
        var partial = new AppSettingsStore(fixture.SettingsPath);

        Assert.False(partial.LoadDebug());
        Assert.Equal(ThemeMode.Dark, partial.LoadTheme());
        Assert.Equal(
            ["first.ff", "second.ff", "third.ff"],
            partial.LoadRecentFastFiles());
    }

    [Fact]
    public void Existing_destination_wins_over_legacy_even_when_malformed()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(fixture.SettingsPath, "not json");
        File.WriteAllText(fixture.LegacyPath, """{"Theme":"Light"}""");

        var store = new AppSettingsStore(
            fixture.SettingsPath,
            fixture.LegacyPath);

        Assert.Equal(ThemeMode.Dark, store.LoadTheme());
        Assert.Equal("not json", File.ReadAllText(fixture.SettingsPath));
        Assert.True(File.Exists(fixture.LegacyPath));
    }

    [Fact]
    public void Valid_legacy_settings_seed_an_absent_destination_once()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(
            fixture.LegacyPath,
            """
            {
              "debug": true,
              "Theme": "Light",
              "Recent": ["legacy.ff"],
              "Extension": { "Enabled": true }
            }
            """);

        var store = new AppSettingsStore(
            fixture.SettingsPath,
            fixture.LegacyPath);

        Assert.True(store.LoadDebug());
        Assert.Equal(ThemeMode.Light, store.LoadTheme());
        Assert.Equal(["legacy.ff"], store.LoadRecentFastFiles());
        Assert.True(File.Exists(fixture.LegacyPath));
        Assert.True(File.Exists(fixture.SettingsPath));
        Assert.True(
            JsonNode.Parse(File.ReadAllText(fixture.SettingsPath))?
                ["Extension"]?["Enabled"]?.GetValue<bool>());

        File.WriteAllText(fixture.LegacyPath, """{"Theme":"Dark"}""");
        var reloaded = new AppSettingsStore(
            fixture.SettingsPath,
            fixture.LegacyPath);
        Assert.Equal(ThemeMode.Light, reloaded.LoadTheme());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"MaterialFolder\":\"/legacy/materials\"}")]
    public void Invalid_or_other_application_legacy_settings_do_not_seed(string json)
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(fixture.LegacyPath, json);

        var store = new AppSettingsStore(
            fixture.SettingsPath,
            fixture.LegacyPath);

        Assert.False(File.Exists(fixture.SettingsPath));
        Assert.False(store.LoadDebug());
        Assert.Equal(ThemeMode.Dark, store.LoadTheme());
        Assert.True(File.Exists(fixture.LegacyPath));
    }

    [Fact]
    public void Failed_legacy_publication_is_non_blocking_and_retains_legacy()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(fixture.LegacyPath, """{"Theme":"Light"}""");
        string blockedParent = Path.Combine(fixture.DirectoryPath, "blocked-parent");
        File.WriteAllText(blockedParent, "not a directory");
        string destination = Path.Combine(blockedParent, "appsettings.json");

        var store = new AppSettingsStore(destination, fixture.LegacyPath);

        Assert.Equal(ThemeMode.Dark, store.LoadTheme());
        Assert.False(File.Exists(destination));
        Assert.True(File.Exists(fixture.LegacyPath));
    }

    [Fact]
    public void Theme_save_preserves_unrelated_keys_and_cleans_temporary_file()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(
            fixture.SettingsPath,
            """
            {
              "Theme": "Dark",
              "Extension": { "Enabled": true }
            }
            """);
        var store = new AppSettingsStore(fixture.SettingsPath);

        store.SaveTheme(ThemeMode.Light);

        JsonNode settings = JsonNode.Parse(File.ReadAllText(fixture.SettingsPath))!;
        Assert.Equal("Light", settings["Theme"]?.GetValue<string>());
        Assert.True(settings["Extension"]?["Enabled"]?.GetValue<bool>());
        Assert.Empty(Directory.EnumerateFiles(
            fixture.DirectoryPath,
            ".appsettings.json.*.tmp"));
    }

    [Fact]
    public void Recent_save_normalizes_deduplicates_filters_and_caps_entries()
    {
        using var fixture = new SettingsFixture();
        var store = new AppSettingsStore(fixture.SettingsPath);
        string first = Path.Combine(fixture.DirectoryPath, "first.ff");
        string normalizedFirst = Path.Combine(
            fixture.DirectoryPath,
            "nested",
            "..",
            "first.ff");
        string second = Path.Combine(fixture.DirectoryPath, "second.ff");
        string third = Path.Combine(fixture.DirectoryPath, "third.ff");
        string fourth = Path.Combine(fixture.DirectoryPath, "fourth.ff");

        store.SaveRecentFastFile(normalizedFirst);
        store.SaveRecentFastFile(second);
        store.SaveRecentFastFile(third);
        store.SaveRecentFastFile(fourth);
        store.SaveRecentFastFile(Path.Combine(fixture.DirectoryPath, "skip.txt"));
        store.SaveRecentFastFile(first.ToUpperInvariant());

        Assert.Equal(
            [Path.GetFullPath(first.ToUpperInvariant()), fourth, third],
            store.LoadRecentFastFiles());
    }

    [Fact]
    public void Persistence_failure_is_reported_without_leaving_a_temporary_file()
    {
        using var fixture = new SettingsFixture();
        Directory.CreateDirectory(fixture.SettingsPath);
        var store = new AppSettingsStore(fixture.SettingsPath);

        Exception? exception = Record.Exception(() => store.SaveTheme(ThemeMode.Light));

        Assert.True(
            exception is IOException or UnauthorizedAccessException,
            $"Unexpected exception: {exception}");
        Assert.Empty(Directory.EnumerateFiles(
            fixture.DirectoryPath,
            ".appsettings.json.*.tmp"));
    }

    [Fact]
    public void Save_does_not_replace_an_existing_destination_that_cannot_be_read()
    {
        using var fixture = new SettingsFixture();
        const string original = """
            {
              "Theme": "Dark",
              "Extension": { "Enabled": true }
            }
            """;
        File.WriteAllText(fixture.SettingsPath, original);
        var store = new AppSettingsStore(fixture.SettingsPath);

        Exception? exception;
        using (FileStream destinationLock = File.Open(
            fixture.SettingsPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            exception = Record.Exception(() => store.SaveTheme(ThemeMode.Light));
        }

        Assert.True(
            exception is IOException or UnauthorizedAccessException,
            $"Unexpected exception: {exception}");
        Assert.Equal(original, File.ReadAllText(fixture.SettingsPath));
    }

    private sealed class SettingsFixture : IDisposable
    {
        public SettingsFixture()
        {
            DirectoryPath = Directory.CreateTempSubdirectory(
                "IW4.Studio.Tests.AppSettings.").FullName;
            SettingsPath = Path.Combine(DirectoryPath, "appsettings.json");
            LegacyPath = Path.Combine(DirectoryPath, "legacy-appsettings.json");
        }

        public string DirectoryPath { get; }
        public string SettingsPath { get; }
        public string LegacyPath { get; }

        public void Dispose() =>
            Directory.Delete(DirectoryPath, recursive: true);
    }
}
