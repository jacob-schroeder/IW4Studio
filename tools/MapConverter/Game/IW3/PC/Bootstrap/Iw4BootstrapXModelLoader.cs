using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.Linker.Contracts;
using MapConverter.Bootstrap.XModel;

namespace MapConverter.Game.IW3.PC.Bootstrap;

internal sealed record Iw4BootstrapXModelGraph(
    IReadOnlyList<XModelAsset> Models,
    LinkAssetPool Providers,
    IReadOnlySet<uint> ReferencedImageFileIndices)
{
    internal IReadOnlySet<AssetKey> ModelKeys { get; } = Models
        .Select(AssetKey.FromDefinition)
        .ToHashSet();
}

/// <summary>
/// Supplies the bundled gameplay model and its dependencies to the map linker.
/// </summary>
internal static class Iw4BootstrapXModelLoader
{
    private const uint OutputLanguageMask = 1;

    internal static Iw4BootstrapXModelGraph Load()
    {
        XModelAsset model = MilTntBombMp.Create();
        MaterialAsset[] materials = model.Materials.OfType<MaterialAsset>().ToArray();
        GfxImageAsset[] images = materials.SelectMany(material => material.Textures)
            .Select(texture => texture.Image).OfType<GfxImageAsset>()
            .DistinctBy(AssetKey.FromDefinition).ToArray();
        BaseAsset[] assets =
        [
            model,
            .. model.Lods.Select(lod => lod.ModelSurfs).OfType<XModelSurfsAsset>(),
            .. materials,
            .. materials.Select(material => material.TechniqueSet).OfType<BaseAsset>(),
            .. images
        ];
        var providers = new LinkAssetPool(assets.Select(asset =>
        {
            if (asset is not GfxImageAsset image)
                return new LinkAssetProviderSource(asset).AsAuthoredDetached();

            int[] partLengths = GfxImageStreamData.ValidateProfileAndComputePartByteCounts(image.StreamData);
            var references = new ImageFileStreamLanguageReferences(OutputLanguageMask,
                image.StreamEntries.Select((entry, index) =>
                    new ImageFileStreamReference(entry, partLengths[index])));
            return new LinkAssetProviderSource(image, imageStreamReferences: [references]).AsAuthoredDetached();
        }));
        return new Iw4BootstrapXModelGraph(
            [new XModelAsset { Name = model.Name }],
            providers,
            images.SelectMany(image => image.StreamEntries)
                .Where(entry => !entry.IsEmpty)
                .Select(entry => entry.FileIndex).ToHashSet());
    }
}
