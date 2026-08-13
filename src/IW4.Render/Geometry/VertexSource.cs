using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using ModelVec3 = IW4.Assets.Math.Vec3;

using IW4.Render.Materials;

namespace IW4.Render.Geometry;

internal readonly record struct VertexSource(
    byte StreamIndex,
    int Stride,
    int Offset,
    byte FormatByte0,
    RsxVertexElementType FormatByte1,
    UvBaseMode BaseMode = UvBaseMode.Engine,
    int ComponentA = 0,
    int ComponentB = 1,
    float ScaleU = 1f,
    float ScaleV = 1f,
    float AddU = 0f,
    float AddV = 0f)
{
    public bool IsDisabledDefaultAttribute => StreamIndex == 2 && Stride == 0 && Offset == 0 && FormatByte0 == 0 && FormatByte1 == 0;

    public int GetOffset(SrfTriangles triangles, int surfaceIndex)
    {
        if (BaseMode == UvBaseMode.Stream0GlobalIndexSourceStride)
            return checked((triangles.BaseVertex + surfaceIndex) * Stride + Offset);

        int streamBase = BaseMode switch
        {
            UvBaseMode.Stream0BaseVertexGfxStride => checked(triangles.BaseVertex * VertexElementDecoder.WorldVertexStride),
            UvBaseMode.Stream0BaseVertexSourceStride => checked(triangles.BaseVertex * Stride),
            UvBaseMode.Stream0LocalIndexOnly => 0,
            UvBaseMode.Stream1VertexLayerData => triangles.VertexLayerData,
            UvBaseMode.Stream1ZeroBase => 0,
            _ => StreamIndex switch
            {
                0 => checked(triangles.BaseVertex * VertexElementDecoder.WorldVertexStride),
                1 => triangles.VertexLayerData,
                _ => -1
            }
        };

        return streamBase < 0
            ? -1
            : checked(streamBase + surfaceIndex * Stride + Offset);
    }

    public Vector2 ApplyTransform(float u, float v)
    {
        return new Vector2(u * ScaleU + AddU, v * ScaleV + AddV);
    }
}
