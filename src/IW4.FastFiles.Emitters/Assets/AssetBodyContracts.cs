using IW4.FastFiles.Zone;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Assets.Menu;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Detached input understood by an asset body emitter.</summary>
public interface IXAssetBuildData
{
    XAssetType AssetType { get; }
}

public interface IRawFileBuildData : IXAssetBuildData
{
    string OriginalName { get; }
    bool HasBuffer { get; }
    int CompressedLength { get; }
    int UncompressedLength { get; }
    /// <summary>
    /// True only when this value carries the exact compressed bytes of an
    /// untouched imported RawFile. New or edited compressed payloads must
    /// leave this false so the emitter can prove their zlib stream and
    /// declared logical length before producing output.
    /// </summary>
    bool PreserveOpaqueCompressedPayload => false;
    byte[] GetSerializedPayloadCopy();
}

/// <summary>
/// Detached ordered Menu registrations. Each nested link retains whether its
/// source pointer owned an inline/insert definition or aliased a previously
/// materialized Menu. Owned definitions retain the complete detached recursive
/// Menu graph through <see cref="NestedXAssetBuildLink.IncomingDefinition"/>.
/// </summary>
public interface IMenuFileBuildData : IXAssetBuildData
{
    string? Name { get; }
    IReadOnlyList<NestedXAssetBuildLink> MenuLinks { get; }
}

public interface IMenuBuildData : IXAssetBuildData
{
    bool IsComplete { get; }
    MenuDefAsset Definition { get; }
}

/// <summary>Typed GameMapSp hand-off.  The eventual emitter consumes detached
/// authored path/vehicle/glass data only; renderer and path-search runtime
/// caches are represented separately so they cannot be mistaken for source
/// payloads while this large graph is completed.</summary>
public interface IGameWorldSpBuildData : IXAssetBuildData
{
    string? Name { get; }
    PathData Path { get; }
    VehicleTrack VehicleTrack { get; }
    GGlassData? GlassData { get; }
}

/// <summary>Detached FxMap source state. The glass-system arrays retain their
/// serialized stream placement (LARGE or RUNTIME); nested asset
/// links are represented by symbolic identities rather than source pointers.</summary>
public interface IFxWorldBuildData : IXAssetBuildData
{
    string? Name { get; }
    FxGlassSystem GlassSystem { get; }
    IReadOnlyList<FxGlassDefReferenceBuildData> DefinitionReferences { get; }
}

/// <summary>Detached ColMap root.  The serialized asset type is deliberately
/// retained here because ColMapSp and ColMapMp share a runtime family but are
/// distinct rows in the source asset table.</summary>
public interface IClipMapBuildData : IXAssetBuildData
{
    XAssetType SerializedType { get; }
    ClipMapAsset Definition { get; }
    ClipMapReferenceBuildData References => ClipMapReferenceBuildData.Empty;
    ClipMapLinkerProvenance LinkerProvenance =>
        ClipMapLinkerProvenance.Empty;
}

/// <summary>
/// Imported non-semantic ColMap source forms needed for an exact no-op
/// relink. Semantic records remain in <see cref="ClipMapAsset"/>; these
/// values retain whether native source reused earlier storage or carried
/// inline child payloads.
/// </summary>
public sealed class ClipMapLinkerProvenance
{
    public static ClipMapLinkerProvenance Empty { get; } = new();

    private readonly IReadOnlyList<int?> _leafBrushNodeBrushesPointerRaws;
    private readonly IReadOnlyList<int?> _partitionBordersPointerRaws;

    public ClipMapLinkerProvenance(
        int? importedPlanesPackedRaw = null,
        int? importedIsInUse = null,
        IEnumerable<int?>? leafBrushNodeBrushesPointerRaws = null,
        IEnumerable<int?>? partitionBordersPointerRaws = null)
    {
        ImportedPlanesPackedRaw = importedPlanesPackedRaw;
        ImportedIsInUse = importedIsInUse;
        _leafBrushNodeBrushesPointerRaws = Array.AsReadOnly(
            (leafBrushNodeBrushesPointerRaws ?? []).ToArray());
        _partitionBordersPointerRaws = Array.AsReadOnly(
            (partitionBordersPointerRaws ?? []).ToArray());
    }

    public int? ImportedPlanesPackedRaw { get; }

    /// <summary>The root +0x04 value before DB_AddXAsset changes the runtime
    /// pool copy to one.</summary>
    public int? ImportedIsInUse { get; }

    /// <summary>One raw source pointer per leaf-brush node. Positive-count
    /// entries retain either inline (-1) or a packed offset; other entries
    /// are null.</summary>
    public IReadOnlyList<int?> LeafBrushNodeBrushesPointerRaws =>
        _leafBrushNodeBrushesPointerRaws;

    /// <summary>One imported packed border-table alias per collision
    /// partition. Native files may retain a packed value even when the
    /// partition's border count is zero.</summary>
    public IReadOnlyList<int?> PartitionBordersPointerRaws =>
        _partitionBordersPointerRaws;
}

/// <summary>
/// Detached nested links reachable from a ColMap definition. The collision
/// records retain scalar/geometry values in <see cref="IClipMapBuildData.Definition"/>.
/// Parallel link provenance retains original pointer forms and detached
/// incoming bodies without retaining loaded BaseAssets or pool addresses.
/// </summary>
public sealed class ClipMapReferenceBuildData
{
    public static ClipMapReferenceBuildData Empty { get; } = new(
        [],
        [Array.Empty<ClipMapDynEntityReferenceBuildData>(), Array.Empty<ClipMapDynEntityReferenceBuildData>()],
        null);
    public ClipMapReferenceBuildData(
        IEnumerable<SymbolicXAssetReference?> staticModels,
        IEnumerable<IReadOnlyList<ClipMapDynEntityReferenceBuildData>> dynamicEntities,
        SymbolicXAssetReference? mapEnts,
        IEnumerable<NestedXAssetBuildLink?>? staticModelLinks = null,
        NestedXAssetBuildLink? mapEntsLink = null)
    {
        ArgumentNullException.ThrowIfNull(staticModels);
        ArgumentNullException.ThrowIfNull(dynamicEntities);
        _staticModels = staticModels.ToArray();
        _dynamicEntities = dynamicEntities.Select(list => (IReadOnlyList<ClipMapDynEntityReferenceBuildData>)Array.AsReadOnly(list.Select(value => value.Copy()).ToArray())).ToArray();
        MapEnts = mapEnts;
        _staticModelLinks = staticModelLinks?.ToArray() ?? [];
        MapEntsLink = mapEntsLink;
    }

    private readonly SymbolicXAssetReference?[] _staticModels;
    private readonly IReadOnlyList<ClipMapDynEntityReferenceBuildData>[] _dynamicEntities;
    private readonly NestedXAssetBuildLink?[] _staticModelLinks;
    public IReadOnlyList<SymbolicXAssetReference?> StaticModels => Array.AsReadOnly(_staticModels);
    public IReadOnlyList<IReadOnlyList<ClipMapDynEntityReferenceBuildData>> DynamicEntities => Array.AsReadOnly(_dynamicEntities.Select(list => (IReadOnlyList<ClipMapDynEntityReferenceBuildData>)Array.AsReadOnly(list.Select(value => value.Copy()).ToArray())).ToArray());
    public SymbolicXAssetReference? MapEnts { get; }
    public IReadOnlyList<NestedXAssetBuildLink?> StaticModelLinks =>
        Array.AsReadOnly(_staticModelLinks);
    public NestedXAssetBuildLink? MapEntsLink { get; }
}

public sealed record ClipMapDynEntityReferenceBuildData(
    SymbolicXAssetReference? XModel,
    SymbolicXAssetReference? DestroyFx,
    SymbolicXAssetReference? PhysPreset,
    NestedXAssetBuildLink? XModelLink = null,
    NestedXAssetBuildLink? DestroyFxLink = null,
    NestedXAssetBuildLink? PhysPresetLink = null)
{
    internal ClipMapDynEntityReferenceBuildData Copy() =>
        new(
            XModel,
            DestroyFx,
            PhysPreset,
            XModelLink,
            DestroyFxLink,
            PhysPresetLink);
}

/// <summary>Detached GfxMap root.  This is kept separate from GfxImage sidecar
/// data because a world map owns renderer-independent source buffers while
/// image streams remain transactional sidecars.</summary>
public interface IGfxWorldBuildData : IXAssetBuildData
{
    GfxWorldAsset Definition { get; }
    GfxWorldReferenceBuildData References => GfxWorldReferenceBuildData.Empty;
}

/// <summary>
/// Detached identities and, when the source pointer materialized one inline,
/// detached nested definitions reachable from the GfxWorld source graph.
/// The definition collections intentionally parallel the symbolic collections:
/// a null definition means the slot is an external/alias reference, while a
/// non-null definition owns the inline child body at that source position.
/// </summary>
public sealed class GfxWorldReferenceBuildData
{
    public static GfxWorldReferenceBuildData Empty { get; } = new();

