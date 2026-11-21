using System.Text.Json;
using IW4.Formats.SourceFormat.Image;
using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Localize;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;

namespace D3dbspLinker;

internal static class MapUiCommand
{
    private const uint LanguageMask = 1;

    internal static int Build(
        string mapName, string previewImageName, string rawDirectory, string metadataPath, string output)
    {
        if (string.IsNullOrEmpty(mapName) || mapName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException($"Invalid map name '{mapName}'.", nameof(mapName));

        Metadata metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllBytes(metadataPath)) ??
            throw new InvalidDataException("UI metadata is empty.");
        foreach (string value in new[] { metadata.Title, metadata.Description, metadata.Factions })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
                throw new InvalidDataException("UI metadata requires a title, description, and faction profile.");
        }

        GfxImageAsset sourceImage = new ImageExchange().Link(rawDirectory, previewImageName);
        if (sourceImage.MapType != MapType.TwoDimensional ||
            sourceImage.TextureSemantic != TextureSemantic.TwoDimensional ||
            sourceImage.PayloadByteCount == 0)
        {
            throw new InvalidDataException("The map preview must be a resident 2D image.");
        }

        string previewName = $"preview_{mapName}";
        GfxImageAsset image = Ps3LoadZoneBuilder.CloneImage(sourceImage, previewName);
        var techniques = new MaterialTechniqueSetAsset { Name = ",2d" };
        MaterialAsset material = Ps3LoadZoneBuilder.CreateBackdropMaterial(
            previewName,
            0x8800180000000000,
            image,
            MaterialSamplerState.FilterLinear | MaterialSamplerState.ClampMask,
            TextureSemantic.TwoDimensional,
            0x18128812,
            techniques,
            (MaterialSortKey)34);
        BaseAsset[] assets =
        [
            image,
            material,
            new LocalizeAsset { Name = mapName, Value = metadata.Title },
            new LocalizeAsset { Name = $"{mapName}_desc", Value = metadata.Description },
            new LocalizeAsset { Name = $"{mapName}_factions", Value = metadata.Factions }
        ];
        var providers = new LinkAssetPool(assets.Append(techniques).Select(asset =>
            new LinkAssetProviderSource(asset).AsAuthoredDetached()));
        LinkRoot[] roots = assets.Select((asset, index) => new LinkRoot(
            $"map-ui:{index}",
            asset.SerializedAssetType,
            LinkRootIntent.Owned,
            AssetKey.FromDefinition(asset),
            asset.SerializedAssetName,
            opaqueHeader: null)).ToArray();

        ZoneLinkResult link = new ZoneLinker().Link(new ZoneLinkRequest(
            providers, roots, LanguageMask, LanguageMask, []));
        if (!link.Succeeded || link.DecodedBytes is not { } decodedBytes)
            throw new InvalidDataException("UI fastfile link failed: " + string.Join("; ", link.Errors));

        FastFilePackagingResult package = new FastFilePackager().PackageGreenfield(
            decodedBytes, link.LanguageMask, link.SelectedLanguageMask, link.ImageStreamLanguageTables);
        if (!package.Succeeded || package.Bytes is not { } bytes)
        {
            throw new InvalidDataException("UI fastfile packaging failed: " +
                string.Join("; ", package.Errors.Select(error => $"{error.Code}: {error.Message}")));
        }

        string outputPath = Path.GetFullPath(output);
        string outputDirectory = Path.GetDirectoryName(outputPath) ??
            throw new ArgumentException("The UI fastfile requires an output file path.", nameof(output));
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(outputPath, bytes.ToArray());
        return 0;
    }

    private sealed record Metadata(string Title, string Description, string Factions);
}
