using IW4.Assets.Codecs.GfxMap;
using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Math;
using Iw4Radiant.Compilation.Lighting;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;
using Bounds = IW4.Assets.Math.Bounds;

namespace Iw4Radiant.Compilation;

internal static class BrushRenderCompiler
{
    internal static ComWorldAsset CompileSun(MapDocument document, string assetName)
    {
        if (!MapSunProperties.TryRead(document.World, out MapSunProperties? source, out string? error))
            throw new InvalidDataException(error);
        if (source is not { } sun)
            throw new InvalidDataException("Compilation requires authored sunlight, suncolor and sundirection worldspawn keys.");
        Vector3 color = sun.Color * sun.Intensity;
        if (!BrushGeometry.IsFinite(color))
            throw new InvalidDataException("The authored sun color and intensity exceed the supported numeric range.");
        return new ComWorldAsset
        {
            Name = assetName,
            IsInUse = 1,
            PrimaryLightCount = 2,
            PrimaryLights =
            [
                new ComPrimaryLight { Type = GfxLightType.None },
                new ComPrimaryLight
                {
                    Type = GfxLightType.Directional,
                    Color = ToVec3(color),
                    Dir = ToVec3(sun.Direction)
                }
            ]
        };
    }

    internal static GfxWorldAsset Compile(
        MapDocument document, string assetName, ClipMapAsset clip, ComWorldAsset com,
        IReadOnlyDictionary<string, MaterialSource> materialSources, IReadOnlyList<Vector3> probeOrigins,
        CancellationToken cancellationToken)
    {
        MapRenderSurface[] polygons = MapSurfaceCompiler.Compile(document);
        if (polygons.Length is 0 or > ushort.MaxValue)
            throw new InvalidDataException("Compilation requires between 1 and 65535 renderable brush faces or mesh triangles.");
        var lightingScene = new BrushLightingScene(document, polygons, materialSources, cancellationToken);
        var (lightmaps, faceUvs, faceLightmapIndices) = BrushLightmapCompiler.BakeLightmaps(lightingScene);
        GfxLightGrid lightGrid = BrushLightGridCompiler.BakeLightGrid(lightingScene);
        var (probeImages, probes) = BrushReflectionCompiler.CaptureProbes(lightingScene, probeOrigins);
        int vertexCount = polygons.Sum(polygon => polygon.Vertices.Length);
        var positions = new byte[checked(vertexCount * WorldVertexCodec.PositionStride)];
        var layers = new byte[checked(vertexCount * WorldVertexCodec.LayerStride)];
        var indices = new List<ushort>();
        var surfaces = new GfxSurface[polygons.Length];
        var surfaceBounds = new GfxSurfaceBounds[polygons.Length];
        var materials = new Dictionary<string, MaterialAsset>(StringComparer.Ordinal);
        foreach (var collisionMaterial in clip.Materials)
        {
            string name = collisionMaterial.Name ?? throw new InvalidDataException("A collision material has no name.");
            if (ClipBrushMaterial.IsPlayerClip(name)) continue;
            materials.Add(name, new MaterialAsset
            {
                Info = new MaterialInfo
                {
                    Name = name,
                    GameFlags = materialSources[name].GameFlags,
                    SortKey = (MaterialSortKey)materialSources[name].Surface.SortKey,
                    SurfaceTypeBits = materialSources[name].SurfaceTypeBits
                }
            });
        }
        int firstVertex = 0;
        for (int faceIndex = 0; faceIndex < polygons.Length; faceIndex++)
        {
            MapRenderSurface polygon = polygons[faceIndex];
            cancellationToken.ThrowIfCancellationRequested();
            bool isSky = materialSources[polygon.Material].IsSky;
            if (!materials.TryGetValue(polygon.Material, out MaterialAsset? material))
                throw new InvalidDataException($"Brush material '{polygon.Material}' is absent from the material table.");
            int localVertexCount = polygon.Vertices.Length;
            if (localVertexCount < 3 || checked((localVertexCount - 2) * 3) > ushort.MaxValue)
                throw new InvalidDataException($"Brush face {faceIndex} exceeds the v22 surface index range.");
            for (int vertexIndex = 0; vertexIndex < localVertexCount; vertexIndex++)
            {
                Vector3 point = polygon.Vertices[vertexIndex];
                Vector2 uv = polygon.TextureCoordinates[vertexIndex];
                Vector4 color = polygon.Colors[vertexIndex];
                int index = checked(firstVertex + vertexIndex);
                WorldVertexCodec.WriteVertex(
                    positions.AsSpan(index * WorldVertexCodec.PositionStride, WorldVertexCodec.PositionStride),
                    layers.AsSpan(index * WorldVertexCodec.LayerStride, WorldVertexCodec.LayerStride),
                    ToVec3(point), ToVec3(polygon.Normals[vertexIndex]), ToVec3(polygon.Tangents[vertexIndex]), ToVec3(polygon.Binormals[vertexIndex]),
                    ColorByte(color.X), ColorByte(color.Y), ColorByte(color.Z), ColorByte(color.W), uv.X, uv.Y, faceUvs[faceIndex][vertexIndex].X, faceUvs[faceIndex][vertexIndex].Y);
            }
            int firstIndex = indices.Count;
            // Native PS3 world triangles wind clockwise relative to their outward normals.
            for (int corner = 1; corner < localVertexCount - 1; corner++)
            {
                indices.Add(0);
                indices.Add(checked((ushort)(corner + 1)));
                indices.Add(checked((ushort)corner));
            }
            surfaces[faceIndex] = new GfxSurface
            {
                Material = material,
                LightmapIndex = faceLightmapIndices[faceIndex],
                ReflectionProbeIndex = isSky ? (byte)0 : NearestProbe(polygon, probes),
                PrimaryLightIndex = isSky ? (byte)0 : (byte)1,
                Flags = lightingScene.CastsSunShadow(faceIndex) ? GfxSurfaceFlags.CastsSunShadow : 0,
                Triangles = new SrfTriangles
                {
                    BaseVertex = firstVertex,
                    VertexLayerData = checked(firstVertex * WorldVertexCodec.LayerStride),
                    VertexCount = checked((ushort)localVertexCount),
                    TriCount = checked((ushort)(localVertexCount - 2)),
                    BaseIndex = firstIndex
                }
            };
            surfaceBounds[faceIndex] = new GfxSurfaceBounds { Bounds = GetBounds(polygon.Vertices) };
            firstVertex = checked(firstVertex + localVertexCount);
        }

        Bounds bounds = GetBounds(document.World.Brushes.SelectMany(brush => brush.GetVertices()).Concat(document.World.Terrains.SelectMany(terrain => terrain.Vertices)));
        ushort[] surfaceIndices = Enumerable.Range(0, surfaces.Length).Select(index => checked((ushort)index)).ToArray();
        uint surfaceCount = checked((uint)surfaces.Length);
        uint surfaceWords = checked(4 * ((surfaceCount + 127) >> 7));
        ushort[] shadowSurfaces = surfaceIndices.Where(index =>
            (surfaces[index].Flags & GfxSurfaceFlags.CastsSunShadow) != 0).ToArray();
        byte[] cellProbes = Enumerable.Range(probes.Count > 1 ? 1 : 0, probes.Count > 1 ? probes.Count - 1 : 1)
            .Select(index => checked((byte)index)).ToArray();
        return new GfxWorldAsset
        {
            Name = assetName,
            BaseName = Path.GetFileNameWithoutExtension(assetName),
            NodeCount = 1,
            SurfaceCount = surfaces.Length,
            SunPrimaryLightIndex = 1,
            PrimaryLightCount = com.PrimaryLightCount,
            DpvsPlanes = new GfxWorldDpvsPlanes { CellCount = 1, Nodes = [1] },
            CellTreeCounts = [new GfxCellTreeCount(1)],
            CellTrees =
            [
                new GfxCellTree
                {
                    AabbTrees = [new GfxAabbTree { Bounds = bounds, SurfaceCount = checked((ushort)surfaces.Length) }]
                }
            ],
            Cells = [new GfxCell { Bounds = bounds, ReflectionProbeCount = checked((byte)cellProbes.Length), ReflectionProbes = cellProbes }],
            WorldDraw = new GfxWorldDraw
            {
                ReflectionProbeCount = checked((uint)probes.Count),
                ReflectionProbeImages = probeImages,
                ReflectionProbeOrigins = probes,
                LightmapCount = lightmaps.Count,
                Lightmaps = lightmaps,
                VertexCount = checked((uint)vertexCount),
                VertexData = new GfxWorldVertexData { PackedVertices = positions },
                VertexLayerDataSize = checked((uint)layers.Length),
                VertexLayerData = new GfxWorldVertexLayerData { PackedLayerData = layers },
                IndexCount = indices.Count,
                Indices = indices
            },
            LightGrid = lightGrid,
            ModelCount = 1,
            Models =
            [
                new GfxBrushModel
                {
                    BoundsMins = [bounds.MidPoint.X, bounds.MidPoint.Y, bounds.MidPoint.Z],
                    BoundsMaxs = [bounds.HalfSize.X, bounds.HalfSize.Y, bounds.HalfSize.Z],
                    WritableMins = [bounds.MidPoint.X, bounds.MidPoint.Y, bounds.MidPoint.Z],
                    WritableMaxs = [bounds.HalfSize.X, bounds.HalfSize.Y, bounds.HalfSize.Z],
                    Radius = new Vector3(bounds.HalfSize.X, bounds.HalfSize.Y, bounds.HalfSize.Z).Length(),
                    SurfaceCount = checked((ushort)surfaces.Length)
                }
            ],
            Mins = [bounds.MidPoint.X, bounds.MidPoint.Y, bounds.MidPoint.Z],
            Maxs = [bounds.HalfSize.X, bounds.HalfSize.Y, bounds.HalfSize.Z],
            Checksum = clip.Checksum,
            ShadowGeom = [new GfxShadowGeometry(), new GfxShadowGeometry
            {
                SurfaceCount = checked((ushort)shadowSurfaces.Length),
                SortedSurfIndex = shadowSurfaces
            }],
            LightRegions = [new GfxLightRegion(), new GfxLightRegion()],
            Dpvs = new GfxWorldDpvsStatic
            {
                StaticSurfaceCount = surfaceCount,
                LitSurfsEnd = surfaceCount,
                VisibilityCounts = [surfaceCount, surfaceCount, surfaceCount, surfaceCount, surfaceCount, surfaceCount, 0, surfaceWords],
                SortedSurfIndex = surfaceIndices,
                Surfaces = surfaces,
                SurfaceBounds = surfaceBounds
            }
        };
    }

