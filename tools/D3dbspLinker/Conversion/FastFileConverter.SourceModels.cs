using IW4.Game.Assets;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;

namespace D3dbspLinker.Conversion;

internal static partial class FastFileConverter
{
    private static (
        IReadOnlyList<XModelAsset> Models,
        IReadOnlyList<BaseAsset> Providers,
        IReadOnlySet<AssetKey> ExternalProviderKeys,
        int XModelSurfsCount,
        int PhysPresetReferenceCount) ResolveDiskModelGraph(ModelSourceCompiler compiler, IReadOnlyList<string> names)
    {
        XModelAsset[] models = names.Select(compiler.LoadModel).ToArray();
        return (models, [], new HashSet<AssetKey>(),
            models.SelectMany(model => model.Lods).Select(lod => lod.ModelSurfs).OfType<XModelSurfsAsset>()
                .Select(AssetKey.FromDefinition).Distinct().Count(),
            models.Select(model => model.PhysPreset).OfType<PhysPresetAsset>()
                .Select(AssetKey.FromDefinition).Distinct().Count());
    }
}
