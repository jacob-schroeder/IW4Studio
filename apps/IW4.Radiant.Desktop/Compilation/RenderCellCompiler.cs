using System.Numerics;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Math;

namespace IW4.Radiant.Desktop.Compilation;

internal static class RenderCellCompiler
{
    // The PS3 opaque camera list reserves 8192 words, four for termination,
    // and a run with a distinct vertex base needs at least two words.
    private const int OpaqueGroupCapacity = (8192 - 4) / 2;

    internal static (GfxWorldDpvsPlanes Planes, GfxCell[] Cells, GfxCellTree[] Trees, ushort[] SortedSurfaces)
        Compile(Bounds world, GfxSurfaceBounds[] surfaces, ushort[] ids, byte[] probes, int unbatchedWorldSurfaces)
    {
        if (unbatchedWorldSurfaces > OpaqueGroupCapacity)
        {
            // Split along the longer horizontal axis. Keep boundary-crossing
            // surfaces in one shared span between the two exclusive spans.
            bool alongX = world.HalfSize.X >= world.HalfSize.Y;
            Vector3 n = alongX ? Vector3.UnitX : Vector3.UnitY;
            Vector3 across = Vector3.Cross(Vector3.UnitZ, n);
            Vector3 center = Vector(world.MidPoint);
            Vector3 extent = Vector(world.HalfSize);
            float split = Vector3.Dot(n, center);
            float width = MathF.Abs(Vector3.Dot(across, extent));
            float depth = Vector3.Dot(n, extent);
            var low = new List<ushort>();
            var shared = new List<ushort>();
            var high = new List<ushort>();
            foreach (ushort id in ids)
            {
                Bounds box = surfaces[id].Bounds;
                float offset = Vector3.Dot(n, Vector(box.MidPoint)) - split;
                float radius = Vector3.Dot(n, Vector(box.HalfSize));
                if (offset < -radius) low.Add(id);
                else if (offset > radius) high.Add(id);
                else shared.Add(id);
            }

            if (low.Count > 0 && high.Count > 0 && depth > 0 && width > 0 && extent.Z > 0 &&
                Math.Max(low.Count, high.Count) + shared.Count <= GfxAabbTree.MaxIndexCount)
            {
                Vector3 halfDepth = n * (depth * 0.5f);
                Bounds HalfBounds(Vector3 midpoint) => new() { MidPoint = Vec(midpoint), HalfSize = Vec(extent - halfDepth) };
                Bounds lowBounds = HalfBounds(center - halfDepth), highBounds = HalfBounds(center + halfDepth);
                Vector3 side = across * width, up = Vector3.UnitZ * extent.Z;
                // Winding cross is opposite the outgoing portal plane normal.
                Vector3[] vertices = [center - side - up, center - side + up, center + side + up, center + side - up];
                GfxPortal Portal(int destination, Vector3 normal, Vector3 u, IEnumerable<Vector3> winding) => new()
                {
                    Plane = new GfxPortalPlane(normal.X, normal.Y, normal.Z, -Vector3.Dot(normal, center)),
                    CellIndex = (ushort)destination, VertexCount = 4,
                    Vertices = winding.Select(v => new GfxPortalVertex(v.X, v.Y, v.Z)).ToArray(),
                    HullAxis = [u.X, u.Y, u.Z, 0, 0, 1]
                };

                // Root bounds cover every shared draw, including geometry extending
                // beyond the volume used to locate the camera.
                Bounds RootBounds(Bounds half)
                {
                    Vector3 min = Vector(half.MidPoint) - Vector(half.HalfSize);
                    Vector3 max = Vector(half.MidPoint) + Vector(half.HalfSize);
                    foreach (ushort id in shared)
                    {
                        Bounds box = surfaces[id].Bounds;
                        min = Vector3.Min(min, Vector(box.MidPoint) - Vector(box.HalfSize));
                        max = Vector3.Max(max, Vector(box.MidPoint) + Vector(box.HalfSize));
                    }
                    return new Bounds { MidPoint = Vec(min * 0.5f + max * 0.5f), HalfSize = Vec(max * 0.5f - min * 0.5f) };
                }

                return (new GfxWorldDpvsPlanes
                    {
                        CellCount = 2, Planes = [new DpvsPlane(n.X, n.Y, n.Z, split, alongX ? (byte)0 : (byte)1, 0, 0)],
                        // Positive child at +2 is cell 1; nonpositive child at +3 is cell 0.
                        Nodes = [3, 3, 2, 1]
                    },
                    [Cell(lowBounds, [Portal(1, n, across, vertices)]), Cell(highBounds, [Portal(0, -n, -across, vertices.Reverse())])],
                    [Tree(RootBounds(lowBounds), 0, low.Count + shared.Count), Tree(RootBounds(highBounds), low.Count, shared.Count + high.Count)],
                    [.. low, .. shared, .. high]);
            }
        }

        return (new GfxWorldDpvsPlanes { CellCount = 1, Nodes = [1] },
            [Cell(world, [])], [Tree(world, 0, ids.Length)], ids);

        GfxCell Cell(Bounds bounds, GfxPortal[] portals) => new()
        {
            Bounds = bounds, Portals = portals, PortalCount = portals.Length,
            ReflectionProbes = probes, ReflectionProbeCount = checked((byte)probes.Length)
        };
    }

    private static GfxCellTree Tree(Bounds bounds, int first, int count) => new()
    {
        AabbTrees = [new GfxAabbTree { Bounds = bounds, StartSurfIndex = checked((ushort)first), SurfaceCount = checked((ushort)count) }]
    };

    private static Vector3 Vector(Vec3 v) => new(v.X, v.Y, v.Z);
    private static Vec3 Vec(Vector3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };
}
