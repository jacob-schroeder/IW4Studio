using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.FxMap;

public sealed class FxGlassSystem
{
    public const int SerializedSize = 0x70;

    public int Offset { get; init; }
    public int Time { get; init; }
    public int PrevTime { get; init; }
    public uint DefCount { get; init; }
    public uint PieceLimit { get; init; }
    public uint PieceWordCount { get; init; }
    public uint InitPieceCount { get; init; }
    public uint CellCount { get; init; }
    public uint ActivePieceCount { get; init; }
    public uint FirstFreePiece { get; init; }
    public uint GeoDataLimit { get; init; }
    public uint GeoDataCount { get; init; }
    public uint InitGeoDataCount { get; init; }
    public XPointer<FxGlassDef[]> DefsPointer { get; init; }
    public IReadOnlyList<FxGlassDef> Defs { get; init; } = [];
    public XPointer<FxGlassPiecePlace[]> PiecePlacesPointer { get; init; }
    public IReadOnlyList<FxGlassPiecePlace> PiecePlaces { get; init; } = [];
    public XPointer<FxGlassPieceState[]> PieceStatesPointer { get; init; }
    public IReadOnlyList<FxGlassPieceState> PieceStates { get; init; } = [];
    public XPointer<FxGlassPieceDynamics[]> PieceDynamicsPointer { get; init; }
    public IReadOnlyList<FxGlassPieceDynamics> PieceDynamics { get; init; } = [];
    public XPointer<FxGlassGeometryData[]> GeoDataPointer { get; init; }
    public IReadOnlyList<FxGlassGeometryData> GeoData { get; init; } = [];
    public XPointer<uint[]> IsInUsePointer { get; init; }
    public IReadOnlyList<uint> IsInUse { get; init; } = [];
    public XPointer<uint[]> CellBitsPointer { get; init; }
    public IReadOnlyList<uint> CellBits { get; init; } = [];
    public XPointer<byte[]> VisDataPointer { get; init; }
    public IReadOnlyList<byte> VisData { get; init; } = [];
    public XPointer<FxVec3[]> LinkOrgPointer { get; init; }
    public IReadOnlyList<FxVec3> LinkOrg { get; init; } = [];
    public XPointer<float[]> HalfThicknessPointer { get; init; }
    public IReadOnlyList<float> HalfThickness { get; init; } = [];
    public XPointer<ushort[]> LightingHandlesPointer { get; init; }
    public IReadOnlyList<ushort> LightingHandles { get; init; } = [];
    public XPointer<FxGlassInitPieceState[]> InitPieceStatesPointer { get; init; }
    public IReadOnlyList<FxGlassInitPieceState> InitPieceStates { get; init; } = [];
    public XPointer<FxGlassGeometryData[]> InitGeoDataPointer { get; init; }
    public IReadOnlyList<FxGlassGeometryData> InitGeoData { get; init; } = [];
    public byte NeedToCompactData { get; init; }
    public byte InitCount { get; init; }
    public ushort Pad66 { get; init; }
    public float EffectChanceAccum { get; init; }
    public int LastPieceDeletionTime { get; init; }
}
