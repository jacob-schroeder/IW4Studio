using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Geometry;

/// <summary>
/// PS3 static-XSurface routes from backend source-table row 2
/// (MTL_WORLDVERT_TEX_2_NRM_2).
/// </summary>
internal static class StaticXSurfaceVertexLayout
{
    internal const int BackendRow =
        (int)MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_2_NRM_2;
    internal const byte TexCoordSourceIndex = 2;
    internal const byte NormalSourceIndex = 3;
    internal const byte TangentSourceIndex = 4;
    internal const byte PackedDirectionRsxType = 0x06;

    internal static bool TryGetTexCoord(out VertexSource source) =>
        TryGetSource(TexCoordSourceIndex, out source);

    internal static bool TryGetNormal(out VertexSource source) =>
        TryGetSource(NormalSourceIndex, out source);

    internal static bool TryGetTangent(out VertexSource source) =>
        TryGetSource(TangentSourceIndex, out source);

    private static bool TryGetSource(byte sourceIndex, out VertexSource source)
    {
        source = default;
        if (!WorldVertexLayout.TryGetSource(
                BackendRow,
                sourceIndex,
                out WorldVertexSource ps3Source) ||
            !WorldVertexLayout.TryGetStreamStride(
                BackendRow,
                ps3Source.StreamIndex,
                out byte stride))
        {
            return false;
        }

        source = new VertexSource(
            ps3Source.StreamIndex,
            stride,
            ps3Source.ByteOffset,
            ps3Source.ComponentCount,
            ps3Source.RsxType);
        return true;
    }
}
