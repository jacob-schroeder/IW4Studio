using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XModel;

public sealed class XModelLodInfo
{
    private ushort _numSurfs;
    private ushort _surfIndex;
    private IReadOnlyList<uint> _partBits = [];
    private XPointer<byte[]> _surfsRuntimePointer;

    public const int SerializedSize = 0x28;

    public float Dist { get; init; }
    public ushort NumSurfs
    {
        get => _numSurfs;
        init => _numSurfs = value;
    }
    // Original wire scalars retained before DB canonicalization mutates the
    // runtime-facing LOD fields. Null identifies authored definitions.
    public ushort? SerializedNumSurfs { get; init; }

    public ushort SurfIndex
    {
        get => _surfIndex;
        init => _surfIndex = value;
    }
    public ushort? SerializedSurfIndex { get; init; }

    public XPointer<XModelSurfsAsset> ModelSurfsPointer { get; init; }
    public IReadOnlyList<uint> PartBits
    {
        get => _partBits;
        init => _partBits = value;
    }
    public IReadOnlyList<uint>? SerializedPartBits { get; init; }

    public XPointer<byte[]> SurfsRuntimePointer
    {
        get => _surfsRuntimePointer;
        init => _surfsRuntimePointer = value;
    }

    public XModelSurfsAsset? ModelSurfs { get; init; }

    internal void ApplyCanonicalSurfaceFixup(
        ushort numSurfs,
        ushort surfIndex,
        IReadOnlyList<uint> partBits,
        XPointer<byte[]> surfsRuntimePointer)
    {
        _numSurfs = numSurfs;
        _surfIndex = surfIndex;
        _partBits = partBits;
        _surfsRuntimePointer = surfsRuntimePointer;
    }
}