    public IReadOnlyList<SymbolicXAssetReference?> SkyImages { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> ReflectionProbeImages { get; init; } = [];
    public IReadOnlyList<GfxLightmapReferenceBuildData> Lightmaps { get; init; } = [];
    public SymbolicXAssetReference? LightmapOverridePrimary { get; init; }
    public SymbolicXAssetReference? LightmapOverrideSecondary { get; init; }
    public IReadOnlyList<SymbolicXAssetReference?> MaterialMemory { get; init; } = [];
    public SymbolicXAssetReference? SunSpriteMaterial { get; init; }
    public SymbolicXAssetReference? SunFlareMaterial { get; init; }
    public SymbolicXAssetReference? OutdoorImage { get; init; }
    public IReadOnlyList<SymbolicXAssetReference?> SurfaceMaterials { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> StaticModelDrawInsts { get; init; } = [];

    public IReadOnlyList<IXAssetBuildData?> SkyImageDefinitions { get; init; } = [];
    public IReadOnlyList<IXAssetBuildData?> ReflectionProbeImageDefinitions { get; init; } = [];
    public IReadOnlyList<GfxLightmapDefinitionBuildData> LightmapDefinitions { get; init; } = [];
    public IXAssetBuildData? LightmapOverridePrimaryDefinition { get; init; }
    public IXAssetBuildData? LightmapOverrideSecondaryDefinition { get; init; }
    public IReadOnlyList<IXAssetBuildData?> MaterialMemoryDefinitions { get; init; } = [];
    public IXAssetBuildData? SunSpriteMaterialDefinition { get; init; }
    public IXAssetBuildData? SunFlareMaterialDefinition { get; init; }
    public IXAssetBuildData? OutdoorImageDefinition { get; init; }
    public IReadOnlyList<IXAssetBuildData?> SurfaceMaterialDefinitions { get; init; } = [];
    public IReadOnlyList<IXAssetBuildData?> StaticModelDrawInstDefinitions { get; init; } = [];

    // Exact imported pointer provenance. These collections parallel the
    // symbolic slots when present; empty collections retain compatibility
    // with greenfield/external-only callers.
    public IReadOnlyList<NestedXAssetBuildLink?> SkyImageLinks { get; init; } = [];
    public IReadOnlyList<NestedXAssetBuildLink?> ReflectionProbeImageLinks { get; init; } = [];
    public IReadOnlyList<GfxLightmapLinkBuildData> LightmapLinks { get; init; } = [];
    public NestedXAssetBuildLink? LightmapOverridePrimaryLink { get; init; }
    public NestedXAssetBuildLink? LightmapOverrideSecondaryLink { get; init; }
    public IReadOnlyList<NestedXAssetBuildLink?> MaterialMemoryLinks { get; init; } = [];
    public NestedXAssetBuildLink? SunSpriteMaterialLink { get; init; }
    public NestedXAssetBuildLink? SunFlareMaterialLink { get; init; }
    public NestedXAssetBuildLink? OutdoorImageLink { get; init; }
    public IReadOnlyList<NestedXAssetBuildLink?> SurfaceMaterialLinks { get; init; } = [];
    public IReadOnlyList<NestedXAssetBuildLink?> StaticModelDrawInstLinks { get; init; } = [];

    // GfxAabbTree::smodelIndexes is the one world-owned direct array whose
    // native loader accepts packed pointers into an earlier inline slice.
    // The nested shape parallels Definition.CellTrees/AabbTrees.
    public IReadOnlyList<IReadOnlyList<GfxAabbTreeIndexPointerBuildData>>
        AabbTreeSModelIndexPointers { get; init; } = [];
}

public sealed record GfxLightmapReferenceBuildData(
    SymbolicXAssetReference? Primary,
    SymbolicXAssetReference? Secondary);

public sealed record GfxLightmapDefinitionBuildData(
    IXAssetBuildData? Primary,
    IXAssetBuildData? Secondary);

public sealed record GfxLightmapLinkBuildData(
    NestedXAssetBuildLink? Primary,
    NestedXAssetBuildLink? Secondary);

public enum GfxDirectPointerSourceForm
{
    Null,
    Inline,
    Insert,
    PackedAlias
}

public sealed record GfxAabbTreeIndexPointerBuildData(
    GfxDirectPointerSourceForm SourceForm,
    int? ImportedPackedRaw = null);

public sealed record FxGlassDefReferenceBuildData(
    SymbolicXAssetReference? Material,
    SymbolicXAssetReference? ShatteredMaterial,
    SymbolicXAssetReference? PhysPreset,
    [property: System.Text.Json.Serialization.JsonIgnore]
    NestedXAssetBuildLink? MaterialLink = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    NestedXAssetBuildLink? ShatteredMaterialLink = null,
    [property: System.Text.Json.Serialization.JsonIgnore]
    NestedXAssetBuildLink? PhysPresetLink = null);

public interface ILocalizeBuildData : IXAssetBuildData
{
    string? Name { get; }
    string? Value { get; }
}

public interface IStringTableCellBuildData
{
    string? Value { get; }
    int Hash { get; }
}

public interface IStringTableBuildData : IXAssetBuildData
{
    string? Name { get; }
    int RowCount { get; }
    int ColumnCount { get; }
    IReadOnlyList<IStringTableCellBuildData> Cells { get; }
}

public interface IPhysPresetBuildData : IXAssetBuildData
{
    string? Name { get; }
    int Type { get; }
    float Mass { get; }
    float Bounce { get; }
    float Friction { get; }
    float BulletForceScale { get; }
    float ExplosiveForceScale { get; }
    string? SndAliasPrefix { get; }
    float PiecesSpreadFraction { get; }
    float PiecesUpwardVelocity { get; }
    byte TempDefaultToCylinder { get; }
    byte PerSurfaceSndAlias { get; }
    ushort Pad2A { get; }
}

public readonly record struct SndCurveKnotBuildData(float X, float Y);

public interface ISndCurveBuildData : IXAssetBuildData
{
    string? Filename { get; }
    ushort KnotCount { get; }
    ushort Padding { get; }
    IReadOnlyList<SndCurveKnotBuildData> Knots { get; }
}

public interface ILeaderboardColumnBuildData
{
    string? Name { get; }
    int Id { get; }
    int PropertyId { get; }
    byte HiddenRaw { get; }
    byte[] GetPad0DTo0FCopy();
    string? StatName { get; }
    int Type { get; }
    int Precision { get; }
    int Aggregation { get; }
}

public interface ILeaderboardBuildData : IXAssetBuildData
{
    string? Name { get; }
    int Id { get; }
    int XpColumnId { get; }
    int PrestigeColumnId { get; }
    IReadOnlyList<ILeaderboardColumnBuildData> Columns { get; }
}

/// <summary>Symbolic identity for a nested XAsset. The original wire name is
/// retained, including a comma prefix when the source names an external
/// reference provider rather than an owned body.</summary>
public sealed record SymbolicXAssetReference(XAssetType AssetType, string OriginalSerializedName)
{
    public bool IsExternalReference =>
        OriginalSerializedName.StartsWith(",", StringComparison.Ordinal);
}

/// <summary>
/// Address-free source form for one nested XAsset pointer. Packed aliases are
/// resolved through a persistent owner cell registered by identity; no
/// imported block offset is retained.
/// </summary>
public enum NestedXAssetPointerSourceForm
{
    Inline,
    Insert,
    PackedAlias
}

/// <summary>
/// Detached nested XAsset materialization. Inline and insert forms carry the
/// exact incoming definition consumed from the source even when DB_AddXAsset
/// selected an older canonical provider. Imported owner-cell provenance is
/// retained only to authorize exact semantic ownership transfers; emitters
/// never replay it as a destination pointer.
/// </summary>
public sealed record NestedXAssetBuildLink(
    SymbolicXAssetReference Reference,
    NestedXAssetPointerSourceForm SourceForm,
    IXAssetBuildData? IncomingDefinition = null,
    int? ImportedPackedRaw = null,
    int? ImportedOwnerCellRaw = null)
{
    public string AliasKey =>
        AssetBodyEmitterHelpers.XAssetAliasKey(
            Reference.AssetType,
            Reference.OriginalSerializedName);
}

public readonly record struct TracerColorBuildData(float Red, float Green, float Blue, float Alpha);

public interface ITracerBuildData : IXAssetBuildData
{
    string? Name { get; }
    SymbolicXAssetReference? MaterialReference { get; }
    uint DrawInterval { get; }
    float Speed { get; }
    float BeamLength { get; }
    float BeamWidth { get; }
    float ScrewRadius { get; }
    float ScrewDistance { get; }
    IReadOnlyList<TracerColorBuildData> Colors { get; }
}

public interface ILightDefBuildData : IXAssetBuildData
{
    string? Name { get; }
    SymbolicXAssetReference? ImageReference { get; }
    NestedXAssetBuildLink? ImageLink => null;
    byte SamplerState { get; }
    byte[] GetPad09To0BCopy();
    uint LmapLookupStart { get; }
}

/// <summary>One serialized 0x44-byte ComMap primary light. The definition
/// name is an XString, not a runtime light/shadow handle.</summary>
public readonly record struct ComPrimaryLightBuildData(
    byte Type,
    byte CanUseShadowMap,
    byte Exponent,
    byte Unused,
    Float3BuildData Color,
    Float3BuildData Direction,
    Float3BuildData Origin,
    float Radius,
    float CosHalfFovOuter,
    float CosHalfFovInner,
    float CosHalfFovExpanded,
    float RotationLimit,
    float TranslationLimit,
    string? DefName);

public interface IComWorldBuildData : IXAssetBuildData
{
    string? Name { get; }
    int IsInUse { get; }
    IReadOnlyList<ComPrimaryLightBuildData> PrimaryLights { get; }
}

public readonly record struct GGlassPieceBuildData(
    ushort DamageTaken,
    ushort CollapseTime,
    int LastStateChangeTime,
    ushort PackedImpactDir,
    ushort PackedImpactPos);

public sealed class GGlassNameBuildData
{
    private readonly ushort[] _pieceIndices;
    public GGlassNameBuildData(string? name, ushort scriptString, IEnumerable<ushort> pieceIndices)
    {
        ArgumentNullException.ThrowIfNull(pieceIndices);
        Name = name;
        ScriptString = scriptString;
        _pieceIndices = pieceIndices.ToArray();
    }
    public string? Name { get; }
    public ushort ScriptString { get; }
    public IReadOnlyList<ushort> PieceIndices => Array.AsReadOnly(_pieceIndices);
}

public sealed class GGlassDataBuildData
{
    private readonly GGlassPieceBuildData[] _pieces;
    private readonly GGlassNameBuildData[] _names;
    private readonly byte[] _pad14To7F;
    public GGlassDataBuildData(
        IEnumerable<GGlassPieceBuildData> pieces,
        ushort damageToWeaken,
        ushort damageToDestroy,
        IEnumerable<GGlassNameBuildData> names,
        byte[] pad14To7F,
        int? importedGlassNamesPointerRaw = null)
    {
        ArgumentNullException.ThrowIfNull(pieces); ArgumentNullException.ThrowIfNull(names); ArgumentNullException.ThrowIfNull(pad14To7F);
        _pieces = pieces.ToArray(); DamageToWeaken = damageToWeaken; DamageToDestroy = damageToDestroy; _names = names.Select(value => new GGlassNameBuildData(value.Name, value.ScriptString, value.PieceIndices)).ToArray(); _pad14To7F = pad14To7F.ToArray(); ImportedGlassNamesPointerRaw = importedGlassNamesPointerRaw;
    }
    public IReadOnlyList<GGlassPieceBuildData> Pieces => Array.AsReadOnly(_pieces);
    public ushort DamageToWeaken { get; }
    public ushort DamageToDestroy { get; }
    public IReadOnlyList<GGlassNameBuildData> Names => Array.AsReadOnly(_names.Select(value => new GGlassNameBuildData(value.Name, value.ScriptString, value.PieceIndices)).ToArray());
    public int? ImportedGlassNamesPointerRaw { get; }
    public byte[] GetPad14To7FCopy() => _pad14To7F.ToArray();
}

public interface IGameWorldMpBuildData : IXAssetBuildData
{
    string? Name { get; }
    GGlassDataBuildData? GlassData { get; }
}

public readonly record struct VehicleVec3BuildData(float X, float Y, float Z);

public readonly record struct VehicleFakeBodyBuildData(
    float AccelPitch, float AccelRoll, float VelPitch, float VelRoll, float SideVelPitch,
    float PitchStrength, float RollStrength, float PitchDampening, float RollDampening,
    float BoatRockingAmplitude, float BoatRockingPeriod, float BoatRockingRotationPeriod,
    float BoatRockingFadeoutSpeed, float BoatBouncingMinForce, float BoatBouncingMaxForce,
    float BoatBouncingRate, float BoatBouncingFadeinSpeed, float BoatBouncingFadeoutSteeringAngle);

public sealed class VehiclePhysicsBuildData
{
    public int PhysicsEnabled { get; init; }
    public string? PhysPresetName { get; init; }
    public SymbolicXAssetReference? PhysPresetReference { get; init; }
    public string? AccelGraphName { get; init; }
    public int SteeringAxle { get; init; }
    public int PowerAxle { get; init; }
    public int BrakingAxle { get; init; }
    public IReadOnlyList<float> Scalars { get; init; } = [];
}

public sealed class VehicleEngineSoundsBuildData
{
    public string? IdleLow { get; init; }
    public string? IdleHigh { get; init; }
    public string? EngineLow { get; init; }
    public string? EngineHigh { get; init; }
    public float EngineSoundSpeed { get; init; }
    public string? EngineStartUp { get; init; }
    public float EngineStartUpLength { get; init; }
    public string? EngineShutdown { get; init; }
    public string? EngineIdle { get; init; }
    public string? EngineSustain { get; init; }
    public string? EngineRampUp { get; init; }
    public float EngineRampUpLength { get; init; }
    public string? EngineRampDown { get; init; }
    public float EngineRampDownLength { get; init; }
}

public sealed class VehicleSuspensionSoundsBuildData
{
    public string? Soft { get; init; }
    public float SoftCompression { get; init; }
    public string? Hard { get; init; }
    public float HardCompression { get; init; }
}

/// <summary>Detached, fully typed VehicleDef root.  Cross-asset links are
/// symbolic comma-prefixed external identities; sound aliases are source
/// strings carried through their serialized nested-cell representation.</summary>
public interface IVehicleBuildData : IXAssetBuildData
{
    string? Name { get; }
    int Type { get; }
    string? UseHintString { get; }
    int Health { get; }
    int QuadBarrel { get; }
    IReadOnlyList<float> MovementScalars { get; }
    VehicleFakeBodyBuildData FakeBody { get; }
    float CollisionDamage { get; }
    float CollisionSpeed { get; }
    VehicleVec3BuildData KillcamOffset { get; }
    IReadOnlyList<int> DamageValues { get; }
    VehiclePhysicsBuildData Physics { get; }
    IReadOnlyList<float> BoostAndSteeringScalars { get; }
    int CamLookEnabled { get; }
    IReadOnlyList<float> CameraScalars { get; }
    string? TurretWeaponName { get; }
    SymbolicXAssetReference? TurretWeaponReference { get; }
    IReadOnlyList<float> TurretScalars { get; }
    string? TurretSpinSound { get; }
    string? TurretStopSound { get; }
    int TrophyEnabled { get; }
    float TrophyRadius { get; }
    float TrophyInactiveRadius { get; }
    int TrophyAmmoCount { get; }
    float TrophyReloadTime { get; }
    IReadOnlyList<ushort> TrophyTags { get; }
    SymbolicXAssetReference? CompassFriendlyIconReference { get; }
    SymbolicXAssetReference? CompassEnemyIconReference { get; }
    float CompassIconWidth { get; }
    float CompassIconHeight { get; }
    VehicleEngineSoundsBuildData EngineSounds { get; }
    VehicleSuspensionSoundsBuildData SuspensionSounds { get; }
    string? CollisionSound { get; }
    float CollisionBlendSpeed { get; }
    string? SpeedSound { get; }
    float SpeedSoundBlendSpeed { get; }
    string? SurfaceSoundPrefix { get; }
    IReadOnlyList<string?> SurfaceSoundAliases { get; }
    float SurfaceSoundBlendSpeed { get; }
    float SlideVolume { get; }
    float SlideBlendSpeed { get; }
    float InAirPitch { get; }
}

public readonly record struct Float3BuildData(float X, float Y, float Z);
public readonly record struct TriggerModelBuildData(int Contents, ushort HullCount, ushort FirstHull);
public readonly record struct TriggerHullBuildData(Float3BuildData MidPoint, Float3BuildData HalfSize, int Contents, ushort SlabCount, ushort FirstSlab);
public readonly record struct TriggerSlabBuildData(Float3BuildData Dir, float MidPoint, float HalfSize);
public readonly record struct StageBuildData(string? Name, Float3BuildData Origin, ushort TriggerIndex, byte SunPrimaryLightIndex, byte Pad13);

public sealed class MapTriggersBuildData
{
    public MapTriggersBuildData(IEnumerable<TriggerModelBuildData> models, IEnumerable<TriggerHullBuildData> hulls, IEnumerable<TriggerSlabBuildData> slabs)
    {
        ArgumentNullException.ThrowIfNull(models); ArgumentNullException.ThrowIfNull(hulls); ArgumentNullException.ThrowIfNull(slabs);
        Models = Array.AsReadOnly(models.ToArray()); Hulls = Array.AsReadOnly(hulls.ToArray()); Slabs = Array.AsReadOnly(slabs.ToArray());
    }
    public IReadOnlyList<TriggerModelBuildData> Models { get; }
    public IReadOnlyList<TriggerHullBuildData> Hulls { get; }
    public IReadOnlyList<TriggerSlabBuildData> Slabs { get; }
}

public interface IMapEntsBuildData : IXAssetBuildData
{
    string? Name { get; }
    byte[] GetEntityStringBytesCopy();
    MapTriggersBuildData Triggers { get; }
    IReadOnlyList<StageBuildData> Stages { get; }
    byte[] GetPad29To2BCopy();
}

public interface IAddonMapEntsBuildData : IXAssetBuildData
{
    string? Name { get; }
    byte[] GetEntityStringBytesCopy();
    MapTriggersBuildData Triggers { get; }
}

public enum MaterialShaderBytecodePointerSourceForm
{
    Null,
    Inline,
    Insert,
    PackedAlias
}

public sealed record MaterialShaderBytecodeBuildProvenance(
    MaterialShaderBytecodePointerSourceForm SourceForm,
    int? InsertOwnerRaw = null,
    int? ImportedPackedRaw = null);

public interface IMaterialShaderBuildData : IXAssetBuildData
{
    string? Name { get; }
    uint DataSize { get; }
    byte[]? GetDataCopy();
    byte[] GetProgramBytesCopy();
    MaterialShaderBytecodeBuildProvenance? BytecodeProvenance => null;
}

public interface ILoadedSoundBuildData : IXAssetBuildData
{
    string? Name { get; }
    ushort FrameCount { get; }
    ushort ChannelCount { get; }
    ushort SampleRate { get; }
    ushort Pad0E { get; }
    ushort Pad10 { get; }
    byte[]? GetSeekTableCopy();
    byte[]? GetPhysicalDataCopy();
}

public readonly record struct GfxImageStreamBuildData(ushort Width, ushort Height, uint LevelSizeAndOffset)
{
    public bool HasStreamingData => Width != 0 || Height != 0 || LevelSizeAndOffset != 0;
}
public interface IGfxImageBuildData : IXAssetBuildData
{
    string? Name { get; } byte Format { get; } byte LevelCount { get; } byte DimensionCount { get; } byte MultiFaceControl { get; } uint TextureFlags { get; }
    ushort Width { get; } ushort Height { get; } ushort Depth { get; } byte PixelDataBlock { get; } byte Pad0F { get; } uint RenderTargetPitch { get; } uint PixelsOffset { get; }
    byte MapType { get; } byte TextureSemantic { get; } byte Category { get; } byte Pad1B { get; } uint CardMemory { get; } ushort BaseWidth { get; } ushort BaseHeight { get; } ushort BaseDepth { get; } byte BaseLevelCount { get; } byte Cached { get; }
    IReadOnlyList<GfxImageStreamBuildData> StreamData { get; }
    /// <summary>
    /// Exact DB-header stream records captured for this image from the
    /// source fastfile's selected language table. Streamed images carry one
    /// record per <see cref="GfxImageStreamBuildData"/> entry. Source header
    /// offsets are discarded because they are container provenance, not
    /// reusable image semantics.
    /// </summary>
    IReadOnlyList<DbHeaderImageStreamEntry> SelectedLanguageStreamEntries => [];
    /// <summary>PS3 imagefile indices used only when StreamData is non-empty.
    /// These are logical package identities, never host file-system paths.</summary>
    IReadOnlyList<uint> ExternalStreamPackageIndices { get; }
    byte[]? GetPayloadCopy();
}

public readonly record struct FontGlyphBuildData(
    ushort Letter, sbyte X0, sbyte Y0, byte Dx, byte PixelWidth, byte PixelHeight, byte Padding,
    float S0, float T0, float S1, float T1);

public interface IFontBuildData : IXAssetBuildData
{
    string? Name { get; }
    int PixelHeight { get; }
    SymbolicXAssetReference? MaterialReference { get; }
    SymbolicXAssetReference? GlowMaterialReference { get; }
    IReadOnlyList<FontGlyphBuildData> Glyphs { get; }
}

public readonly record struct TechniqueVertexDeclarationBuildData(byte StreamCount, byte HasOptionalSource, IReadOnlyList<MaterialVertexStreamRoutingBuildData> Routing);
public readonly record struct MaterialVertexStreamRoutingBuildData(byte Source, byte Dest);
public enum TechniqueDirectPointerSourceForm
{
    Null,
    Inline,
    Insert,
    PackedAlias
}
public sealed record TechniqueDirectPointerBuildProvenance(
    TechniqueDirectPointerSourceForm SourceForm,
    int? InlineOwnerRaw = null,
    int? ImportedPackedRaw = null);
public readonly record struct TechniqueShaderArgumentBuildData(
    ushort Type,
    ushort Dest,
    int RawValue,
    Float4BuildData? Literal,
    TechniqueDirectPointerBuildProvenance? LiteralProvenance = null);
public readonly record struct Float4BuildData(float X, float Y, float Z, float W);
public sealed class TechniquePassBuildData
{
    public TechniquePassBuildData(TechniqueVertexDeclarationBuildData? vertexDeclaration, SymbolicXAssetReference? vertexShader, SymbolicXAssetReference? pixelShader, byte perPrimArgCount, byte perObjArgCount, byte stableArgCount, byte customSamplerFlags, byte precompiledIndex, IReadOnlyList<TechniqueShaderArgumentBuildData> arguments, NestedXAssetBuildLink? vertexShaderLink = null, NestedXAssetBuildLink? pixelShaderLink = null, TechniqueDirectPointerBuildProvenance? vertexDeclarationProvenance = null)
    { VertexDeclaration = vertexDeclaration; VertexShader = vertexShader; PixelShader = pixelShader; PerPrimArgCount = perPrimArgCount; PerObjArgCount = perObjArgCount; StableArgCount = stableArgCount; CustomSamplerFlags = customSamplerFlags; PrecompiledIndex = precompiledIndex; Arguments = Array.AsReadOnly(arguments.ToArray()); VertexShaderLink = vertexShaderLink; PixelShaderLink = pixelShaderLink; VertexDeclarationProvenance = vertexDeclarationProvenance; }
    public TechniqueVertexDeclarationBuildData? VertexDeclaration { get; } public SymbolicXAssetReference? VertexShader { get; } public SymbolicXAssetReference? PixelShader { get; } public byte PerPrimArgCount { get; } public byte PerObjArgCount { get; } public byte StableArgCount { get; } public byte CustomSamplerFlags { get; } public byte PrecompiledIndex { get; } public IReadOnlyList<TechniqueShaderArgumentBuildData> Arguments { get; } public NestedXAssetBuildLink? VertexShaderLink { get; } public NestedXAssetBuildLink? PixelShaderLink { get; } public TechniqueDirectPointerBuildProvenance? VertexDeclarationProvenance { get; }
}
public sealed class TechniqueBuildData
{
    public TechniqueBuildData(string? name, ushort flags, IReadOnlyList<TechniquePassBuildData> passes) { Name = name; Flags = flags; Passes = Array.AsReadOnly(passes.ToArray()); }
    public string? Name { get; } public ushort Flags { get; } public IReadOnlyList<TechniquePassBuildData> Passes { get; }
}

public enum MaterialTechniquePointerSourceForm
{
    Null,
    Inline,
    PackedAlias
}

/// <summary>
/// Exact source form for one of the 37 direct MaterialTechnique pointer
/// cells in a MaterialTechniqueSet root. InlineOwnerRaw identifies the
/// original LARGE technique root so later packed aliases can target its
/// relocated owner. ImportedPackedRaw is retained only for dependency-owned
/// aliases that have no owner in the zone being linked.
/// </summary>
public sealed record MaterialTechniqueSlotBuildProvenance(
    MaterialTechniquePointerSourceForm SourceForm,
    int? InlineOwnerRaw = null,
    int? ImportedPackedRaw = null);

public interface ITechniqueSetBuildData : IXAssetBuildData
{
    string? Name { get; } byte WorldVertexFormat { get; } IReadOnlyList<TechniqueBuildData?> TechniqueSlots { get; }
    IReadOnlyList<MaterialTechniqueSlotBuildProvenance> TechniqueSlotProvenance =>
        [];
}

public readonly record struct MaterialVec2BuildData(float X, float Y);
public readonly record struct MaterialConstantBuildData(uint NameHash, byte[] NameBytes, Float4BuildData Literal);
public sealed class MaterialTextureBuildData
{
    public MaterialTextureBuildData(uint nameHash, byte nameStart, byte nameEnd, byte samplerState, byte semantic, SymbolicXAssetReference? imageReference, MaterialWaterBuildData? water, NestedXAssetBuildLink? imageLink = null)
    { NameHash = nameHash; NameStart = nameStart; NameEnd = nameEnd; SamplerState = samplerState; Semantic = semantic; ImageReference = imageReference; Water = water; ImageLink = imageLink; }
    public uint NameHash { get; } public byte NameStart { get; } public byte NameEnd { get; } public byte SamplerState { get; } public byte Semantic { get; } public SymbolicXAssetReference? ImageReference { get; } public MaterialWaterBuildData? Water { get; } public NestedXAssetBuildLink? ImageLink { get; }
}
public sealed class MaterialWaterBuildData
{
    public MaterialWaterBuildData(uint writableRaw, int m, int n, float lx, float lz, float gravity, float windVelocity, MaterialVec2BuildData windDirection, float amplitude, Float4BuildData codeConstant, IReadOnlyList<float> h0x, IReadOnlyList<float> h0y, IReadOnlyList<float> wTerm, SymbolicXAssetReference? imageReference, NestedXAssetBuildLink? imageLink = null)
    { WritableRaw = writableRaw; M = m; N = n; Lx = lx; Lz = lz; Gravity = gravity; WindVelocity = windVelocity; WindDirection = windDirection; Amplitude = amplitude; CodeConstant = codeConstant; H0X = Array.AsReadOnly(h0x.ToArray()); H0Y = Array.AsReadOnly(h0y.ToArray()); WTerm = Array.AsReadOnly(wTerm.ToArray()); ImageReference = imageReference; ImageLink = imageLink; }
    public uint WritableRaw { get; } public int M { get; } public int N { get; } public float Lx { get; } public float Lz { get; } public float Gravity { get; } public float WindVelocity { get; } public MaterialVec2BuildData WindDirection { get; } public float Amplitude { get; } public Float4BuildData CodeConstant { get; } public IReadOnlyList<float> H0X { get; } public IReadOnlyList<float> H0Y { get; } public IReadOnlyList<float> WTerm { get; } public SymbolicXAssetReference? ImageReference { get; } public NestedXAssetBuildLink? ImageLink { get; }
}
public enum MaterialLoadBitsPointerSourceForm
{
    Inline,
    Insert,
    PackedAlias
}

/// <summary>
/// Address-free identity for one GfxStateBits loadBits alias cell. The target
/// token identifies the cell read by an imported packed pointer; the owner
/// token identifies the current GfxStateBits pointer cell that later values
/// may alias.
/// </summary>
public readonly record struct MaterialLoadBitsAliasToken(int Value);

public sealed record MaterialLoadBitsLinkerProvenance(
    MaterialLoadBitsPointerSourceForm SourceForm =
        MaterialLoadBitsPointerSourceForm.Inline,
    int? ImportedPackedRaw = null,
    MaterialLoadBitsAliasToken? TargetAlias = null,
    MaterialLoadBitsAliasToken? OwnerAlias = null)
{
    public static MaterialLoadBitsLinkerProvenance Empty { get; } = new();
}

public readonly record struct MaterialStateBitsBuildData(
    IReadOnlyList<uint> LoadBits,
    uint Tail,
    MaterialLoadBitsLinkerProvenance? LinkerProvenance = null);
public interface IMaterialBuildData : IXAssetBuildData
{
    string? Name { get; } byte GameFlags { get; } byte SortKey { get; } byte TextureAtlasRowCount { get; } byte TextureAtlasColumnCount { get; } uint SurfaceTypeBits { get; } ushort HashIndex { get; } ushort Pad16 { get; }
    IReadOnlyList<byte> StateBitsEntries { get; } byte StateFlags { get; } byte CameraRegion { get; } byte Pad43 { get; } ushort Pad8E { get; } bool HasRuntimeTechniqueSlotState { get; }
    SymbolicXAssetReference? TechniqueSetReference { get; } NestedXAssetBuildLink? TechniqueSetLink => null; IReadOnlyList<MaterialTextureBuildData> Textures { get; } IReadOnlyList<MaterialConstantBuildData> Constants { get; } IReadOnlyList<MaterialStateBitsBuildData> StateBits { get; } IReadOnlyList<string?> XStrings { get; }
}

public sealed class PhysPlaneBuildData
{
    public PhysPlaneBuildData(Float3BuildData normal, float dist, byte type, byte signBits, byte[] pad12) { Normal = normal; Dist = dist; Type = type; SignBits = signBits; Pad12 = pad12.ToArray(); }
    public Float3BuildData Normal { get; } public float Dist { get; } public byte Type { get; } public byte SignBits { get; } public byte[] Pad12 { get; }
}
public sealed class PhysBrushSideBuildData
{
    public PhysBrushSideBuildData(PhysPlaneBuildData? plane, ushort materialNum, byte firstAdjacentSideOffset, byte edgeCount) { Plane = plane; MaterialNum = materialNum; FirstAdjacentSideOffset = firstAdjacentSideOffset; EdgeCount = edgeCount; }
    public PhysPlaneBuildData? Plane { get; } public ushort MaterialNum { get; } public byte FirstAdjacentSideOffset { get; } public byte EdgeCount { get; }
}
public sealed class PhysBrushBuildData
{
    public PhysBrushBuildData(ushort glassPieceIndex, IReadOnlyList<PhysBrushSideBuildData> sides, IReadOnlyList<byte> baseAdjacentSide, IReadOnlyList<short> axialMaterialNum, IReadOnlyList<byte> firstAdjacentSideOffsets, IReadOnlyList<byte> edgeCount, int? importedSidesPackedRaw = null) { GlassPieceIndex = glassPieceIndex; Sides = Array.AsReadOnly(sides.ToArray()); BaseAdjacentSide = Array.AsReadOnly(baseAdjacentSide.ToArray()); AxialMaterialNum = Array.AsReadOnly(axialMaterialNum.ToArray()); FirstAdjacentSideOffsets = Array.AsReadOnly(firstAdjacentSideOffsets.ToArray()); EdgeCount = Array.AsReadOnly(edgeCount.ToArray()); ImportedSidesPackedRaw = importedSidesPackedRaw; }
    public ushort GlassPieceIndex { get; } public IReadOnlyList<PhysBrushSideBuildData> Sides { get; } public IReadOnlyList<byte> BaseAdjacentSide { get; } public IReadOnlyList<short> AxialMaterialNum { get; } public IReadOnlyList<byte> FirstAdjacentSideOffsets { get; } public IReadOnlyList<byte> EdgeCount { get; } public int? ImportedSidesPackedRaw { get; }
}
public sealed class PhysBrushWrapperBuildData
{
    public PhysBrushWrapperBuildData(Float3BuildData midpoint, Float3BuildData halfSize, PhysBrushBuildData brush, int totalEdgeCount, IReadOnlyList<PhysPlaneBuildData> planes, int? importedPlanesPackedRaw = null) { Midpoint = midpoint; HalfSize = halfSize; Brush = brush; TotalEdgeCount = totalEdgeCount; Planes = Array.AsReadOnly(planes.ToArray()); ImportedPlanesPackedRaw = importedPlanesPackedRaw; }
    public Float3BuildData Midpoint { get; } public Float3BuildData HalfSize { get; } public PhysBrushBuildData Brush { get; } public int TotalEdgeCount { get; } public IReadOnlyList<PhysPlaneBuildData> Planes { get; } public int? ImportedPlanesPackedRaw { get; }
}
public sealed class PhysGeomBuildData
{
    public PhysGeomBuildData(PhysBrushWrapperBuildData? brushWrapper, int type, IReadOnlyList<Float3BuildData> orientation, Float3BuildData midpoint, Float3BuildData halfSize) { BrushWrapper = brushWrapper; Type = type; Orientation = Array.AsReadOnly(orientation.ToArray()); Midpoint = midpoint; HalfSize = halfSize; }
    public PhysBrushWrapperBuildData? BrushWrapper { get; } public int Type { get; } public IReadOnlyList<Float3BuildData> Orientation { get; } public Float3BuildData Midpoint { get; } public Float3BuildData HalfSize { get; }
}
public interface IPhysCollmapBuildData : IXAssetBuildData
{
    string? Name { get; } IReadOnlyList<PhysGeomBuildData> Geoms { get; } Float3BuildData CenterOfMass { get; } Float3BuildData MomentsOfInertia { get; } Float3BuildData ProductsOfInertia { get; } Float3BuildData BoundsMidpoint { get; } Float3BuildData BoundsHalfSize { get; }
}

public readonly record struct XAnimNotifyBuildData(ushort Name, float Time);
public readonly record struct XAnimQuat2BuildData(short Value0, short Value1);
public readonly record struct XAnimQuatBuildData(short Value0, short Value1, short Value2, short Value3);
public readonly record struct XAnimSmallTransFrameBuildData(byte X, byte Y, byte Z);
public readonly record struct XAnimLargeTransFrameBuildData(short X, short Y, short Z);
public sealed class XAnimTransFramesBuildData
{
    public XAnimTransFramesBuildData(Float3BuildData mins, Float3BuildData size, IReadOnlyList<ushort> dynamicFrames, IReadOnlyList<XAnimSmallTransFrameBuildData>? smallFrames, IReadOnlyList<XAnimLargeTransFrameBuildData>? largeFrames) { Mins = mins; Size = size; DynamicFrames = Array.AsReadOnly(dynamicFrames.ToArray()); SmallFrames = smallFrames is null ? null : Array.AsReadOnly(smallFrames.ToArray()); LargeFrames = largeFrames is null ? null : Array.AsReadOnly(largeFrames.ToArray()); }
    public Float3BuildData Mins { get; } public Float3BuildData Size { get; } public IReadOnlyList<ushort> DynamicFrames { get; } public IReadOnlyList<XAnimSmallTransFrameBuildData>? SmallFrames { get; } public IReadOnlyList<XAnimLargeTransFrameBuildData>? LargeFrames { get; }
}
public sealed class XAnimPartTransBuildData
{
    public XAnimPartTransBuildData(ushort size, byte smallTrans, byte pad3, Float3BuildData? frame0, XAnimTransFramesBuildData? frames) { Size = size; SmallTrans = smallTrans; Pad3 = pad3; Frame0 = frame0; Frames = frames; }
    public ushort Size { get; } public byte SmallTrans { get; } public byte Pad3 { get; } public Float3BuildData? Frame0 { get; } public XAnimTransFramesBuildData? Frames { get; }
}
public sealed class XAnimQuat2FramesBuildData
{
    public XAnimQuat2FramesBuildData(IReadOnlyList<ushort> dynamicFrames, IReadOnlyList<XAnimQuat2BuildData> frames) { DynamicFrames = Array.AsReadOnly(dynamicFrames.ToArray()); Frames = Array.AsReadOnly(frames.ToArray()); }
    public IReadOnlyList<ushort> DynamicFrames { get; } public IReadOnlyList<XAnimQuat2BuildData> Frames { get; }
}
public sealed class XAnimQuat2PartBuildData
{
    public XAnimQuat2PartBuildData(ushort size, byte pad2, byte pad3, XAnimQuat2BuildData? frame0, XAnimQuat2FramesBuildData? frames) { Size = size; Pad2 = pad2; Pad3 = pad3; Frame0 = frame0; Frames = frames; }
    public ushort Size { get; } public byte Pad2 { get; } public byte Pad3 { get; } public XAnimQuat2BuildData? Frame0 { get; } public XAnimQuat2FramesBuildData? Frames { get; }
}
public sealed class XAnimQuatFramesBuildData
{
    public XAnimQuatFramesBuildData(IReadOnlyList<ushort> dynamicFrames, IReadOnlyList<XAnimQuatBuildData> frames) { DynamicFrames = Array.AsReadOnly(dynamicFrames.ToArray()); Frames = Array.AsReadOnly(frames.ToArray()); }
    public IReadOnlyList<ushort> DynamicFrames { get; } public IReadOnlyList<XAnimQuatBuildData> Frames { get; }
}
public sealed class XAnimQuatPartBuildData
{
    public XAnimQuatPartBuildData(ushort size, byte pad2, byte pad3, XAnimQuatBuildData? frame0, XAnimQuatFramesBuildData? frames) { Size = size; Pad2 = pad2; Pad3 = pad3; Frame0 = frame0; Frames = frames; }
    public ushort Size { get; } public byte Pad2 { get; } public byte Pad3 { get; } public XAnimQuatBuildData? Frame0 { get; } public XAnimQuatFramesBuildData? Frames { get; }
}
public sealed class XAnimDeltaBuildData
{
    public XAnimDeltaBuildData(XAnimPartTransBuildData? trans, XAnimQuat2PartBuildData? quat2, XAnimQuatPartBuildData? quat) { Trans = trans; Quat2 = quat2; Quat = quat; }
    public XAnimPartTransBuildData? Trans { get; } public XAnimQuat2PartBuildData? Quat2 { get; } public XAnimQuatPartBuildData? Quat { get; }
}
public interface IXAnimBuildData : IXAssetBuildData
{
    string? Name { get; } ushort DataByteCount { get; } ushort DataShortCount { get; } ushort DataIntCount { get; } ushort RandomDataByteCount { get; } ushort RandomDataIntCount { get; } ushort NumFrames { get; } byte Flags { get; } byte DeltaFlags { get; } IReadOnlyList<byte> BoneCounts { get; } byte BoneNameCount { get; } byte NotifyCount { get; } byte AssetTypeValue { get; } byte Pad1F { get; } int RandomDataShortCount { get; } int IndexCount { get; } float Framerate { get; } float Frequency { get; }
    IReadOnlyList<ushort> Names { get; } IReadOnlyList<byte> DataBytes { get; } IReadOnlyList<short> DataShorts { get; } IReadOnlyList<int> DataInts { get; } IReadOnlyList<short> RandomDataShorts { get; } IReadOnlyList<byte> RandomDataBytes { get; } IReadOnlyList<int> RandomDataInts { get; } IReadOnlyList<ushort> Indices { get; } IReadOnlyList<XAnimNotifyBuildData> Notify { get; } XAnimDeltaBuildData? Delta { get; }
}

public sealed class XModelCollisionNodeBuildData
{
    public XModelCollisionNodeBuildData(ushort minsX, ushort minsY, ushort minsZ, ushort maxsX, ushort maxsY, ushort maxsZ, ushort childBeginIndex, ushort childCount) { MinsX = minsX; MinsY = minsY; MinsZ = minsZ; MaxsX = maxsX; MaxsY = maxsY; MaxsZ = maxsZ; ChildBeginIndex = childBeginIndex; ChildCount = childCount; }
    public ushort MinsX { get; } public ushort MinsY { get; } public ushort MinsZ { get; } public ushort MaxsX { get; } public ushort MaxsY { get; } public ushort MaxsZ { get; } public ushort ChildBeginIndex { get; } public ushort ChildCount { get; }
}
/// <summary>
/// Address-free identity for one imported XModel storage range.  Equal tokens
/// mean the loader materialized the same direct-pointer payload; the source
/// block address itself never escapes capture.
/// </summary>
public readonly record struct XModelReusableStorageToken(int Value);

public enum XModelNestedPointerSourceForm
{
    Inline,
    Insert,
    PackedAlias
}

public sealed record XModelSurfaceLinkerProvenance(
    XModelReusableStorageToken? Verts0Storage = null,
    XModelReusableStorageToken? Verts1Storage = null,
    XModelReusableStorageToken? TriIndicesStorage = null)
{
    public static XModelSurfaceLinkerProvenance Empty { get; } = new();
}

public sealed record XModelLinkerProvenance(
    XModelReusableStorageToken? BoneNamesStorage = null,
    XModelReusableStorageToken? ParentListStorage = null,
    XModelReusableStorageToken? QuatsStorage = null,
    XModelReusableStorageToken? TransStorage = null,
    XModelReusableStorageToken? PartClassificationStorage = null,
    XModelReusableStorageToken? BaseMatStorage = null,
    XModelNestedPointerSourceForm PhysPresetForm = XModelNestedPointerSourceForm.Inline,
    XModelNestedPointerSourceForm PhysCollmapForm = XModelNestedPointerSourceForm.Inline)
{
    public static XModelLinkerProvenance Empty { get; } = new();
}

public sealed class XModelCollisionTreeBuildData
{
    public XModelCollisionTreeBuildData(Float3BuildData trans, Float3BuildData scale, IReadOnlyList<XModelCollisionNodeBuildData> nodes, IReadOnlyList<ushort> leafs) { Trans = trans; Scale = scale; Nodes = Array.AsReadOnly(nodes.ToArray()); Leafs = Array.AsReadOnly(leafs.ToArray()); }
    public Float3BuildData Trans { get; } public Float3BuildData Scale { get; } public IReadOnlyList<XModelCollisionNodeBuildData> Nodes { get; } public IReadOnlyList<ushort> Leafs { get; }
}
public sealed class XModelRigidVertListBuildData
{
    public XModelRigidVertListBuildData(ushort boneOffset, ushort vertCount, ushort triOffset, ushort triCount, XModelCollisionTreeBuildData? collisionTree) { BoneOffset = boneOffset; VertCount = vertCount; TriOffset = triOffset; TriCount = triCount; CollisionTree = collisionTree; }
    public ushort BoneOffset { get; } public ushort VertCount { get; } public ushort TriOffset { get; } public ushort TriCount { get; } public XModelCollisionTreeBuildData? CollisionTree { get; }
}
public sealed class XModelSurfaceBuildData
{
    public XModelSurfaceBuildData(ushort flagsOrPad00, byte streamFlags, byte pad03, ushort vertCount, ushort triCount, IReadOnlyList<ushort> triIndices, ushort blend0, ushort blend1, ushort blend2, ushort blend3, IReadOnlyList<ushort> vertsBlend, IReadOnlyList<byte> verts0, int vb0StreamSource, int vb0DataOffset, IReadOnlyList<byte> verts1, int vb1StreamSource, int vb1DataOffset, IReadOnlyList<XModelRigidVertListBuildData> rigidVertLists, int indexBufferDataOffset, IReadOnlyList<uint> partBits, XModelSurfaceLinkerProvenance? linkerProvenance = null)
    { FlagsOrPad00 = flagsOrPad00; StreamFlags = streamFlags; Pad03 = pad03; VertCount = vertCount; TriCount = triCount; TriIndices = Array.AsReadOnly(triIndices.ToArray()); Blend0 = blend0; Blend1 = blend1; Blend2 = blend2; Blend3 = blend3; VertsBlend = Array.AsReadOnly(vertsBlend.ToArray()); Verts0 = Array.AsReadOnly(verts0.ToArray()); Vb0StreamSource = vb0StreamSource; Vb0DataOffset = vb0DataOffset; Verts1 = Array.AsReadOnly(verts1.ToArray()); Vb1StreamSource = vb1StreamSource; Vb1DataOffset = vb1DataOffset; RigidVertLists = Array.AsReadOnly(rigidVertLists.ToArray()); IndexBufferDataOffset = indexBufferDataOffset; PartBits = Array.AsReadOnly(partBits.ToArray()); LinkerProvenance = linkerProvenance ?? XModelSurfaceLinkerProvenance.Empty; }
    public ushort FlagsOrPad00 { get; } public byte StreamFlags { get; } public byte Pad03 { get; } public ushort VertCount { get; } public ushort TriCount { get; } public IReadOnlyList<ushort> TriIndices { get; } public ushort Blend0 { get; } public ushort Blend1 { get; } public ushort Blend2 { get; } public ushort Blend3 { get; } public IReadOnlyList<ushort> VertsBlend { get; } public IReadOnlyList<byte> Verts0 { get; } public int Vb0StreamSource { get; } public int Vb0DataOffset { get; } public IReadOnlyList<byte> Verts1 { get; } public int Vb1StreamSource { get; } public int Vb1DataOffset { get; } public IReadOnlyList<XModelRigidVertListBuildData> RigidVertLists { get; } public int IndexBufferDataOffset { get; } public IReadOnlyList<uint> PartBits { get; } public XModelSurfaceLinkerProvenance LinkerProvenance { get; }
}
public interface IXModelSurfsBuildData : IXAssetBuildData
{
    string? Name { get; } ushort NumSurfs => checked((ushort)Surfaces.Count); ushort Pad0A { get; } IReadOnlyList<uint> PartBits { get; } IReadOnlyList<XModelSurfaceBuildData> Surfaces { get; }
    int? ImportedSurfacesPackedRaw => null;
}
public sealed class XModelLodBuildData
{
    public XModelLodBuildData(float dist, ushort numSurfs, ushort surfIndex, IReadOnlyList<uint> partBits, IXModelSurfsBuildData? modelSurfs, XModelNestedPointerSourceForm modelSurfsSourceForm = XModelNestedPointerSourceForm.Inline, NestedXAssetBuildLink? modelSurfsLink = null) { Dist = dist; NumSurfs = numSurfs; SurfIndex = surfIndex; PartBits = Array.AsReadOnly(partBits.ToArray()); ModelSurfs = modelSurfs; ModelSurfsSourceForm = modelSurfsSourceForm; ModelSurfsLink = modelSurfsLink; }
    public float Dist { get; } public ushort NumSurfs { get; } public ushort SurfIndex { get; } public IReadOnlyList<uint> PartBits { get; } public IXModelSurfsBuildData? ModelSurfs { get; } public XModelNestedPointerSourceForm ModelSurfsSourceForm { get; } public NestedXAssetBuildLink? ModelSurfsLink { get; }
}
public sealed class XModelDObjAnimMatBuildData
{
    public XModelDObjAnimMatBuildData(Float4BuildData quat, Float3BuildData trans, float transWeight) { Quat = quat; Trans = trans; TransWeight = transWeight; }
    public Float4BuildData Quat { get; } public Float3BuildData Trans { get; } public float TransWeight { get; }
}
public sealed class XModelCollSurfBuildData
{
    public XModelCollSurfBuildData(Float3BuildData midpoint, Float3BuildData halfSize, int boneIdx, int contents, int surfFlags) { Midpoint = midpoint; HalfSize = halfSize; BoneIdx = boneIdx; Contents = contents; SurfFlags = surfFlags; }
    public Float3BuildData Midpoint { get; } public Float3BuildData HalfSize { get; } public int BoneIdx { get; } public int Contents { get; } public int SurfFlags { get; }
}
public sealed class XModelBoneInfoBuildData
{
    public XModelBoneInfoBuildData(Float3BuildData midpoint, Float3BuildData halfSize, float radiusSquared) { Midpoint = midpoint; HalfSize = halfSize; RadiusSquared = radiusSquared; }
    public Float3BuildData Midpoint { get; } public Float3BuildData HalfSize { get; } public float RadiusSquared { get; }
}
public interface IXModelBuildData : IXAssetBuildData
{
    string? Name { get; } byte NumBones { get; } byte NumRootBones { get; } byte NumSurfs { get; } byte Pad07 { get; } float Scale { get; } IReadOnlyList<uint> NoScalePartBits { get; } IReadOnlyList<ushort> BoneNames { get; } IReadOnlyList<byte> ParentList { get; } IReadOnlyList<short> Quats { get; } IReadOnlyList<float> Trans { get; } IReadOnlyList<byte> PartClassification { get; } IReadOnlyList<XModelDObjAnimMatBuildData> BaseMat { get; } IReadOnlyList<SymbolicXAssetReference?> MaterialReferences { get; } IReadOnlyList<NestedXAssetBuildLink?> MaterialLinks => []; IReadOnlyList<XModelLodBuildData> Lods { get; } byte MaxLoadedLod { get; } byte NumLods { get; } byte CollLod { get; } byte Flags { get; } IReadOnlyList<XModelCollSurfBuildData> CollSurfs { get; } int Contents { get; } IReadOnlyList<XModelBoneInfoBuildData> BoneInfo { get; } float Radius { get; } Float3BuildData BoundsMidpoint { get; } Float3BuildData BoundsHalfSize { get; } IReadOnlyList<ushort> InvHighMipRadius { get; } int MemUsage { get; } SymbolicXAssetReference? PhysPresetReference { get; } SymbolicXAssetReference? PhysCollmapReference { get; }
    NestedXAssetBuildLink? PhysPresetLink => null;
    NestedXAssetBuildLink? PhysCollmapLink => null;
    XModelLinkerProvenance LinkerProvenance => XModelLinkerProvenance.Empty;
}
public sealed class SoundSpeakerLevelBuildData
{
    public SoundSpeakerLevelBuildData(int speaker, int numLevels, float level0, float level1) { Speaker = speaker; NumLevels = numLevels; Level0 = level0; Level1 = level1; }
    public int Speaker { get; } public int NumLevels { get; } public float Level0 { get; } public float Level1 { get; }
}
public sealed class SoundChannelMapBuildData
{
    public SoundChannelMapBuildData(int entryCount, IReadOnlyList<SoundSpeakerLevelBuildData> speakers) { EntryCount = entryCount; Speakers = Array.AsReadOnly(speakers.ToArray()); }
    public int EntryCount { get; } public IReadOnlyList<SoundSpeakerLevelBuildData> Speakers { get; }
}
public sealed class SoundSpeakerMapBuildData
{
    public SoundSpeakerMapBuildData(byte isDefault, IReadOnlyList<byte> padding, string? name, IReadOnlyList<SoundChannelMapBuildData> channelMaps) { IsDefault = isDefault; Padding = Array.AsReadOnly(padding.ToArray()); Name = name; ChannelMaps = Array.AsReadOnly(channelMaps.ToArray()); }
    public byte IsDefault { get; } public IReadOnlyList<byte> Padding { get; } public string? Name { get; } public IReadOnlyList<SoundChannelMapBuildData> ChannelMaps { get; }
}
public sealed class SoundFileBuildData
{
    public SoundFileBuildData(SndAliasTypeBuildKind kind, byte exists, ushort padding, SymbolicXAssetReference? loadedSoundReference, uint streamedFileIndex, int streamFileOffset, int streamFileLength, string? externalDirectory, string? externalFilename, NestedXAssetBuildLink? loadedSoundLink = null) { Kind = kind; Exists = exists; Padding = padding; LoadedSoundReference = loadedSoundReference; StreamedFileIndex = streamedFileIndex; StreamFileOffset = streamFileOffset; StreamFileLength = streamFileLength; ExternalDirectory = externalDirectory; ExternalFilename = externalFilename; LoadedSoundLink = loadedSoundLink; }
    public SndAliasTypeBuildKind Kind { get; } public byte Exists { get; } public ushort Padding { get; } public SymbolicXAssetReference? LoadedSoundReference { get; } public uint StreamedFileIndex { get; } public int StreamFileOffset { get; } public int StreamFileLength { get; } public string? ExternalDirectory { get; } public string? ExternalFilename { get; } public NestedXAssetBuildLink? LoadedSoundLink { get; }
}
public enum SndAliasTypeBuildKind : byte { Unknown = 0, Loaded = 1, Streamed = 2, Primed = 3 }
public enum SoundDirectPointerSourceForm
{
    Null,
    Inline,
    Insert,
    PackedAlias
}
public sealed record SoundDirectPointerBuildProvenance(
    SoundDirectPointerSourceForm SourceForm,
    int? ImportedPackedRaw = null);
public sealed class SoundAliasBuildData
{
    public SoundAliasBuildData(
        string? aliasName,
        string? subtitle,
        string? secondaryAliasName,
        string? chainAliasName,
        string? mixerGroup,
        IReadOnlyList<SoundFileBuildData> soundFiles,
        int sequence,
        float volumeMin,
        float volumeMax,
        float pitchMin,
        float pitchMax,
        float distanceMin,
        float distanceMax,
        float velocityMin,
        int flags,
        float slavePercentage,
        float probability,
        float lfePercentage,
        float centerPercentage,
        int startDelay,
        SymbolicXAssetReference? volumeFalloffCurveReference,
        float envelopMin,
        float envelopMax,
        float envelopPercentage,
        SoundSpeakerMapBuildData? speakerMap,
        NestedXAssetBuildLink? volumeFalloffCurveLink = null,
        SoundDirectPointerBuildProvenance? soundFilesPointerProvenance = null,
        SoundDirectPointerBuildProvenance? speakerMapPointerProvenance = null)
    {
        ArgumentNullException.ThrowIfNull(soundFiles);

        AliasName = aliasName;
        Subtitle = subtitle;
        SecondaryAliasName = secondaryAliasName;
        ChainAliasName = chainAliasName;
        MixerGroup = mixerGroup;
        SoundFiles = Array.AsReadOnly(soundFiles.ToArray());
        Sequence = sequence;
        VolumeMin = volumeMin;
        VolumeMax = volumeMax;
        PitchMin = pitchMin;
        PitchMax = pitchMax;
        DistanceMin = distanceMin;
        DistanceMax = distanceMax;
        VelocityMin = velocityMin;
        Flags = flags;
        SlavePercentage = slavePercentage;
        Probability = probability;
        LfePercentage = lfePercentage;
        CenterPercentage = centerPercentage;
        StartDelay = startDelay;
        VolumeFalloffCurveReference = volumeFalloffCurveReference;
        EnvelopMin = envelopMin;
        EnvelopMax = envelopMax;
        EnvelopPercentage = envelopPercentage;
        SpeakerMap = speakerMap;
        VolumeFalloffCurveLink = volumeFalloffCurveLink;
        SoundFilesPointerProvenance = soundFilesPointerProvenance;
        SpeakerMapPointerProvenance = speakerMapPointerProvenance;
    }

    public string? AliasName { get; }
    public string? Subtitle { get; }
    public string? SecondaryAliasName { get; }
    public string? ChainAliasName { get; }
    public string? MixerGroup { get; }
    public IReadOnlyList<SoundFileBuildData> SoundFiles { get; }
    public int Sequence { get; }
    public float VolumeMin { get; }
    public float VolumeMax { get; }
    public float PitchMin { get; }
    public float PitchMax { get; }
    public float DistanceMin { get; }
    public float DistanceMax { get; }
    public float VelocityMin { get; }
    public int Flags { get; }
    public float SlavePercentage { get; }
    public float Probability { get; }
    public float LfePercentage { get; }
    public float CenterPercentage { get; }
    public int StartDelay { get; }
    public SymbolicXAssetReference? VolumeFalloffCurveReference { get; }
    public float EnvelopMin { get; }
    public float EnvelopMax { get; }
    public float EnvelopPercentage { get; }
    public SoundSpeakerMapBuildData? SpeakerMap { get; }
    public NestedXAssetBuildLink? VolumeFalloffCurveLink { get; }
    public SoundDirectPointerBuildProvenance? SoundFilesPointerProvenance { get; }
    public SoundDirectPointerBuildProvenance? SpeakerMapPointerProvenance { get; }
}
public interface ISoundAliasListBuildData : IXAssetBuildData { string? AliasName { get; } IReadOnlyList<SoundAliasBuildData> Aliases { get; } }

public readonly record struct FxVec3BuildData(float X, float Y, float Z);
public readonly record struct FxFloatRangeBuildData(float Base, float Amplitude);
public readonly record struct FxIntRangeBuildData(int Base, int Amplitude);
public readonly record struct FxSpawnBuildData(int LoopingIntervalMsec, int Count);
public readonly record struct FxAtlasBuildData(byte Behavior, byte Index, byte Fps, byte LoopCount, byte ColIndexBits, byte RowIndexBits, short EntryCount);
public readonly record struct FxBoundsBuildData(FxVec3BuildData MidPoint, FxVec3BuildData HalfSize);
public readonly record struct FxColorBuildData(byte R, byte G, byte B, byte A);
public sealed class FxVelocityInFrameBuildData
{
    public FxVelocityInFrameBuildData(FxVec3BuildData velocityBase, FxVec3BuildData velocityAmplitude, FxVec3BuildData totalDeltaBase, FxVec3BuildData totalDeltaAmplitude) { VelocityBase = velocityBase; VelocityAmplitude = velocityAmplitude; TotalDeltaBase = totalDeltaBase; TotalDeltaAmplitude = totalDeltaAmplitude; }
    public FxVec3BuildData VelocityBase { get; } public FxVec3BuildData VelocityAmplitude { get; } public FxVec3BuildData TotalDeltaBase { get; } public FxVec3BuildData TotalDeltaAmplitude { get; }
}
public sealed class FxVelocitySampleBuildData
{
    public FxVelocitySampleBuildData(FxVelocityInFrameBuildData local, FxVelocityInFrameBuildData world) { Local = local; World = world; }
    public FxVelocityInFrameBuildData Local { get; } public FxVelocityInFrameBuildData World { get; }
}
public sealed class FxVisualStateBuildData
{
    public FxVisualStateBuildData(FxColorBuildData color, float rotationDelta, float rotationTotal, float size0, float size1, float scale) { Color = color; RotationDelta = rotationDelta; RotationTotal = rotationTotal; Size0 = size0; Size1 = size1; Scale = scale; }
    public FxColorBuildData Color { get; } public float RotationDelta { get; } public float RotationTotal { get; } public float Size0 { get; } public float Size1 { get; } public float Scale { get; }
}
public sealed class FxVisualStateSampleBuildData
{
    public FxVisualStateSampleBuildData(FxVisualStateBuildData @base, FxVisualStateBuildData amplitude) { Base = @base; Amplitude = amplitude; }
    public FxVisualStateBuildData Base { get; } public FxVisualStateBuildData Amplitude { get; }
}
public enum FxVisualBuildKind : byte { Material, Model, Sound, Effect, NoChild }
public sealed class FxVisualBuildData
{
    public FxVisualBuildData(FxVisualBuildKind kind, SymbolicXAssetReference? materialReference = null, SymbolicXAssetReference? modelReference = null, SymbolicXAssetReference? soundReference = null, SymbolicXAssetReference? effectReference = null, int reserved = 0, NestedXAssetBuildLink? materialLink = null, NestedXAssetBuildLink? modelLink = null) { Kind = kind; MaterialReference = materialReference; ModelReference = modelReference; SoundReference = soundReference; EffectReference = effectReference; Reserved = reserved; MaterialLink = materialLink; ModelLink = modelLink; }
    public FxVisualBuildKind Kind { get; } public SymbolicXAssetReference? MaterialReference { get; } public SymbolicXAssetReference? ModelReference { get; } public SymbolicXAssetReference? SoundReference { get; } public SymbolicXAssetReference? EffectReference { get; } public int Reserved { get; } [System.Text.Json.Serialization.JsonIgnore] public NestedXAssetBuildLink? MaterialLink { get; } [System.Text.Json.Serialization.JsonIgnore] public NestedXAssetBuildLink? ModelLink { get; }
}
public sealed class FxMarkVisualBuildData
{
    public FxMarkVisualBuildData(SymbolicXAssetReference? material0Reference, SymbolicXAssetReference? material1Reference, NestedXAssetBuildLink? material0Link = null, NestedXAssetBuildLink? material1Link = null) { Material0Reference = material0Reference; Material1Reference = material1Reference; Material0Link = material0Link; Material1Link = material1Link; }
    public SymbolicXAssetReference? Material0Reference { get; } public SymbolicXAssetReference? Material1Reference { get; } [System.Text.Json.Serialization.JsonIgnore] public NestedXAssetBuildLink? Material0Link { get; } [System.Text.Json.Serialization.JsonIgnore] public NestedXAssetBuildLink? Material1Link { get; }
}
public readonly record struct FxTrailVertexBuildData(float Pos0, float Pos1, float Normal0, float Normal1, float TexCoord);
public sealed class FxTrailBuildData
{
    public FxTrailBuildData(int scrollTimeMsec, int repeatDist, float invSplitDist, float invSplitArcDist, float invSplitTime, IReadOnlyList<FxTrailVertexBuildData> vertices, IReadOnlyList<ushort> indices) { ScrollTimeMsec = scrollTimeMsec; RepeatDist = repeatDist; InvSplitDist = invSplitDist; InvSplitArcDist = invSplitArcDist; InvSplitTime = invSplitTime; Vertices = Array.AsReadOnly(vertices.ToArray()); Indices = Array.AsReadOnly(indices.ToArray()); }
    public int ScrollTimeMsec { get; } public int RepeatDist { get; } public float InvSplitDist { get; } public float InvSplitArcDist { get; } public float InvSplitTime { get; } public IReadOnlyList<FxTrailVertexBuildData> Vertices { get; } public IReadOnlyList<ushort> Indices { get; }
}
public sealed class FxSparkFountainBuildData
{
    public FxSparkFountainBuildData(float gravity, float bounceFrac, float bounceRand, float sparkSpacing, float sparkLength, int sparkCount, float loopTime, float velMin, float velMax, float velConeFrac, float restSpeed, float boostTime, float boostFactor) { Gravity = gravity; BounceFrac = bounceFrac; BounceRand = bounceRand; SparkSpacing = sparkSpacing; SparkLength = sparkLength; SparkCount = sparkCount; LoopTime = loopTime; VelMin = velMin; VelMax = velMax; VelConeFrac = velConeFrac; RestSpeed = restSpeed; BoostTime = boostTime; BoostFactor = boostFactor; }
    public float Gravity { get; } public float BounceFrac { get; } public float BounceRand { get; } public float SparkSpacing { get; } public float SparkLength { get; } public int SparkCount { get; } public float LoopTime { get; } public float VelMin { get; } public float VelMax { get; } public float VelConeFrac { get; } public float RestSpeed { get; } public float BoostTime { get; } public float BoostFactor { get; }
}
public enum FxExtendedBuildKind : byte { None, Trail, SparkFountain, DefaultBytePayload }
public sealed class FxExtendedBuildData
{
    public FxExtendedBuildData(FxExtendedBuildKind kind, FxTrailBuildData? trail = null, FxSparkFountainBuildData? sparkFountain = null, byte defaultBytePayload = 0) { Kind = kind; Trail = trail; SparkFountain = sparkFountain; DefaultBytePayload = defaultBytePayload; }
    public FxExtendedBuildKind Kind { get; } public FxTrailBuildData? Trail { get; } public FxSparkFountainBuildData? SparkFountain { get; } public byte DefaultBytePayload { get; }
}
public sealed class FxElementBuildData
{
    public FxElementBuildData(int flags, FxSpawnBuildData spawn, FxFloatRangeBuildData spawnRange, FxFloatRangeBuildData fadeInRange, FxFloatRangeBuildData fadeOutRange, float spawnFrustumCullRadius, FxIntRangeBuildData spawnDelayMsec, FxIntRangeBuildData lifeSpanMsec, IReadOnlyList<FxFloatRangeBuildData> spawnOrigin, FxFloatRangeBuildData spawnOffsetRadius, FxFloatRangeBuildData spawnOffsetHeight, IReadOnlyList<FxFloatRangeBuildData> spawnAngles, IReadOnlyList<FxFloatRangeBuildData> angularVelocity, FxFloatRangeBuildData initialRotation, FxFloatRangeBuildData gravity, FxFloatRangeBuildData reflectionFactor, FxAtlasBuildData atlas, byte elemType, byte visualCount, byte velIntervalCount, byte visStateIntervalCount, IReadOnlyList<FxVelocitySampleBuildData> velocitySamples, IReadOnlyList<FxVisualStateSampleBuildData> visualSamples, IReadOnlyList<FxVisualBuildData> visuals, IReadOnlyList<FxMarkVisualBuildData> markVisuals, FxBoundsBuildData collBounds, SymbolicXAssetReference? effectOnImpactReference, SymbolicXAssetReference? effectOnDeathReference, SymbolicXAssetReference? effectEmittedReference, FxFloatRangeBuildData emitDist, FxFloatRangeBuildData emitDistVariance, FxExtendedBuildData? extended, byte sortOrder, byte lightingFrac, byte useItemClip, byte fadeInfo)
    { Flags = flags; Spawn = spawn; SpawnRange = spawnRange; FadeInRange = fadeInRange; FadeOutRange = fadeOutRange; SpawnFrustumCullRadius = spawnFrustumCullRadius; SpawnDelayMsec = spawnDelayMsec; LifeSpanMsec = lifeSpanMsec; SpawnOrigin = Array.AsReadOnly(spawnOrigin.ToArray()); SpawnOffsetRadius = spawnOffsetRadius; SpawnOffsetHeight = spawnOffsetHeight; SpawnAngles = Array.AsReadOnly(spawnAngles.ToArray()); AngularVelocity = Array.AsReadOnly(angularVelocity.ToArray()); InitialRotation = initialRotation; Gravity = gravity; ReflectionFactor = reflectionFactor; Atlas = atlas; ElemType = elemType; VisualCount = visualCount; VelIntervalCount = velIntervalCount; VisStateIntervalCount = visStateIntervalCount; VelocitySamples = Array.AsReadOnly(velocitySamples.ToArray()); VisualSamples = Array.AsReadOnly(visualSamples.ToArray()); Visuals = Array.AsReadOnly(visuals.ToArray()); MarkVisuals = Array.AsReadOnly(markVisuals.ToArray()); CollBounds = collBounds; EffectOnImpactReference = effectOnImpactReference; EffectOnDeathReference = effectOnDeathReference; EffectEmittedReference = effectEmittedReference; EmitDist = emitDist; EmitDistVariance = emitDistVariance; Extended = extended; SortOrder = sortOrder; LightingFrac = lightingFrac; UseItemClip = useItemClip; FadeInfo = fadeInfo; }
    public int Flags { get; } public FxSpawnBuildData Spawn { get; } public FxFloatRangeBuildData SpawnRange { get; } public FxFloatRangeBuildData FadeInRange { get; } public FxFloatRangeBuildData FadeOutRange { get; } public float SpawnFrustumCullRadius { get; } public FxIntRangeBuildData SpawnDelayMsec { get; } public FxIntRangeBuildData LifeSpanMsec { get; } public IReadOnlyList<FxFloatRangeBuildData> SpawnOrigin { get; } public FxFloatRangeBuildData SpawnOffsetRadius { get; } public FxFloatRangeBuildData SpawnOffsetHeight { get; } public IReadOnlyList<FxFloatRangeBuildData> SpawnAngles { get; } public IReadOnlyList<FxFloatRangeBuildData> AngularVelocity { get; } public FxFloatRangeBuildData InitialRotation { get; } public FxFloatRangeBuildData Gravity { get; } public FxFloatRangeBuildData ReflectionFactor { get; } public FxAtlasBuildData Atlas { get; } public byte ElemType { get; } public byte VisualCount { get; } public byte VelIntervalCount { get; } public byte VisStateIntervalCount { get; } public IReadOnlyList<FxVelocitySampleBuildData> VelocitySamples { get; } public IReadOnlyList<FxVisualStateSampleBuildData> VisualSamples { get; } public IReadOnlyList<FxVisualBuildData> Visuals { get; } public IReadOnlyList<FxMarkVisualBuildData> MarkVisuals { get; } public FxBoundsBuildData CollBounds { get; } public SymbolicXAssetReference? EffectOnImpactReference { get; } public SymbolicXAssetReference? EffectOnDeathReference { get; } public SymbolicXAssetReference? EffectEmittedReference { get; } public FxFloatRangeBuildData EmitDist { get; } public FxFloatRangeBuildData EmitDistVariance { get; } public FxExtendedBuildData? Extended { get; } public byte SortOrder { get; } public byte LightingFrac { get; } public byte UseItemClip { get; } public byte FadeInfo { get; }
}
public interface IFxEffectDefBuildData : IXAssetBuildData { string? Name { get; } int Flags { get; } int TotalSize { get; } int MsecLoopingLife { get; } int ElemDefCountLooping { get; } int ElemDefCountOneShot { get; } int ElemDefCountEmission { get; } IReadOnlyList<FxElementBuildData> Elements { get; } }
public sealed class FxImpactEntryBuildData
{
    public FxImpactEntryBuildData(IReadOnlyList<SymbolicXAssetReference?> surfaceEffects, IReadOnlyList<SymbolicXAssetReference?> fleshEffects, IReadOnlyList<NestedXAssetBuildLink?>? surfaceEffectLinks = null, IReadOnlyList<NestedXAssetBuildLink?>? fleshEffectLinks = null) { SurfaceEffects = Array.AsReadOnly(surfaceEffects.ToArray()); FleshEffects = Array.AsReadOnly(fleshEffects.ToArray()); SurfaceEffectLinks = Array.AsReadOnly((surfaceEffectLinks ?? new NestedXAssetBuildLink?[surfaceEffects.Count]).ToArray()); FleshEffectLinks = Array.AsReadOnly((fleshEffectLinks ?? new NestedXAssetBuildLink?[fleshEffects.Count]).ToArray()); }
    public IReadOnlyList<SymbolicXAssetReference?> SurfaceEffects { get; } public IReadOnlyList<SymbolicXAssetReference?> FleshEffects { get; } [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<NestedXAssetBuildLink?> SurfaceEffectLinks { get; } [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<NestedXAssetBuildLink?> FleshEffectLinks { get; }
}
public interface IFxImpactTableBuildData : IXAssetBuildData { string? Name { get; } IReadOnlyList<FxImpactEntryBuildData> Entries { get; } }

/// <summary>All non-owned links reachable from the two Weapon roots.  The
/// definition fields carry authored scalar/array data; this table carries
/// only symbolic external identities, never runtime pointers.</summary>
public sealed class WeaponReferenceBuildData
{
    public SymbolicXAssetReference? KillIcon { get; init; }
    public SymbolicXAssetReference? DpadIcon { get; init; }
    public IReadOnlyList<SymbolicXAssetReference?> GunModels { get; init; } = [];
    public SymbolicXAssetReference? HandModel { get; init; }
    public IReadOnlyList<SymbolicXAssetReference?> FlashEffects { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> Materials { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> Effects { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> WorldGunModels { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> WorldModels { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> IconMaterials { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> OverlayMaterials { get; init; } = [];
    public SymbolicXAssetReference? PhysCollmap { get; init; }
    public SymbolicXAssetReference? ProjectileModel { get; init; }
    public IReadOnlyList<SymbolicXAssetReference?> ProjectileEffects { get; init; } = [];
    public IReadOnlyList<SymbolicXAssetReference?> ImpactEffects { get; init; } = [];
    public SymbolicXAssetReference? IgnitionEffect { get; init; }
    public SymbolicXAssetReference? Tracer { get; init; }
    public SymbolicXAssetReference? TurretOverheatEffect { get; init; }
}

/// <summary>
/// Address-free identity assigned to one imported reusable storage range.
/// Equal tokens mean the native loader exposed the same backing bytes even
/// when two fields interpret those bytes through different element types.
/// </summary>
public readonly record struct WeaponReusableStorageToken(int Value);

public enum WeaponNestedPointerSourceForm
{
    Inline,
    Insert
}

/// <summary>
/// Wire-topology provenance that cannot be reconstructed from detached
/// semantic values alone. It contains no source block address or runtime
/// pointer.
/// </summary>
public sealed record WeaponLinkerProvenance(
    WeaponReusableStorageToken? HideTagsStorage = null,
    WeaponReusableStorageToken? WorldGunModelsStorage = null,
    WeaponNestedPointerSourceForm KillIconForm = WeaponNestedPointerSourceForm.Inline,
    WeaponNestedPointerSourceForm DpadIconForm = WeaponNestedPointerSourceForm.Inline)
{
    public static WeaponLinkerProvenance Empty { get; } = new();
}

/// <summary>Detached complete Weapon payload.  <see cref="Variant"/> and
/// <see cref="Definition"/> are scalar/array projections with all runtime
/// asset instances removed; links reside exclusively in <see cref="References"/>.</summary>
public interface IWeaponBuildData : IXAssetBuildData
{
    WeaponVariantDef Variant { get; }
    WeaponDef Definition { get; }
    WeaponReferenceBuildData References { get; }
    WeaponLinkerProvenance LinkerProvenance => WeaponLinkerProvenance.Empty;
}

/// <summary>Emits one body from only immutable build data and pass-one addresses.</summary>
public interface IXAssetBodyEmitter
{
    XAssetType AssetType { get; }
    IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null);
    AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null);
}

/// <summary>Body bytes are per block and root is the pointer target for its XAsset row.</summary>
public sealed class AssetBodyEmission
{
    private readonly IReadOnlyList<EmissionBlockSegment> _segments;
    private readonly IReadOnlyList<EmissionBlockSegment> _sourceSegments;

    public AssetBodyEmission(
        XAssetType assetType,
        EmissionAddress rootAddress,
        IEnumerable<EmissionBlockSegment> segments,
        IEnumerable<EmissionBlockSegment>? sourceSegments = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        AssetType = assetType;
        RootAddress = rootAddress;
        EmissionBlockSegment[] copied = segments.Select(segment => segment.Copy()).ToArray();
        // TEMP is scoped scratch storage. Distinct source operations may
        // intentionally materialize at the same TEMP address after a pop, so
        // an address is not a globally unique segment identity.  Existing
        // emitters append their owning root after its children; choose that
        // last matching segment for the default traversal order.
        int rootIndex = Array.FindLastIndex(copied, segment => segment.Address == rootAddress);
        if (rootIndex < 0)
            throw new InvalidDataException("An asset-body emission must contain its root segment.");
        _segments = Array.AsReadOnly(copied);

        EmissionBlockSegment[] ordered = sourceSegments is null
            ? [copied[rootIndex], .. copied
                .Where((_, index) => index != rootIndex)
                .OrderBy(segment => segment.Address.Block)
                .ThenBy(segment => segment.Address.Offset)]
            : sourceSegments.Select(segment => segment.Copy()).ToArray();
        if (ordered.Length != copied.Length ||
            !HaveSameSegments(ordered, copied) ||
            ordered[0].Address != rootAddress)
        {
            throw new InvalidDataException("Asset-body source order must contain every segment exactly once and begin with its root.");
        }
        _sourceSegments = Array.AsReadOnly(ordered);
    }

    public XAssetType AssetType { get; }
    public EmissionAddress RootAddress { get; }
    public IReadOnlyList<EmissionBlockSegment> Segments => _segments;

    /// <summary>Exact serialized source order.  It may move between blocks
    /// as nested loaders push/pop their own streams, so address sorting is
    /// not a substitute for this sequence.</summary>
    public IReadOnlyList<EmissionBlockSegment> SourceSegments => _sourceSegments;

    private static bool HaveSameSegments(IEnumerable<EmissionBlockSegment> left, IEnumerable<EmissionBlockSegment> right) =>
        left.GroupBy(SegmentIdentity.Create).OrderBy(group => group.Key)
            .Select(group => (group.Key, Count: group.Count()))
            .SequenceEqual(right.GroupBy(SegmentIdentity.Create).OrderBy(group => group.Key)
                .Select(group => (group.Key, Count: group.Count())));

    private readonly record struct SegmentIdentity(XFileBlockType Block, int Offset, string Bytes)
        : IComparable<SegmentIdentity>
    {
        public static SegmentIdentity Create(EmissionBlockSegment segment) =>
            new(segment.Address.Block, segment.Address.Offset, Convert.ToHexString(segment.Bytes.Span));

        public int CompareTo(SegmentIdentity other)
        {
            int block = Block.CompareTo(other.Block);
            if (block != 0) return block;
            int offset = Offset.CompareTo(other.Offset);
            return offset != 0 ? offset : StringComparer.Ordinal.Compare(Bytes, other.Bytes);
        }
    }
}

public sealed class EmissionBlockSegment
{
    private readonly byte[] _bytes;

    public EmissionBlockSegment(EmissionAddress address, ReadOnlySpan<byte> bytes)
    {
        Address = address;
        _bytes = bytes.ToArray();
    }

    public EmissionAddress Address { get; }
    public ReadOnlyMemory<byte> Bytes => _bytes;
    internal EmissionBlockSegment Copy() => new(Address, _bytes);
}
