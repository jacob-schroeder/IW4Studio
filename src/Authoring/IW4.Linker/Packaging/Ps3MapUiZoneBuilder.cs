using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Localize;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;

namespace IW4.Linker.Packaging;

public static class Ps3MapUiZoneBuilder
{
    private const uint LanguageMask = 1;

    public static byte[] Build(string previewName, GfxImageAsset sourceImage,
        IReadOnlyList<LocalizeAsset> localizations)
    {
        if (sourceImage.MapType != MapType.TwoDimensional ||
            sourceImage.TextureSemantic != TextureSemantic.TwoDimensional ||
            sourceImage.PayloadByteCount <= 0 || sourceImage.PayloadByteCount != sourceImage.PayloadBytes.Count ||
            sourceImage.StreamData.Any(part => part.HasStreamingData))
        {
            throw new InvalidDataException("The map preview must be a resident 2D image.");
        }

        GfxImageAsset image = sourceImage.Name == previewName
            ? sourceImage : Ps3LoadZoneBuilder.CloneImage(sourceImage, previewName);
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
        BaseAsset[] assets = [image, material, .. localizations];
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

        return bytes.ToArray();
    }
}
