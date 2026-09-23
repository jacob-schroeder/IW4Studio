using System.Text.Json;

namespace Iw4Radiant;

internal sealed class RadiantSettings
{
    public string? MaterialFolder { get; set; }
    public string? XModelFolder { get; set; }
    public List<MaterialFavoriteCollection> MaterialFavorites { get; set; } = [];
    public List<FoliagePainterPreset> FoliagePresets { get; set; } = [];

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
            settings.FoliagePresets ??= [];
            settings.FoliagePresets.RemoveAll(preset => preset is null);
            foreach (FoliagePainterPreset preset in settings.FoliagePresets)
            {
                preset.Name ??= "";
                preset.Models ??= [];
                preset.Models.RemoveAll(model => model is null);
                foreach (FoliagePresetModel model in preset.Models) model.Name ??= "";
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

internal sealed class FoliagePainterPreset
{
    public string Name { get; set; } = "";
    public float Radius { get; set; } = 64;
    public int Density { get; set; } = 1;
    public float Spacing { get; set; } = 32;
    public float MinimumScale { get; set; } = 0.8f;
    public float MaximumScale { get; set; } = 1.2f;
    public bool RandomYaw { get; set; } = true;
    public bool AlignToSurface { get; set; } = true;
    public List<FoliagePresetModel> Models { get; set; } = [];
}

internal sealed class FoliagePresetModel
{
    public string Name { get; set; } = "";
    public decimal? Weight { get; set; } = 1;
    public bool PlacementInitialized { get; set; }
    public bool UsesCustomPlacement { get; set; }
    public float MinimumScale { get; set; } = 0.8f;
    public float MaximumScale { get; set; } = 1.2f;
    public bool RandomYaw { get; set; } = true;
    public float FixedYaw { get; set; }
    public bool AlignToSurface { get; set; } = true;
    public float SurfaceOffset { get; set; }
}
