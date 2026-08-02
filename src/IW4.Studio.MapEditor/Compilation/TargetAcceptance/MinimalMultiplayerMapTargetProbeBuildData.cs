using System.Text;
using IW4.Assets.Assets.ColMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Immutable MapEnts body for the bounded M7 target probe. The payload owns
/// exactly one worldspawn, one deathmatch spawn, and one intermission camera.
/// It deliberately owns no brush-model selectors or triggers. The single
/// stage row is the native map baseline shared by retail mp_terminal and the
/// IW3-to-IW4 map converter.
/// </summary>
internal sealed class MinimalMultiplayerMapProbeMapEntsBuildData :
    IMapEntsBuildData
{
    private const string EntitySource =
        "{\n" +
        "\"classname\" \"worldspawn\"\n" +
        "}\n" +
        "{\n" +
        "\"classname\" \"mp_dm_spawn\"\n" +
        "\"origin\" \"0 0 64\"\n" +
        "\"angles\" \"0 0 0\"\n" +
        "}\n" +
        "{\n" +
        "\"classname\" \"mp_global_intermission\"\n" +
        "\"origin\" \"0 -192 128\"\n" +
        "\"angles\" \"15 90 0\"\n" +
        "}\n" +
        "\0";

    private readonly byte[] _entityStringBytes;
    private readonly byte[] _tailPad = [0, 0, 0];

    internal MinimalMultiplayerMapProbeMapEntsBuildData(string name)
    {
        Name = MapCompilerContentIdentityInput
            .NormalizeMultiplayerMapAssetName(name);
        _entityStringBytes = Encoding.Latin1.GetBytes(EntitySource);
        Triggers = new MapTriggersBuildData([], [], []);
        Stages =
        [
            new StageBuildData(
                "stage 0",
                new Float3BuildData(0, 0, 0),
                TriggerIndex: 0x0400,
                SunPrimaryLightIndex: 1,
                Pad13: 0)
        ];
    }

    public XAssetType AssetType => XAssetType.MapEnts;

    public string Name { get; }

    public MapTriggersBuildData Triggers { get; }

    public IReadOnlyList<StageBuildData> Stages { get; }

    public byte[] GetEntityStringBytesCopy() =>
        _entityStringBytes.ToArray();

    public byte[] GetPad29To2BCopy() => _tailPad.ToArray();
}

/// <summary>
/// ColMap adapter that changes only the nested MapEnts ownership edge. The
/// collision definition remains the validated M4 projection while the source
/// pointer uses the insert form observed on the retail mp_terminal ColMap.
/// </summary>
internal sealed class MinimalMultiplayerMapProbeCollisionBuildData :
    IClipMapBuildData
{
    internal MinimalMultiplayerMapProbeCollisionBuildData(
        ClipMapAsset definition,
        IMapEntsBuildData mapEnts)
    {
        Definition = definition ??
            throw new ArgumentNullException(nameof(definition));
        ArgumentNullException.ThrowIfNull(mapEnts);
        if (definition.SerializedType != XAssetType.ColMapMp)
        {
            throw new ArgumentException(
                "The minimal multiplayer probe requires a ColMapMp root.",
                nameof(definition));
        }

        var reference = new SymbolicXAssetReference(
            XAssetType.MapEnts,
            $",{mapEnts.Name}");
        References = new ClipMapReferenceBuildData(
            staticModels: [],
            dynamicEntities:
            [
                Array.Empty<ClipMapDynEntityReferenceBuildData>(),
                Array.Empty<ClipMapDynEntityReferenceBuildData>()
            ],
            mapEnts: reference,
            staticModelLinks: [],
            mapEntsLink: new NestedXAssetBuildLink(
                reference,
                NestedXAssetPointerSourceForm.Insert,
                mapEnts));
    }

    public XAssetType AssetType => XAssetType.ColMapMp;

    public XAssetType SerializedType => XAssetType.ColMapMp;

    public ClipMapAsset Definition { get; }

    public ClipMapReferenceBuildData References { get; }

    public ClipMapLinkerProvenance LinkerProvenance =>
        ClipMapLinkerProvenance.Empty;
}
