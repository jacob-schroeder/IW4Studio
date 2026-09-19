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

    private readonly string _settingsPath;

    public AppSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public bool LoadDebug()
    {
        JsonObject settings = ReadSettings();
        return settings["debug"] is JsonValue debugValue &&
            debugValue.TryGetValue(out bool enabled) &&
            enabled;
    }

    public ThemeMode LoadTheme()
    {
        JsonObject settings = ReadSettings();
        string? value = settings["Theme"] is JsonValue themeValue
            && themeValue.TryGetValue(out string? configuredTheme)
                ? configuredTheme
                : null;

        return Enum.TryParse(value, ignoreCase: true, out ThemeMode mode)
            ? mode
            : ThemeMode.Dark;
    }

    public void SaveTheme(ThemeMode mode)
    {
        JsonObject settings = ReadSettings();
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

        JsonObject settings = ReadSettings();
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
    {
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
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private JsonObject ReadSettings()
    {
        if (!File.Exists(_settingsPath))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject
                ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
        catch (IOException)
        {
            return new JsonObject();
        }
        catch (UnauthorizedAccessException)
        {
            return new JsonObject();
        }
    }
}
