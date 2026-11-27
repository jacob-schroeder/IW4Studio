using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets;
using IW4.Game.Assets.XAnim;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;

namespace IW4.Studio.D3dbsp;

public sealed class FastFilePreparedSources
{
    internal FastFilePreparedSources(FastFileDependencyPreflightRequest request,
        MaterialSourceCompiler materials, ModelSourceCompiler models,
        MaterialSourceCompiler bootstrapMaterials, ModelSourceCompiler bootstrapModels,
        IReadOnlyDictionary<string, (MaterialSourceCompiler Materials, ModelSourceCompiler Models)> characters,
        BaseAsset[] emitterAssets, SoundFilePackager soundPackager,
        Ps3MapBootstrap bootstrap, IReadOnlyDictionary<string, XAnimPartsAsset> animations)
    {
        Request = request;
        Materials = materials;
        Models = models;
        BootstrapMaterials = bootstrapMaterials;
        BootstrapModels = bootstrapModels;
        Characters = characters;
        EmitterAssets = emitterAssets;
        SoundPackager = soundPackager;
        Bootstrap = bootstrap;
        Animations = animations;
    }

    internal FastFileDependencyPreflightRequest Request { get; }
    internal MaterialSourceCompiler Materials { get; }
    internal ModelSourceCompiler Models { get; }
    internal MaterialSourceCompiler BootstrapMaterials { get; }
    internal ModelSourceCompiler BootstrapModels { get; }
    internal IReadOnlyDictionary<string, (MaterialSourceCompiler Materials, ModelSourceCompiler Models)> Characters { get; }
    internal BaseAsset[] EmitterAssets { get; }
    internal SoundFilePackager SoundPackager { get; }
    internal Ps3MapBootstrap Bootstrap { get; }
    internal IReadOnlyDictionary<string, XAnimPartsAsset> Animations { get; }
}
