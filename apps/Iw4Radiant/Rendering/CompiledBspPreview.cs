using System.Numerics;
using IW4.Formats.D3dbsp;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.ComWorld;
using IW4.Game.Codecs.GfxMap;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Rendering;

internal sealed class CompiledBspPreview
{
    internal const int MissingLightmap = -1;
    internal const int UnsupportedLightmap = -2;
    internal const int NoLightmap = 31;
    internal SceneVertex[] Vertices { get; }
    internal Vector2[] LightmapUvs { get; }
    internal IReadOnlyList<byte[]?> DiffuseLightmaps { get; }
    internal IReadOnlyList<byte[]?> SunVisibilityLightmaps { get; }
    internal IReadOnlyList<IReadOnlyList<byte[]>?> ReflectionProbeRgbaMips { get; }
    internal string? ReflectionProbeDataError { get; }
    internal Vector3 SunDirection { get; }
    internal Vector3 SunColorLinear { get; }
    internal (string Material, int Start, int Count, int WireStart, int WireCount)[] Batches { get; }
    internal int[] BatchLightmapIndices { get; }
    internal bool[] BatchHasDirectSun { get; }
    internal byte[] BatchReflectionProbeIndices { get; }
    internal int MissingSurfaceCount { get; }
    internal int UnsupportedSurfaceCount { get; }
    internal int UnlightmappedSurfaceCount { get; }
    internal string? LightmapDataError { get; }
    internal string? SunDataError { get; }
    internal int DirectSunOmittedSurfaceCount { get; }
    internal (Vector3 Min, Vector3 Max) Bounds { get; private set; }
    internal IReadOnlyList<(string Name, Matrix4x4 Transform, XModelSource Source)> Models { get; private set; } = [];
    internal string? ModelDataError { get; private set; }
    internal string? ModelAssetNotice { get; private set; }
    internal IReadOnlyDictionary<string, WaterMaterialDefinition> WaterDefinitions { get; private set; } =
        new Dictionary<string, WaterMaterialDefinition>(StringComparer.Ordinal);
    internal string? WaterDataError { get; private set; }
    private IReadOnlyList<(string Name, Matrix4x4 Transform)> _modelPlacements = [];
    internal IReadOnlyList<string> ModelNames => _modelPlacements.Select(placement => placement.Name)
        .Distinct(StringComparer.Ordinal).ToArray();

    private CompiledBspPreview(SceneVertex[] vertices, Vector2[] lightmapUvs,
        IReadOnlyList<byte[]?> diffuseLightmaps, IReadOnlyList<byte[]?> sunVisibilityLightmaps,
        IReadOnlyList<IReadOnlyList<byte[]>?> reflectionProbeRgbaMips, string? reflectionProbeDataError,
        Vector3 sunDirection, Vector3 sunColorLinear,
        (string Material, int Start, int Count, int WireStart, int WireCount)[] batches,
        int[] batchLightmapIndices, bool[] batchHasDirectSun, byte[] batchReflectionProbeIndices,
        int missingSurfaceCount, int unsupportedSurfaceCount,
        int unlightmappedSurfaceCount, int directSunOmittedSurfaceCount,
        string? lightmapDataError, string? sunDataError,
        (Vector3 Min, Vector3 Max) bounds)
    {
        Vertices = vertices;
        LightmapUvs = lightmapUvs;
        DiffuseLightmaps = diffuseLightmaps;
        SunVisibilityLightmaps = sunVisibilityLightmaps;
        ReflectionProbeRgbaMips = reflectionProbeRgbaMips;
        ReflectionProbeDataError = reflectionProbeDataError;
        SunDirection = sunDirection;
        SunColorLinear = sunColorLinear;
        Batches = batches;
        BatchLightmapIndices = batchLightmapIndices;
        BatchHasDirectSun = batchHasDirectSun;
        BatchReflectionProbeIndices = batchReflectionProbeIndices;
        MissingSurfaceCount = missingSurfaceCount;
        UnsupportedSurfaceCount = unsupportedSurfaceCount;
        UnlightmappedSurfaceCount = unlightmappedSurfaceCount;
        LightmapDataError = lightmapDataError;
        SunDataError = sunDataError;
        DirectSunOmittedSurfaceCount = directSunOmittedSurfaceCount;
        Bounds = bounds;
    }

