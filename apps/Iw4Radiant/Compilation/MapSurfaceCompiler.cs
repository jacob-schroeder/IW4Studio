using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.Techset;
using IW4.Game.Assets.TechniqueSet;
using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Compilation;

internal static class MapSurfaceCompiler
{
    internal static void ValidateTerrain(MapTerrain terrain)
    {
        if (terrain.IsCurve)
        {
            ValidateCurveDimensions(terrain);
            ValidateGrid(terrain, requireArea: false);
            terrain = CompiledTerrain(terrain);
        }
        ValidateGrid(terrain, requireArea: true);
    }

    internal static MapTerrain CompiledTerrain(MapTerrain terrain)
    {
        if (!terrain.IsCurve) return terrain;
        ValidateCurveDimensions(terrain);
        return PatchGeometry.Evaluate(terrain);
    }

    private static void ValidateCurveDimensions(MapTerrain terrain)
    {
        if (terrain.Width is < 3 or > 15 || terrain.Height is < 3 or > 15 ||
            terrain.Width % 2 == 0 || terrain.Height % 2 == 0)
            throw new NotSupportedException($"Curve '{terrain.Material}' needs an odd 3–15 control grid in each direction.");
    }

    private static void ValidateGrid(MapTerrain terrain, bool requireArea)
    {
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
        if (requireArea && !terrain.GetTriangles().Any(triangle => Vector3.Cross(terrain.Vertices[triangle.B] - terrain.Vertices[triangle.A],
                terrain.Vertices[triangle.C] - terrain.Vertices[triangle.A]).LengthSquared() > 0.00000001f))
            throw new InvalidDataException($"Mesh '{terrain.Material}' has no visible triangles.");
    }

    internal static MapRenderSurface[] Compile(MapDocument document, IReadOnlyDictionary<string, MaterialSource> materials,
        MapStageLighting stages)
    {
        var shore = new WaterShoreGeometry(document, name => materials.GetValueOrDefault(name));
        var tints = CompileColorTints(document, materials);
        var surfaces = CompileEntity(document.World, materials, tints, shore, stages).ToList();
        int index = 0;
        foreach (MapEntity entity in MapCompiler.BrushEntities(document))
        {
            index++;
            if (entity.ClassName != "script_brushmodel") continue;
            int first = surfaces.Count;
            surfaces.AddRange(CompileEntity(entity, materials, tints, null, stages).Select(surface => surface with
            {
                ModelIndex = index, SourceIndex = surface.SourceIndex + first
            }));
        }
        return surfaces.ToArray();
    }

