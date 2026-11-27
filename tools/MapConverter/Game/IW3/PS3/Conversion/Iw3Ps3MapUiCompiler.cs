using IW4.Linker.Packaging;
using System.Text;
using IW4.Game.Assets.Image;
using MapConverter.Game.IW3.PS3.Extraction;
using MapConverter.Game.IW3.PS3.Images;
using MapConverter.Game.IW3.PS3.Rules;

namespace MapConverter.Game.IW3.PS3.Conversion;

internal static class Iw3Ps3MapUiCompiler
{
    internal static byte[] Build(string mapName, string sourceDirectory, string stringDirectory,
        FactionProfile factions, CancellationToken token)
    {
        var row = Iw3Ps3FastFileCatalog.ReadMapUiRow(
            Path.Combine(sourceDirectory, "code_post_gfx_mp.ff"), mapName, token);
        string ReadText(string key)
        {
            string path = Path.Combine(stringDirectory, "english", "localizedstrings", MapUiRules.StringFile(key));
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Map UI conversion requires COD4 English StringEd source. Place it under the source directory's " +
                    "english/localizedstrings folder, or select its raw directory with --source-library.", path);
            return MapUiRules.EnglishText(key, File.ReadAllText(path, Encoding.Latin1));
        }
        var localizations = MapUiRules.Localizations(mapName, ReadText(row.TitleKey), ReadText(row.DescriptionKey), factions);
        string uiPath = Path.Combine(sourceDirectory, "ui_mp.ff");
        int root = Iw3Ps3FastFileCatalog.FindInlineUiImageRoot(uiPath, row.PreviewMaterial, 4, token) ??
            throw new InvalidDataException($"COD4 UI fastfile has no preview material '{row.PreviewMaterial}'.");
        var images = new Iw3Ps3ImageLibrary("");
        images.AddFastFile(uiPath, token);
        GfxImageAsset image = images.ResolvePreviewAt(uiPath, root, mapName);
        return Ps3MapUiZoneBuilder.Build(MapUiRules.PreviewName(mapName), image, localizations);
    }
}
