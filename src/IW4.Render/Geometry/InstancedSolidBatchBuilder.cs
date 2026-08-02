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

namespace IW4.Render.Geometry;

internal sealed class InstancedSolidBatchBuilder(
    List<float> vertices,
    List<uint> indices,
    MapRenderBounds localBounds,
    int skippedTriangles,
    int readFailureTriangles)
{
    public List<float> Vertices { get; } = vertices;
    public List<uint> Indices { get; } = indices;
    public MapRenderBounds LocalBounds { get; } = localBounds;
    public int SkippedTriangles { get; } = skippedTriangles;
    public int ReadFailureTriangles { get; } = readFailureTriangles;
    public List<MapRenderStaticModelInstance> Instances { get; } = [];
}
