using System.Security;
using System.Text.Json;

namespace Iw4Radiant;

internal sealed class RadiantSettings
{
    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _saveSync = new();
    private string? _filePath = RadiantSettingsPath.GetDefaultFilePath();

    public string? MaterialFolder { get; set; }
    public string? XModelFolder { get; set; }

    internal static RadiantSettings Load()
    {
        string? filePath = RadiantSettingsPath.GetDefaultFilePath();
        if (filePath is null)
            return new RadiantSettings { _filePath = null };

        TryMigrateLegacySettings(
            filePath,
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        try
        {
            RadiantSettings settings =
                JsonSerializer.Deserialize<RadiantSettings>(File.ReadAllText(filePath))
                ?? new RadiantSettings();
            settings._filePath = filePath;
            return settings;
        }
        catch
        {
            return new RadiantSettings { _filePath = filePath };
        }
    }

    internal string? Save()
    {
        if (_filePath is null)
            return "The per-user settings directory is unavailable.";

        try
        {
            lock (_saveSync)
                WriteSettings(_filePath, this, overwrite: true);
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private static void TryMigrateLegacySettings(
        string settingsPath,
        string legacySettingsPath)
    {
        if (PathsEqual(settingsPath, legacySettingsPath))
            return;

        try
        {
            if (!IsFileConfirmedMissing(settingsPath)
                || !TryReadLegacySettings(legacySettingsPath, out RadiantSettings legacySettings))
            {
                return;
            }

            WriteSettings(settingsPath, legacySettings, overwrite: false);
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
        catch (JsonException)
        {
        }
    }

    private static bool TryReadLegacySettings(
        string path,
        out RadiantSettings settings)
    {
        settings = new RadiantSettings();
        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || (!document.RootElement.TryGetProperty(nameof(MaterialFolder), out _)
                && !document.RootElement.TryGetProperty(nameof(XModelFolder), out _)))
        {
            return false;
        }

        RadiantSettings? parsed = JsonSerializer.Deserialize<RadiantSettings>(json);
        if (parsed is null)
            return false;

        settings = parsed;
        return true;
    }

    private static void WriteSettings(
        string settingsPath,
        RadiantSettings settings,
        bool overwrite)
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("The appsettings path has no parent directory.");

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, WriterOptions));
            File.Move(temporaryPath, settingsPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
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

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
