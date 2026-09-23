using System.Text.Json;

namespace Iw4Radiant;

internal sealed class RadiantSettings
{
    public string? MaterialFolder { get; set; }
    public string? XModelFolder { get; set; }
    public List<MaterialFavoriteCollection> MaterialFavorites { get; set; } = [];

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IW4Studio", "Iw4Radiant", "appsettings.json");
    private static string PreviousFilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    internal static RadiantSettings Load()
    {
        try
        {
            string path = File.Exists(FilePath) ? FilePath : PreviousFilePath;
            var settings = JsonSerializer.Deserialize<RadiantSettings>(File.ReadAllText(path)) ?? new RadiantSettings();
            settings.MaterialFavorites ??= [];
            settings.MaterialFavorites.RemoveAll(collection => collection is null);
            foreach (MaterialFavoriteCollection collection in settings.MaterialFavorites)
            {
                collection.Name ??= "";
                collection.Materials ??= [];
                collection.Materials.RemoveAll(material => material is null);
            }
            return settings;
        }
        catch { return new RadiantSettings(); }
    }

    internal bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch { return false; }
    }
}

internal sealed class MaterialFavoriteCollection
{
    public string Name { get; set; } = "";
    public List<string> Materials { get; set; } = [];
}