    private static MapRenderSurface[] CompileEntity(MapEntity entity, IReadOnlyDictionary<string, MaterialSource> materials,
        IReadOnlyDictionary<string, Vector3> tints, WaterShoreGeometry? shore, MapStageLighting stages)
    {
        var surfaces = new List<MapRenderSurface>();
        foreach (var (brush, contributesToLightGrid) in entity.Brushes.Where(brush => !BrushGlass.IsGlass(brush))
                     .Select(brush => (brush, BrushLighting.Read(brush))))
        foreach (MapPolygon boundary in brush.GetPolygons())
        {
            if (ClipBrushMaterial.IsCollisionOnly(boundary.Face.Material) || CaulkMaterial.IsCaulk(boundary.Face.Material)) continue;
            MaterialSource material = materials[boundary.Face.Material];
            if (!OceanSurfaceGeometry.IsVisibleSurface(boundary, material.IsWater)) continue;
            Vector3 boundaryCenter = boundary.Vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) /
                boundary.Vertices.Length;
            byte sunIndex = stages.SunIndexAt(boundaryCenter);
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
                        return TintColor(color, tints.GetValueOrDefault(polygon.Face.Material, Vector3.One));
                    }).ToArray(), surfaces.Count)
                {
                    Displacement = ocean is not null && OceanSurfaceGeometry.IsTop(boundary) ? ocean.Height : 0,
                    ContributesToLightGrid = contributesToLightGrid,
                    ReflectionCenter = boundaryCenter,
                    SunPrimaryLightIndex = sunIndex
                });
            }
        }

        // PatchGeometry uses the editor's eight samples per quadratic span. Keep
        // the control grid untouched; render and collision consume the same policy.
        MapTerrain[] meshes = entity.Terrains.Select(CompiledTerrain).ToArray();
        var meshIndices = meshes.Select((terrain, index) => (terrain, index: index + surfaces.Count))
            .ToDictionary(pair => pair.terrain, pair => pair.index);
        var terrainNormals = new TerrainNormals(meshes);
        foreach (MapTerrain terrain in meshes)
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
            Vector3[] normals = indices.Select(index => terrainNormals.At(terrain, index, normal)).ToArray();
            surfaces.Add(new(terrain.Material, indices.Select(index => terrain.Vertices[index]).ToArray(), normal, normals,
                normals.Select(n => Unit(tangent - n * Vector3.Dot(tangent, n))).ToArray(),
                normals.Select(n => Unit(binormal - n * Vector3.Dot(binormal, n))).ToArray(),
                indices.Select(index => terrain.TextureCoordinates[index]).ToArray(),
                indices.Select(index => TintColor(terrain.Colors[index],
                    tints.GetValueOrDefault(terrain.Material, Vector3.One))).ToArray(), meshIndices[terrain])
            {
                // MapTerrain.LightmapSize is authored world-units-per-secondary-luxel. Preserve it
                // on every terrain render triangle; brush surfaces intentionally leave this null.
                LightmapSize = terrain.LightmapSize,
                SunPrimaryLightIndex = stages.SunIndexAt((terrain.Vertices[a] + terrain.Vertices[b] + terrain.Vertices[c]) / 3)
            });
        }
        return surfaces.ToArray();
    }

    private static Dictionary<string, Vector3> CompileColorTints(MapDocument document,
        IReadOnlyDictionary<string, MaterialSource> materials)
    {
        var tints = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        var nativeTint = new Dictionary<(string Root, string TechniqueSet), bool>();
        var names = document.Brushes.Where(brush => !BrushGlass.IsGlass(brush))
            .SelectMany(brush => brush.Faces).Select(face => face.Material)
            .Concat(document.Terrains.Select(terrain => terrain.Material)).Distinct(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!materials.TryGetValue(name, out MaterialSource? material) || material.IsSky || material.IsWater ||
                !material.UsesVertexColor || material.ColorTint == Vector3.One) continue;
            string root = material.SourceDirectory ?? throw new InvalidDataException(
                $"Material '{name}' needs its source technique set to compile colorTint.");
            var key = (root, material.TechniqueSet);
            if (!nativeTint.TryGetValue(key, out bool bindsTint))
            {
                var techset = new TechsetExchange().Link(root, material.TechniqueSet);
                // Native material-name hash for colorTint. A bound constant already
                // reaches the shader; multiplying the vertices would apply it twice.
                bindsTint = techset.TechniqueSlots.SelectMany(slot => slot.Technique?.Passes ?? [])
                    .SelectMany(pass => pass.Args).Any(argument =>
                        argument.Type is MaterialShaderArgumentType.MaterialVertexConst or MaterialShaderArgumentType.MaterialPixelConst &&
                        argument.MaterialNameHash == 0xb60c3b3a);
                nativeTint.Add(key, bindsTint);
            }
            if (bindsTint) continue;
            Vector3 tint = material.ColorTint;
            if (!BrushGeometry.IsFinite(tint) || tint.X < 0 || tint.X > 1 || tint.Y < 0 || tint.Y > 1 || tint.Z < 0 || tint.Z > 1)
                throw new NotSupportedException($"Material '{name}' colorTint must be between 0 and 1 for native vertex colors.");
            tints.Add(name, tint);
        }
        return tints;
    }

    // Native lm/lm_sun shaders multiply colorMap by Color0 before squaring RGB
    // for lighting. Keep tint in the authored domain and preserve terrain alpha.
    private static Vector4 TintColor(Vector4 color, Vector3 tint) =>
        new(color.X * tint.X, color.Y * tint.Y, color.Z * tint.Z, color.W);

    private static Vector3 Unit(Vector3 vector) => BrushGeometry.IsFinite(vector) && vector.LengthSquared() > 0.00000001f
        ? Vector3.Normalize(vector)
        : throw new InvalidDataException("Terrain has opposing normals or a degenerate tangent. Split the mesh at the hard edge.");
}
