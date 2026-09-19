using System.Text.Json;

namespace Iw4Radiant;

internal sealed class RadiantSettings
{
    public string? MaterialFolder { get; set; }
    public string? XModelFolder { get; set; }

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    internal static RadiantSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<RadiantSettings>(File.ReadAllText(FilePath)) ?? new RadiantSettings();
        }
        catch { return new RadiantSettings(); }
    }

    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
