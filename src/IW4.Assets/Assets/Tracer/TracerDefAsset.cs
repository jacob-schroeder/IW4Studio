using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Tracer;

public sealed class TracerDefAsset : BaseAsset
{
    public const int SerializedSize = 0x70;
    public const int ColorCount = 5;

    // 0x00: XString name.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04: Material pointer.
    public XPointer<MaterialAsset> MaterialPointer { get; init; }
    public MaterialAsset? Material { get; init; }
    public MaterialAsset? MaterialIncomingDefinition { get; init; }

    // 0x08..0x1C: tracer geometry and timing parameters.
    public uint DrawInterval { get; init; }
    public float Speed { get; init; }
    public float BeamLength { get; init; }
    public float BeamWidth { get; init; }
    public float ScrewRadius { get; init; }
    public float ScrewDistance { get; init; }

    // 0x20: five 4-float color rows through the end of the 0x70-byte root.
    public IReadOnlyList<TracerColor> Colors { get; init; } = [];
}