    private static byte NearestProbe(MapRenderSurface polygon, IReadOnlyList<GfxReflectionProbe> probes)
    {
        if (probes.Count == 1) return 0;
        Vector3 center = polygon.Vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) / polygon.Vertices.Length;
        int nearest = 1;
        float distance = float.PositiveInfinity;
        for (int index = 1; index < probes.Count; index++)
        {
            var probe = probes[index];
            float candidate = Vector3.DistanceSquared(center, new Vector3(probe.OffsetX, probe.OffsetY, probe.OffsetZ));
            if (candidate < distance) { distance = candidate; nearest = index; }
        }
        return checked((byte)nearest);
    }

    private static Bounds GetBounds(IEnumerable<Vector3> vertices)
    {
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        foreach (Vector3 point in vertices)
        {
            minimum = Vector3.Min(minimum, point);
            maximum = Vector3.Max(maximum, point);
        }
        Vector3 midpoint = minimum * 0.5f + maximum * 0.5f;
        Vector3 halfSize = maximum * 0.5f - minimum * 0.5f;
        return new Bounds { MidPoint = ToVec3(midpoint), HalfSize = ToVec3(halfSize) };
    }

    private static byte ColorByte(float value) => checked((byte)MathF.Round(value * 255));

    private static Vec3 ToVec3(Vector3 value) => new() { X = value.X, Y = value.Y, Z = value.Z };
}
