using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxCell
{
    public const int SerializedSize = 0x28;

    // 0x00: Bounds are stored as midpoint[3] followed by halfSize[3].
    public ModelBounds Bounds { get; init; } = new();

    // 0x18
    public int PortalCount { get; init; }

    // 0x1C
    public XPointer<GfxPortal[]> PortalsPointer { get; init; }
    public IReadOnlyList<GfxPortal> Portals { get; init; } = [];

    // 0x20
    public byte ReflectionProbeCount { get; init; }

    // 0x21
    public IReadOnlyList<byte> Pad21 { get; init; } = [];

    // 0x24
    public XPointer<byte[]> ReflectionProbesPointer { get; init; }
    public IReadOnlyList<byte> ReflectionProbes { get; init; } = [];
}
