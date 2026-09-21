using IW4.AssetExchange.SourceFormat.Material;
using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Compilation;

internal static class MapSurfaceCompiler
{
    internal static void ValidateTerrain(MapTerrain terrain)
    {
        if (terrain.IsCurve)
            throw new NotSupportedException("Curve control points are not compiled yet. Use a terrain mesh for this build.");
        long count = (long)terrain.Width * terrain.Height;
        if (terrain.Width < 2 || terrain.Height < 2 || count != terrain.Vertices.Length ||
            count != terrain.TextureCoordinates.Length || count != terrain.LightmapCoordinates.Length ||
            count != terrain.Colors.Length || count != terrain.EdgeFlags.Length)
            throw new InvalidDataException($"Terrain '{terrain.Material}' has an incomplete vertex grid.");
        if (terrain.Lightmap != "lightmap_gray" || !float.IsFinite(terrain.LightmapSize) || terrain.LightmapSize <= 0 || terrain.Subdivision < 1)
            throw new NotSupportedException($"Terrain '{terrain.Material}' requires a standard lightmap_gray projection.");
        if (terrain.Smoothing is not (null or "smoothing_smooth" or "smoothing_hard"))
            throw new NotSupportedException($"Terrain smoothing '{terrain.Smoothing}' is not supported. Use smoothing_smooth or smoothing_hard.");
        for (int index = 0; index < terrain.Vertices.Length; index++)
        {
            Vector2 uv = terrain.TextureCoordinates[index], lightmap = terrain.LightmapCoordinates[index];
            Vector4 color = terrain.Colors[index];
            if (!BrushGeometry.IsFinite(terrain.Vertices[index]) || !float.IsFinite(uv.X) || !float.IsFinite(uv.Y) ||
                !float.IsFinite(lightmap.X) || !float.IsFinite(lightmap.Y) ||
                !float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z) || !float.IsFinite(color.W) ||
                color.X < 0 || color.X > 1 || color.Y < 0 || color.Y > 1 || color.Z < 0 || color.Z > 1 || color.W < 0 || color.W > 1)
                throw new InvalidDataException($"Terrain '{terrain.Material}' has invalid vertex coordinates or color values.");
            if (terrain.EdgeFlags[index] is not (0 or 1))
                throw new NotSupportedException($"Terrain '{terrain.Material}' contains unsupported triangle flags.");
        }
        if (!terrain.GetTriangles().Any(triangle => Vector3.Cross(terrain.Vertices[triangle.B] - terrain.Vertices[triangle.A],
                terrain.Vertices[triangle.C] - terrain.Vertices[triangle.A]).LengthSquared() > 0.00000001f))
            throw new InvalidDataException($"Terrain '{terrain.Material}' has no visible triangles.");
    }

    internal static MapRenderSurface[] Compile(MapDocument document, IReadOnlyDictionary<string, MaterialSource> materials)
    {
        var shore = new WaterShoreGeometry(document, name => materials.GetValueOrDefault(name));
        var surfaces = CompileEntity(document.World, materials, shore).ToList();
        int index = 0;
        foreach (MapEntity entity in MapCompiler.BrushEntities(document))
        {
            index++;
            if (entity.ClassName != "script_brushmodel") continue;
            int first = surfaces.Count;
            surfaces.AddRange(CompileEntity(entity, materials, null).Select(surface => surface with
            {
                ModelIndex = index, SourceIndex = surface.SourceIndex + first
            }));
        }
        return surfaces.ToArray();
    }

    private static MapRenderSurface[] CompileEntity(MapEntity entity, IReadOnlyDictionary<string, MaterialSource> materials,
        WaterShoreGeometry? shore)
    {
        var surfaces = new List<MapRenderSurface>();
        foreach (MapPolygon boundary in entity.Brushes.SelectMany(brush => brush.GetPolygons()))
        {
            if (ClipBrushMaterial.IsPlayerClip(boundary.Face.Material) || CaulkMaterial.IsCaulk(boundary.Face.Material)) continue;
            MaterialSource material = materials[boundary.Face.Material];
            if (!OceanSurfaceGeometry.IsVisibleSurface(boundary, material.IsWater)) continue;
            OceanWaveSettings? ocean = material.Ocean;
            var contacts = material.IsWater ? shore?.Contacts(boundary) ?? [] : [];
            foreach (MapPolygon polygon in OceanSurfaceGeometry.Subdivide(boundary, ocean, contacts, shore))
            {
                Vector3 normal = polygon.Face.Normal;
                var mapping = SurfaceProjection.Parse(polygon.Face.Projection).GetMapping(normal);
                Vector3 projectedU = mapping.U - normal * Vector3.Dot(normal, mapping.U);
                Vector3 projectedV = mapping.V - normal * Vector3.Dot(normal, mapping.V);
                Vector3 alongU = Vector3.Cross(projectedV, normal);
                float determinant = Vector3.Dot(projectedU, alongU);
                if (!float.IsFinite(determinant) || determinant == 0)
                    throw new InvalidDataException("A brush face has a degenerate texture basis.");
                Vector3 tangent = Vector3.Normalize(alongU / determinant);
                Vector3 binormal = Vector3.Normalize(Vector3.Cross(normal, projectedU) / determinant);
                int count = polygon.Vertices.Length;
                Vector2[] uv = polygon.Vertices.Select(point => new Vector2(
                    (float)(BrushGeometry.Dot(mapping.U, point) + mapping.Offset.X),
                    (float)(BrushGeometry.Dot(mapping.V, point) + mapping.Offset.Y))).ToArray();
                surfaces.Add(new(polygon.Face.Material, polygon.Vertices, normal,
                    Enumerable.Repeat(normal, count).ToArray(), Enumerable.Repeat(tangent, count).ToArray(),
                    Enumerable.Repeat(binormal, count).ToArray(), uv, polygon.Vertices.Select(point =>
                    {
                        Vector4 color = ocean is null ? Vector4.One : OceanSurfaceGeometry.VertexColor(boundary, ocean, point, shore);
                        if (material.IsWater && ocean is null) color.W = WaterShoreGeometry.VertexAlpha(point, contacts);
                        return color;
                    }).ToArray(), surfaces.Count)
                {
                    Displacement = ocean is not null && OceanSurfaceGeometry.IsTop(boundary) ? ocean.Height : 0,
                    ReflectionCenter = boundary.Vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) / boundary.Vertices.Length
                });
            }
        }

        var meshIndices = entity.Terrains.Select((terrain, index) => (terrain, index: index + surfaces.Count))
            .ToDictionary(pair => pair.terrain, pair => pair.index);
        // Equal positions on adjoining patches share a normal. Materials/smoothing groups stay independent.
        var smoothNormals = new Dictionary<(Vector3 Position, string Material, string Smoothing), Vector3>();
        foreach (MapTerrain terrain in entity.Terrains)
        foreach (var (a, b, c) in terrain.GetTriangles())
        {
            Vector3 normal = Vector3.Cross(terrain.Vertices[b] - terrain.Vertices[a], terrain.Vertices[c] - terrain.Vertices[a]);
            if (normal.LengthSquared() <= 0.00000001f || terrain.Smoothing == "smoothing_hard") continue;
            foreach (int vertex in new[] { a, b, c })
            {
                var key = (terrain.Vertices[vertex], terrain.Material, terrain.Smoothing ?? "smoothing_smooth");
                smoothNormals[key] = smoothNormals.GetValueOrDefault(key) + normal;
            }
        }
        foreach (MapTerrain terrain in entity.Terrains)
        foreach (var (a, b, c) in terrain.GetTriangles())
        {
            Vector3 edgeU = terrain.Vertices[b] - terrain.Vertices[a], edgeV = terrain.Vertices[c] - terrain.Vertices[a];
            Vector3 cross = Vector3.Cross(edgeU, edgeV);
            // Native decal meshes use a repeated corner to encode a single triangle in a 2x2 grid.
            if (cross.LengthSquared() <= 0.00000001f) continue;
            Vector3 normal = Vector3.Normalize(cross);
            int[] indices = [a, b, c];
            Vector2 uvU = terrain.TextureCoordinates[b] - terrain.TextureCoordinates[a];
            Vector2 uvV = terrain.TextureCoordinates[c] - terrain.TextureCoordinates[a];
            float determinant = uvU.X * uvV.Y - uvU.Y * uvV.X;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 0.00000001f)
                throw new InvalidDataException($"Terrain '{terrain.Material}' has a degenerate texture projection. Give the triangle distinct texture coordinates.");
            Vector3 tangent = (edgeU * uvV.Y - edgeV * uvU.Y) / determinant;
            Vector3 binormal = (edgeV * uvU.X - edgeU * uvV.X) / determinant;
            Vector3[] normals = indices.Select(index => terrain.Smoothing == "smoothing_hard" ? normal :
                Unit(smoothNormals[(terrain.Vertices[index], terrain.Material, terrain.Smoothing ?? "smoothing_smooth")])).ToArray();
            surfaces.Add(new(terrain.Material, indices.Select(index => terrain.Vertices[index]).ToArray(), normal, normals,
                normals.Select(n => Unit(tangent - n * Vector3.Dot(tangent, n))).ToArray(),
                normals.Select(n => Unit(binormal - n * Vector3.Dot(binormal, n))).ToArray(),
                indices.Select(index => terrain.TextureCoordinates[index]).ToArray(),
                indices.Select(index => terrain.Colors[index]).ToArray(), meshIndices[terrain])
            {
                // MapTerrain.LightmapSize is authored world-units-per-secondary-luxel. Preserve it
                // on every terrain render triangle; brush surfaces intentionally leave this null.
                LightmapSize = terrain.LightmapSize
            });
        }
        return surfaces.ToArray();
    }

    private static Vector3 Unit(Vector3 vector) => BrushGeometry.IsFinite(vector) && vector.LengthSquared() > 0.00000001f
        ? Vector3.Normalize(vector)
        : throw new InvalidDataException("Terrain has opposing normals or a degenerate tangent. Split the mesh at the hard edge.");
}