    internal (int LightmapIndex, bool HasDirectSun) LightingAt(int vertexIndex)
    {
        int low = 0, high = Batches.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (Batches[middle].Start > vertexIndex) high = middle - 1;
            else low = middle + 1;
        }
        return (BatchLightmapIndices[high], BatchHasDirectSun[high]);
    }

    internal byte ReflectionProbeAt(int vertexIndex)
    {
        int low = 0, high = Batches.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (Batches[middle].Start > vertexIndex) high = middle - 1;
            else low = middle + 1;
        }
        return BatchReflectionProbeIndices[high];
    }

    internal static CompiledBspPreview Read(string path)
    {
        D3dbspFile file = D3dbspFile.Read(path);
        var surfaces = file.GetRenderTriangles();
        IReadOnlyList<IReadOnlyList<byte[]>?> reflectionProbeRgbaMips;
        string? reflectionProbeDataError = null;
        try { reflectionProbeRgbaMips = file.GetRenderReflectionProbeRgbaMips(); }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            reflectionProbeRgbaMips = [];
            reflectionProbeDataError = exception.Message;
        }
        IReadOnlyList<(byte[] Primary, byte[] UpperRgba, byte[] LowerRgba)> planes;
        string? lightmapDataError = null;
        try { planes = file.GetRenderLightmapPlanes(); }
        catch (InvalidDataException exception)
        {
            planes = [];
            lightmapDataError = exception.Message;
        }
        var diffuseLightmaps = new byte[]?[planes.Count];
        var sunVisibilityLightmaps = new byte[]?[planes.Count];
        for (int index = 0; index < planes.Count; index++)
        {
            (byte[] primary, byte[] upper, byte[] lower) = planes[index];
            if (!IsRadiantDiffuseEncoding(upper, lower)) continue;
            diffuseLightmaps[index] = upper;
            sunVisibilityLightmaps[index] = primary;
        }
        Vector3 sunDirection = Vector3.Zero, sunColorLinear = Vector3.Zero;
        string? sunDataError = null;
        try
        {
            IReadOnlyList<ComPrimaryLight> lights = file.GetRenderPrimaryLights();
            if (lights.Count != 2 || lights[0].Type != GfxLightType.None ||
                lights[1].Type != GfxLightType.Directional)
                sunDataError = "The compiled primary-light table is outside Radiant's two-row sun profile.";
            else
            {
                var direction = lights[1].Dir;
                var color = lights[1].Color;
                sunDirection = new Vector3(direction.X, direction.Y, direction.Z);
                Vector3 encodedColor = new(color.X, color.Y, color.Z);
                float directionLengthSquared = sunDirection.LengthSquared();
                if (!Finite(sunDirection) || !float.IsFinite(directionLengthSquared) ||
                    directionLengthSquared <= 0 ||
                    !Finite(encodedColor) || Vector3.Min(encodedColor, Vector3.Zero) != Vector3.Zero)
                    sunDataError = "The compiled sun direction or color is invalid.";
                else
                {
                    sunDirection = Vector3.Normalize(sunDirection);
                    sunColorLinear = new Vector3(GfxColorCodec.GammaToLinear(encodedColor.X),
                        GfxColorCodec.GammaToLinear(encodedColor.Y), GfxColorCodec.GammaToLinear(encodedColor.Z));
                    if (!Finite(sunColorLinear))
                        sunDataError = "The compiled sun color exceeds the supported range.";
                }
            }
        }
        catch (InvalidDataException exception) { sunDataError = exception.Message; }
        var vertices = new List<SceneVertex>();
        var lightmapUvs = new List<Vector2>();
        var batches = new List<(string Material, int Start, int Count, int WireStart, int WireCount)>();
        var batchLightmapIndices = new List<int>();
        var batchHasDirectSun = new List<bool>();
        var batchReflectionProbeIndices = new List<byte>();
        int missing = 0, unsupported = 0, unlightmapped = 0, sunOmitted = 0;
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        var classified = surfaces.Select(surface =>
        {
            int index = surface.LightmapIndex;
            if (index == NoLightmap) unlightmapped++;
            else if (index >= diffuseLightmaps.Length) { missing++; index = MissingLightmap; }
            else if (diffuseLightmaps[index] is null || surface.Vertices.Any(vertex =>
                         !float.IsFinite(vertex.LightmapUv.X) || !float.IsFinite(vertex.LightmapUv.Y) ||
                         vertex.LightmapUv.X < 0 || vertex.LightmapUv.X > 1 ||
                         vertex.LightmapUv.Y < 0 || vertex.LightmapUv.Y > 1))
            {
                unsupported++;
                index = UnsupportedLightmap;
            }
            bool hasSun = index >= 0 && index != NoLightmap &&
                sunDataError is null && surface.PrimaryLightIndex == 1;
            if (index >= 0 && index != NoLightmap && !hasSun) sunOmitted++;
            return (Surface: surface, LightmapIndex: index, HasSun: hasSun,
                surface.ReflectionProbeIndex);
        });
        foreach (var material in classified.GroupBy(item => item.Surface.Material, StringComparer.Ordinal))
        foreach (var group in material.GroupBy(item => (item.LightmapIndex, item.HasSun, item.ReflectionProbeIndex)))
        {
            int start = vertices.Count;
            foreach (var item in group)
            {
                var surface = item.Surface;
                for (int triangle = 0; triangle < surface.Vertices.Count; triangle += 3)
                {
                    // D3DBSP indices use the native clockwise winding; OpenGL uses CCW here.
                    Add(surface.Vertices[triangle]);
                    Add(surface.Vertices[triangle + 2]);
                    Add(surface.Vertices[triangle + 1]);
                }
            }
            batches.Add((material.Key, start, vertices.Count - start, 0, 0));
            batchLightmapIndices.Add(group.Key.LightmapIndex);
            batchHasDirectSun.Add(group.Key.HasSun);
            batchReflectionProbeIndices.Add(group.Key.ReflectionProbeIndex);
        }
        void Add((Vector3 Position, Vector3 Normal, Vector2 Uv, Vector2 LightmapUv, Vector4 Color) vertex)
        {
            vertices.Add(new SceneVertex(vertex.Position, vertex.Normal, vertex.Uv, vertex.Color));
            lightmapUvs.Add(float.IsFinite(vertex.LightmapUv.X) && float.IsFinite(vertex.LightmapUv.Y)
                ? Vector2.Clamp(vertex.LightmapUv, Vector2.Zero, Vector2.One) : Vector2.Zero);
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }
        if (vertices.Count == 0)
            throw new InvalidDataException("The d3dbsp has no render triangles to preview.");
        var preview = new CompiledBspPreview(vertices.ToArray(), lightmapUvs.ToArray(), diffuseLightmaps,
            sunVisibilityLightmaps, reflectionProbeRgbaMips, reflectionProbeDataError, sunDirection, sunColorLinear,
            batches.ToArray(), batchLightmapIndices.ToArray(), batchHasDirectSun.ToArray(),
            batchReflectionProbeIndices.ToArray(),
            missing, unsupported, unlightmapped, sunOmitted, lightmapDataError, sunDataError, (min, max));
        try { preview._modelPlacements = file.GetRenderStaticModels(); }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            preview.ModelDataError = exception.Message;
        }
        try
        {
            IReadOnlyDictionary<string, string>? world = file.GetEntities().FirstOrDefault(entity =>
                entity.GetValueOrDefault("classname") == "worldspawn");
            if (world is not null)
                preview.WaterDefinitions = WaterMaterialAuthoring.ReadDefinitions(world);
            else if (surfaces.Any(surface => WaterMaterialAuthoring.IsAuthoredMaterialName(surface.Material)))
                preview.WaterDataError = "The compiled BSP has no worldspawn water definitions.";
            foreach (var batch in preview.Batches)
            {
                if (!preview.WaterDefinitions.TryGetValue(batch.Material, out WaterMaterialDefinition? definition) ||
                    definition.Ocean is not { } ocean) continue;
                Vector3 displacement = new(ocean.Height);
                for (int index = batch.Start; index < batch.Start + batch.Count; index++)
                {
                    Vector3 position = preview.Vertices[index].Position;
                    preview.Bounds = (Vector3.Min(preview.Bounds.Min, position - displacement),
                        Vector3.Max(preview.Bounds.Max, position + displacement));
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or
                                          ArgumentException or FormatException or OverflowException)
        {
            preview.WaterDataError = exception.Message;
        }
        return preview;
    }

    internal void ResolveModels(Func<string, XModelSource?> resolveModel)
    {
        var resolved = new List<(string Name, Matrix4x4 Transform, XModelSource Source)>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var unsupported = new List<string>();
        (Vector3 Min, Vector3 Max) bounds = Bounds;
        foreach ((string name, Matrix4x4 transform) in _modelPlacements)
        {
            XModelSource? source = resolveModel(name);
            if (source is null) { missing.Add(name); continue; }
            try
            {
                (Vector3 localMin, Vector3 localMax) = source.Bounds;
                Vector3 modelMin = new(float.PositiveInfinity), modelMax = new(float.NegativeInfinity);
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = Vector3.Transform(new Vector3(
                        (corner & 1) == 0 ? localMin.X : localMax.X,
                        (corner & 2) == 0 ? localMin.Y : localMax.Y,
                        (corner & 4) == 0 ? localMin.Z : localMax.Z), transform);
                    if (!Finite(point))
                        throw new InvalidDataException($"Compiled model '{name}' has non-finite world bounds.");
                    modelMin = Vector3.Min(modelMin, point);
                    modelMax = Vector3.Max(modelMax, point);
                }
                bounds = (Vector3.Min(bounds.Min, modelMin), Vector3.Max(bounds.Max, modelMax));
                resolved.Add((name, transform, source));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                              InvalidOperationException or ArgumentException or IndexOutOfRangeException)
            {
                unsupported.Add($"{name}: {exception.Message}");
            }
        }
        Models = resolved;
        Bounds = bounds;
        string notice = string.Join("; ", new[]
        {
            missing.Count == 0 ? null : $"Missing compiled models: {string.Join(", ", missing)}",
            unsupported.Count == 0 ? null : $"Unsupported compiled models: {string.Join("; ", unsupported.Take(4))}" +
                (unsupported.Count > 4 ? $"; {unsupported.Count - 4} more" : ""),
            ModelDataError is null ? null : $"Compiled model placements: {ModelDataError}"
        }.OfType<string>());
        ModelAssetNotice = notice.Length == 0 ? null : notice;
    }

    private static bool IsRadiantDiffuseEncoding(byte[] upper, byte[] lower)
    {
        // BrushLightmapCompiler writes sqrt-encoded diffuse RGB in the upper
        // plane, no directional RGB in the lower, and alpha 128 at occupied luxels.
        bool occupied = false;
        for (int offset = 0; offset < upper.Length; offset += 4)
        {
            if (lower[offset] != 0 || lower[offset + 1] != 0 || lower[offset + 2] != 0 ||
                upper[offset + 3] is not (0 or 128) || lower[offset + 3] != upper[offset + 3])
                return false;
            occupied |= upper[offset + 3] == 128;
        }
        return occupied;
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
