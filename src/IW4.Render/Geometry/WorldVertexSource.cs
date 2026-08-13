using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Geometry;

internal readonly record struct WorldVertexSource(
    byte StreamIndex,
    byte ByteOffset,
    byte ComponentCount,
    RsxVertexElementType RsxType)
{
    internal bool IsUnavailableSourceTuple =>
        StreamIndex == 2 && ByteOffset == 0 && ComponentCount == 0 && RsxType == 0;
}
