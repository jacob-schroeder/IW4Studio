using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Assets.XModel;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Render.Geometry;

internal sealed class InstancedSolidBatchBuilder(
    List<float> vertices,
    List<uint> indices,
    RenderBounds localBounds,
    int skippedTriangles,
    int readFailureTriangles)
{
    public List<float> Vertices { get; } = vertices;
    public List<uint> Indices { get; } = indices;
    public RenderBounds LocalBounds { get; } = localBounds;
    public int SkippedTriangles { get; } = skippedTriangles;
    public int ReadFailureTriangles { get; } = readFailureTriangles;
    public List<MapRenderStaticModelInstance> Instances { get; } = [];
}
