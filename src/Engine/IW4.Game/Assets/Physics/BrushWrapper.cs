using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Physics;

public sealed class BrushWrapper
{
    public const int SerializedSize = 0x44;

    public Bounds Bounds { get; init; } = new();
    public CBrush Brush { get; init; } = new();
    public int TotalEdgeCount { get; init; }
    public XPointer<CPlane[]> PlanesPointer { get; init; }
    public IReadOnlyList<CPlane> Planes { get; init; } = [];
}
