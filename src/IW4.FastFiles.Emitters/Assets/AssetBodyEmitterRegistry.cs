using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Closed production registration point for body-only emitter support.</summary>
public sealed class AssetBodyEmitterRegistry
{
    private readonly Dictionary<XAssetType, IXAssetBodyEmitter> _emitters = [];

    public static AssetBodyEmitterRegistry CreateDefault()
    {
        var registry = new AssetBodyEmitterRegistry();
        registry.Register(new RawFileBodyEmitter());
        registry.Register(new LocalizeBodyEmitter());
        registry.Register(new StringTableBodyEmitter());
        registry.Register(new StructuredDataBodyEmitter());
        registry.Register(new MenuFileBodyEmitter());
        registry.Register(new MenuBodyEmitter());
        registry.Register(new PhysPresetBodyEmitter());
        registry.Register(new SndCurveBodyEmitter());
        registry.Register(new LeaderboardBodyEmitter());
        registry.Register(new TracerBodyEmitter());
        registry.Register(new LightDefBodyEmitter());
        registry.Register(new ComWorldBodyEmitter());
        registry.Register(new GameWorldMpBodyEmitter());
        registry.Register(new FxWorldBodyEmitter());
        registry.Register(new GfxWorldBodyEmitter());
        registry.Register(new ClipMapBodyEmitter(XAssetType.ColMapSp));
        registry.Register(new ClipMapBodyEmitter(XAssetType.ColMapMp));
        registry.Register(new GameWorldSpBodyEmitter());
        registry.Register(new VehicleBodyEmitter());
        registry.Register(new WeaponBodyEmitter());
        registry.Register(new MapEntsBodyEmitter());
        registry.Register(new AddonMapEntsBodyEmitter());
        registry.Register(new MaterialShaderBodyEmitter(XAssetType.PixelShader));
        registry.Register(new MaterialShaderBodyEmitter(XAssetType.VertexShader));
        registry.Register(new LoadedSoundBodyEmitter());
        registry.Register(new GfxImageBodyEmitter());
        registry.Register(new FontBodyEmitter());
        registry.Register(new TechniqueSetBodyEmitter());
        registry.Register(new MaterialBodyEmitter());
        registry.Register(new PhysCollmapBodyEmitter());
        registry.Register(new XAnimBodyEmitter());
        registry.Register(new XModelSurfsBodyEmitter());
        registry.Register(new XModelBodyEmitter());
        registry.Register(new SoundAliasListBodyEmitter());
        registry.Register(new FxEffectDefBodyEmitter());
        registry.Register(new FxImpactTableBodyEmitter());
        return registry;
    }

    public void Register(IXAssetBodyEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        if (!_emitters.TryAdd(emitter.AssetType, emitter))
            throw new InvalidOperationException($"A body emitter is already registered for '{emitter.AssetType}'.");
    }

    public bool TryGet(XAssetType assetType, out IXAssetBodyEmitter? emitter) =>
        _emitters.TryGetValue(assetType, out emitter);

    public IXAssetBodyEmitter Require(XAssetType assetType) =>
        TryGet(assetType, out IXAssetBodyEmitter? emitter)
            ? emitter!
            : throw new KeyNotFoundException($"No body emitter is registered for '{assetType}'.");
}
