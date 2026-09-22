using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using IW4.Studio.Desktop.Themes;

namespace IW4.Studio.Desktop.Persistence;

/// <summary>
/// Reads and updates application preferences while preserving unrelated settings.
/// </summary>
internal sealed class AppSettingsStore
{
    private const int MaximumRecentFastFiles = 3;

    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        WriteIndented = true
    };

    private readonly string? _settingsPath;

    private AppSettingsStore()
    {
    }

    public AppSettingsStore(string settingsPath)
        : this(settingsPath, legacySettingsPath: null)
    {
    }

    internal AppSettingsStore(string settingsPath, string? legacySettingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);

        if (!string.IsNullOrWhiteSpace(legacySettingsPath))
            TryMigrateLegacySettings(Path.GetFullPath(legacySettingsPath));
    }

    internal static AppSettingsStore CreateDefault()
    {
        string? settingsPath = AppSettingsPath.GetDefaultFilePath();
        return settingsPath is null
            ? new AppSettingsStore()
            : new AppSettingsStore(
                settingsPath,
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
    }

    public ThemeMode LoadTheme()
    {
        JsonObject settings = ReadSettings();
        string? value = settings["Theme"] is JsonValue themeValue
            && themeValue.TryGetValue(out string? configuredTheme)
                ? configuredTheme
                : null;

        return Enum.TryParse(value, ignoreCase: true, out ThemeMode mode)
            && Enum.IsDefined(mode)
            ? mode
            : ThemeMode.Dark;
    }

    public void SaveTheme(ThemeMode mode)
    {
        JsonObject settings = ReadSettingsForUpdate();
        settings["Theme"] = mode.ToString();

        WriteSettings(settings);
    }

    public IReadOnlyList<string> LoadRecentFastFiles()
    {
        JsonObject settings = ReadSettings();
        return ReadRecentFastFiles(settings);
    }

    public void SaveRecentFastFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".ff", StringComparison.OrdinalIgnoreCase))
            return;

        JsonObject settings = ReadSettingsForUpdate();
        var recentFiles = new List<string> { fullPath };
        recentFiles.AddRange(
            ReadRecentFastFiles(settings)
                .Where(existingPath => !string.Equals(
                    existingPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase)));

        settings["Recent"] = new JsonArray(
            recentFiles
                .Take(MaximumRecentFastFiles)
                .Select(path => (JsonNode?)JsonValue.Create(path))
                .ToArray());

        WriteSettings(settings);
    }

    private static IReadOnlyList<string> ReadRecentFastFiles(JsonObject settings)
    {
        if (settings["Recent"] is not JsonArray recentFiles)
            return [];

        var paths = new List<string>(MaximumRecentFastFiles);
        foreach (JsonNode? item in recentFiles)
        {
            if (item is not JsonValue value
                || !value.TryGetValue(out string? path)
                || string.IsNullOrWhiteSpace(path)
                || !string.Equals(Path.GetExtension(path), ".ff", StringComparison.OrdinalIgnoreCase)
                || paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            paths.Add(path);
            if (paths.Count == MaximumRecentFastFiles)
                break;
        }

        return paths;
    }

    private void WriteSettings(JsonObject settings)
        => WriteSettings(settings, overwrite: true);

    private void WriteSettings(JsonObject settings, bool overwrite)
    {
        if (_settingsPath is null)
            throw new IOException("The per-user settings directory is unavailable.");

        string? directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("The appsettings path has no parent directory.");

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(
                temporaryPath,
                settings.ToJsonString(WriterOptions) + Environment.NewLine);
            File.Move(temporaryPath, _settingsPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private JsonObject ReadSettings()
    {
        if (_settingsPath is null || !File.Exists(_settingsPath))
            return new JsonObject();

        return TryReadSettings(_settingsPath, out JsonObject settings)
            ? settings
            : new JsonObject();
    }

    private JsonObject ReadSettingsForUpdate()
    {
        if (_settingsPath is null)
            throw new IOException("The per-user settings directory is unavailable.");

        try
        {
            return JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject
                ?? new JsonObject();
        }
        catch (FileNotFoundException)
        {
            return new JsonObject();
        }
        catch (DirectoryNotFoundException)
        {
            return new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private void TryMigrateLegacySettings(string legacySettingsPath)
    {
        if (_settingsPath is null || PathsEqual(_settingsPath, legacySettingsPath))
            return;

        try
        {
            if (!IsFileConfirmedMissing(_settingsPath)
                || !TryReadSettings(legacySettingsPath, out JsonObject settings)
                || !ContainsStudioSetting(settings))
            {
                return;
            }

            WriteSettings(settings, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SecurityException)
        {
        }
    }

    private static bool TryReadSettings(string path, out JsonObject settings)
    {
        settings = new JsonObject();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject parsed)
                return false;

            settings = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    private static bool IsFileConfirmedMissing(string path)
    {
        try
        {
            using FileStream stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    private static bool ContainsStudioSetting(JsonObject settings) =>
        settings.ContainsKey("Theme")
        || settings.ContainsKey("Recent");

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
