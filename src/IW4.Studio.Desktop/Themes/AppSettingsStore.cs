using System.Text.Json;
using System.Text.Json.Nodes;

namespace IW4.Studio.Desktop.Themes;

/// <summary>
/// Reads and updates the theme preference while preserving unrelated settings.
/// </summary>
internal sealed class AppSettingsStore
{
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
