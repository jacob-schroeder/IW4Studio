using IW4.Formats.SourceFormat.Material;
using System.Buffers;
using System.Numerics;
using System.Runtime.Versioning;
using IW4.Formats.SourceFormat.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Codecs.GfxMap;
using IW4.Render.Geometry;
using IW4.Render.Metal;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;
using RayHit = IW4.Render.Geometry.GpuRayTraversal.Hit;
using RaySurfaceFlags = IW4.Render.Geometry.GpuRayTraversal.SurfaceFlags;

namespace Iw4Radiant.Compilation.Lighting;

/// <summary>CPU sampling of authored world surfaces and sky images for the lighting bake.</summary>
internal sealed class BrushLightingScene
{
    // Twelve hemisphere rays at each of four diffuse samples, plus sixteen
    // sun visibility rays, give a 64-ray base direct-light budget per luxel.
    private const int SkySampleCount = 24;
    private const int BounceLuxelStep = 4;
    private const float RayOffset = 0.0625f;
    private readonly MaterialSource[] _materials;
    private readonly ImageSourceMipLevel[] _images;
    private readonly Vector3[] _skyDirections;
    private readonly (Vector3 Normal, Vector3 Anchor)[][] _solidBrushes;
    private readonly Vector3[] _lightGridNormals;
    private readonly float[] _lightGridSkyWeights;
    private readonly float[] _lightGridSkyWeightTotals;
    private readonly MapStageLighting _stages;
    private readonly Vector3[] _sunColors;
    private readonly Vector3[] _sunDirections;
    private readonly (MapLight Light, Vector3 LinearColor, byte PrimaryIndex)[] _localLights;
    private readonly ShadowModel[] _shadowModels;
    private readonly LightingRayHierarchy _worldRays;
    private readonly Comparison<SurfaceHit> _coincidentHitComparison;
    private readonly DirectFace?[] _directFaces;
    private bool _directFacesFrozen;

    private readonly record struct SurfaceHit(int Face, float Distance, float Opacity, Vector4 Texel,
        Vector3 Point, Vector4 Color, Vector3 Normal);

    private readonly record struct DirectSample(Vector3 Irradiance, float SunVisibility);

    private sealed class DirectFace(Vector3 anchor, Vector3 uAxis, Vector3 vAxis, Vector2 minimum,
        float luxelSize, int width, int height)
    {
        private readonly int _sampleWidth = (width - 2) / BounceLuxelStep + 2;
        private readonly int _sampleHeight = (height - 2) / BounceLuxelStep + 2;
        private readonly DirectSample[] _samples = new DirectSample[
            ((width - 2) / BounceLuxelStep + 2) * ((height - 2) / BounceLuxelStep + 2)];

        internal void Set(int x, int y, Vector3 irradiance, float visibility)
            => _samples[((y == height - 1 ? _sampleHeight - 1 : y / BounceLuxelStep) * _sampleWidth) +
                (x == width - 1 ? _sampleWidth - 1 : x / BounceLuxelStep)] = new(irradiance, visibility);

        internal DirectSample Sample(Vector3 point)
        {
            float x = Math.Clamp((Vector3.Dot(point - anchor, uAxis) - minimum.X) / luxelSize, 0, width - 1);
            float y = Math.Clamp((Vector3.Dot(point - anchor, vAxis) - minimum.Y) / luxelSize, 0, height - 1);
            int x0 = Math.Min((int)x / BounceLuxelStep, _sampleWidth - 2);
            int y0 = Math.Min((int)y / BounceLuxelStep, _sampleHeight - 2);
            int x1 = Math.Min((x0 + 1) * BounceLuxelStep, width - 1);
            int y1 = Math.Min((y0 + 1) * BounceLuxelStep, height - 1);
            float tx = Math.Clamp((x - x0 * BounceLuxelStep) / (x1 - x0 * BounceLuxelStep), 0, 1);
            float ty = Math.Clamp((y - y0 * BounceLuxelStep) / (y1 - y0 * BounceLuxelStep), 0, 1);
            DirectSample a = Lerp(_samples[y0 * _sampleWidth + x0], _samples[y0 * _sampleWidth + x0 + 1], tx);
            DirectSample b = Lerp(_samples[(y0 + 1) * _sampleWidth + x0], _samples[(y0 + 1) * _sampleWidth + x0 + 1], tx);
            return Lerp(a, b, ty);
        }

        private static DirectSample Lerp(DirectSample a, DirectSample b, float amount) =>
            new(Vector3.Lerp(a.Irradiance, b.Irradiance, amount),
                a.SunVisibility + (b.SunVisibility - a.SunVisibility) * amount);
    }

    private sealed record ShadowModel(Vector3 Minimum, Vector3 Maximum,
        (MapRenderSurface Surface, MaterialSource Material, ImageSourceMipLevel? Image)[] Triangles,
        LightingRayHierarchy Rays);

    internal BrushLightingScene(MapDocument document, IReadOnlyList<MapRenderSurface> polygons,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        MapStageLighting stages, CancellationToken cancellationToken = default)
    {
        Polygons = polygons;
        CancellationToken = cancellationToken;
        _stages = stages;
        _sunColors = new Vector3[stages.SunCount + 1];
        _sunDirections = new Vector3[stages.SunCount + 1];
        for (byte index = 1; index <= stages.SunCount; index++)
        {
            MapSunProperties sun = stages.Sun(index);
            _sunDirections[index] = sun.Direction;
            Vector3 sunColor = sun.Color * sun.Intensity;
            _sunColors[index] = new Vector3(GfxColorCodec.GammaToLinear(sunColor.X),
                GfxColorCodec.GammaToLinear(sunColor.Y), GfxColorCodec.GammaToLinear(sunColor.Z));
        }
        var localLights = new List<(MapLight, Vector3, byte)>();
        int primaryIndex = stages.SunCount + 1;
        string? error;
        foreach (MapEntity entity in document.Entities.Where(entity => entity.ClassName == "light"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MapLight.TryCreate(entity, document.ResolveTargets(entity), out MapLight light, out error))
            {
                if (error is not null) throw new InvalidDataException(error);
                continue;
            }
            // MapLight already normalizes authored RGB and applies intensity.
            // Convert once, before applying spatial attenuation in linear light.
            Vector3 color = new(GfxColorCodec.GammaToLinear(light.Color.X),
                GfxColorCodec.GammaToLinear(light.Color.Y), GfxColorCodec.GammaToLinear(light.Color.Z));
            if (!BrushGeometry.IsFinite(color))
                throw new NotSupportedException("The authored local light color and intensity exceed the supported numeric range.");
            if (light.IsPrimary && primaryIndex >= 256 - stages.SunCount)
                throw new NotSupportedException("The authored primary lights exceed the native light-grid ordinal range.");
            localLights.Add((light, color, light.IsPrimary ? checked((byte)primaryIndex++) : (byte)0));
        }
        _localLights = localLights.ToArray();
        _materials = new MaterialSource[polygons.Count];
        _directFaces = new DirectFace?[polygons.Count];
        _coincidentHitComparison = (a, b) =>
        {
            int material = _materials[b.Face].Surface.SortKey.CompareTo(_materials[a.Face].Surface.SortKey);
            return material != 0 ? material : a.Face.CompareTo(b.Face);
        };
        _images = new ImageSourceMipLevel[polygons.Count];
        var images = new Dictionary<string, ImageSourceMipLevel>(StringComparer.Ordinal);
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        for (int index = 0; index < polygons.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MapRenderSurface polygon = polygons[index];
            if (!materials.TryGetValue(polygon.Material, out MaterialSource? material))
                throw new InvalidDataException($"Lighting has no source material '{polygon.Material}'.");
            _materials[index] = material;
            if (!material.IsWater)
            {
                if (!images.TryGetValue(material.ImagePath, out ImageSourceMipLevel pixels))
                {
                    if (!Path.GetExtension(material.ImagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException($"The native lighting bake requires a DDS color image for '{material.Name}'.");
                    using FileStream stream = File.OpenRead(material.ImagePath);
                    ImageFileDocument image = new ImageExchange().Read(stream, ImageFileFormat.Dds);
                    if (image.Shape != (material.IsSky ? ImageFileShape.Cube : ImageFileShape.TwoDimensional) || image.MipLevels.Count == 0)
                        throw new InvalidDataException($"The lighting image shape does not match material '{material.Name}'.");
                    if (image.UsesSrgbReads == true)
                        throw new NotSupportedException($"Material '{material.Name}' requests sRGB image reads; this native squared-color bake profile requires ordinary texture reads.");
                    pixels = image.MipLevels[0];
                    images.Add(material.ImagePath, pixels);
                }
                _images[index] = pixels;
            }
            if (material.IsSky || !polygon.ContributesToLightGrid) continue;
            foreach (Vector3 point in polygon.Vertices)
            {
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
        }
        if (polygons.Any(polygon => !polygon.ContributesToLightGrid))
        {
            // A smaller grid must still cover offshore props, including models
            // that do not cast shadows. Unflagged maps retain their existing bounds.
            foreach (MapEntity entity in document.Entities.Where(entity => entity.ClassName == "misc_model"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = entity.Properties.GetValueOrDefault("model") ?? "";
                if (!models.TryGetValue(name, out XModelSource? model))
                    throw new InvalidDataException($"Lighting has no source geometry for static model '{name}'.");
                var bounds = XModelGeometry.Bounds(entity, model);
                minimum = Vector3.Min(minimum, bounds.Min);
                maximum = Vector3.Max(maximum, bounds.Max);
            }
        }
        if (!BrushGeometry.IsFinite(minimum) || !BrushGeometry.IsFinite(maximum))
            throw new InvalidDataException("Lighting requires finite non-sky geometry or model bounds with lighting enabled.");
        var solidBrushes = new List<(Vector3 Normal, Vector3 Anchor)[]>();
        foreach (MapBrush brush in document.World.Brushes)
        {
            bool solid = true;
            foreach (MapFace face in brush.Faces)
                if (!CaulkMaterial.IsCaulk(face.Material) &&
                    (!materials.TryGetValue(face.Material, out var material) || material.IsSky ||
                    material.IsWater || material.Surface.IsBlended || material.Surface.AlphaTest is not null)
                )
                {
                    solid = false;
                    break;
                }
            if (!solid) continue;
            var planes = new (Vector3 Normal, Vector3 Anchor)[brush.Faces.Count];
            for (int face = 0; face < planes.Length; face++)
                planes[face] = (brush.Faces[face].Normal, brush.Faces[face].A);
            solidBrushes.Add(planes);
        }
        _solidBrushes = solidBrushes.ToArray();
        var shadowModels = new List<ShadowModel>();
        foreach (MapEntity entity in document.Entities.Where(entity => entity.ClassName == "misc_model"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MapModelCompiler.CastsShadow(entity)) continue;
            string name = entity.Properties.GetValueOrDefault("model") ?? "";
            if (!models.TryGetValue(name, out XModelSource? model))
                throw new InvalidDataException($"Lighting has no source geometry for static model '{name}'. Load its XModel before building.");
            var triangles = new List<(MapRenderSurface, MaterialSource, ImageSourceMipLevel?)>();
            bool castsShadow = false;
            Vector3 modelMinimum = new(float.PositiveInfinity), modelMaximum = new(float.NegativeInfinity);
            foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!materials.TryGetValue(triangle.Material, out MaterialSource? material) || material.TechniqueSet.Length == 0)
                    throw new InvalidDataException($"Lighting has no native source material '{triangle.Material}' for static model '{name}'.");
                castsShadow |= material.Surface.HasShadowMapTechnique;
                Vector3[] vertices = [triangle.A.Position, triangle.B.Position, triangle.C.Position];
                Vector3 cross = Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]);
                if (cross.LengthSquared() <= 0.00000001f) continue;
                Vector3 normal = Vector3.Normalize(cross);
                var surface = new MapRenderSurface(triangle.Material, vertices, normal,
                    [triangle.A.Normal, triangle.B.Normal, triangle.C.Normal], [], [],
                    [triangle.A.Uv, triangle.B.Uv, triangle.C.Uv], [triangle.A.Color, triangle.B.Color, triangle.C.Color], triangles.Count);
                ImageSourceMipLevel? pixels = null;
                if (material.Surface.HasShadowMapTechnique && material.Surface.ShadowAlphaTest is not null)
                {
                    if (!images.TryGetValue(material.ImagePath, out ImageSourceMipLevel loaded))
                    {
                        if (!Path.GetExtension(material.ImagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                            throw new NotSupportedException($"The shadow bake requires a DDS alpha image for '{material.Name}'.");
                        using FileStream stream = File.OpenRead(material.ImagePath);
                        ImageFileDocument image = new ImageExchange().Read(stream, ImageFileFormat.Dds);
                        if (image.Shape != ImageFileShape.TwoDimensional || image.MipLevels.Count == 0)
                            throw new InvalidDataException($"The shadow image must be two-dimensional for '{material.Name}'.");
                        loaded = image.MipLevels[0];
                        images.Add(material.ImagePath, loaded);
                    }
                    pixels = loaded;
                }
                triangles.Add((surface, material, pixels));
                foreach (Vector3 vertex in vertices)
                {
                    modelMinimum = Vector3.Min(modelMinimum, vertex);
                    modelMaximum = Vector3.Max(modelMaximum, vertex);
                }
            }
            if (castsShadow && triangles.Count > 0)
                shadowModels.Add(new ShadowModel(modelMinimum, modelMaximum, triangles.ToArray(),
                    new LightingRayHierarchy(triangles.Select(triangle => triangle.Item1).ToArray(), cancellationToken)));
        }
        _shadowModels = shadowModels.ToArray();
        _worldRays = new LightingRayHierarchy(polygons, cancellationToken);
        Minimum = minimum;
        Maximum = maximum;
        _skyDirections = new Vector3[SkySampleCount];
        int hemisphereCount = _skyDirections.Length / 2;
        for (int index = 0; index < hemisphereCount; index++)
        {
            float z = 1 - (index + 0.5f) / hemisphereCount;
            float angle = index * 2.39996323f;
            float radius = MathF.Sqrt(1 - z * z);
            Vector3 direction = new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, z);
            // Antipodal pairs limit every surface normal to twelve
            // positive-cosine directions, including slopes and vertical walls.
            _skyDirections[index * 2] = direction;
            _skyDirections[index * 2 + 1] = -direction;
        }
        int lightGridSampleCount = GfxLightGridCodec.SampleDirections.Count;
        _lightGridNormals = new Vector3[lightGridSampleCount];
        _lightGridSkyWeights = new float[checked(_skyDirections.Length * lightGridSampleCount)];
        _lightGridSkyWeightTotals = new float[lightGridSampleCount];
        for (int sample = 0; sample < lightGridSampleCount; sample++)
        {
            var value = GfxLightGridCodec.SampleDirections[sample];
            _lightGridNormals[sample] = Vector3.Normalize(new Vector3(value.X, value.Y, value.Z));
        }
        for (int direction = 0; direction < _skyDirections.Length; direction++)
        for (int sample = 0; sample < lightGridSampleCount; sample++)
        {
            float weight = MathF.Max(0, Vector3.Dot(_lightGridNormals[sample], _skyDirections[direction]));
            _lightGridSkyWeights[direction * lightGridSampleCount + sample] = weight;
            _lightGridSkyWeightTotals[sample] += weight;
        }
    }

    internal IReadOnlyList<MapRenderSurface> Polygons { get; }
    internal CancellationToken CancellationToken { get; }
    internal Vector3 Minimum { get; }
    internal Vector3 Maximum { get; }
    internal byte SunCount => _stages.SunCount;
    internal Vector3 SunDirection(byte sunIndex) => _sunDirections[sunIndex];
    private Vector3 SunColor(byte sunIndex) => _sunColors[sunIndex];
    private byte SunIndexAt(Vector3 point) => _stages.SunIndexAt(point);
    private byte SunIndexForPrimary(Vector3 point, byte primaryIndex) =>
        primaryIndex > 0 && primaryIndex <= SunCount ? primaryIndex : SunIndexAt(point);
    internal bool IsSky(int face) => _materials[face].IsSky;
    internal bool IsWater(int face) => _materials[face].IsWater;
    internal bool HasPrimaryLocalLights => _localLights.Any(light => light.PrimaryIndex != 0);

    internal byte PrimaryLightForFace(int face)
    {
        if (IsSky(face)) return 0;
        if (IsWater(face)) return Polygons[face].SunPrimaryLightIndex;
        Vector3 minimum = Polygons[face].Vertices.Aggregate(Vector3.Min);
        Vector3 maximum = Polygons[face].Vertices.Aggregate(Vector3.Max);
        // Native selectors scan local primaries in ascending order. A face may
        // extend outside the radius: the native attenuation clips that portion.
        foreach (var (light, _, index) in _localLights)
            if (index != 0 && Vector3.DistanceSquared(light.Origin,
                    Vector3.Clamp(light.Origin, minimum, maximum)) < light.Radius * light.Radius &&
                (!light.IsSpotlight || PrimaryLocalLightProfile.SpotIntersectsBounds(light.Origin,
                    light.Direction, MathF.Cos(light.CoverageAngle), minimum, maximum)))
                return index;
        return Polygons[face].SunPrimaryLightIndex;
    }

    internal byte PrimaryLightAt(Vector3 point)
    {
        foreach (var (light, _, index) in _localLights)
            if (index != 0 && Vector3.DistanceSquared(point, light.Origin) < light.Radius * light.Radius &&
                (!light.IsSpotlight || Vector3.Dot(light.Direction,
                    -DirectionToLight(point, light.Origin, out _)) > MathF.Cos(light.CoverageAngle)))
                return index;
        return SunIndexAt(point);
    }

    internal float PrimaryVisibility(Vector3 point, Vector3 normal, byte primaryIndex)
    {
        if (primaryIndex > 0 && primaryIndex <= SunCount) return SunVisibility(point, normal, primaryIndex);
        if (primaryIndex == 0) return 0;
        MapLight light = _localLights.First(value => value.PrimaryIndex == primaryIndex).Light;
        Vector3 origin = point + normal * RayOffset;
        if (IsInsideSolid(origin, 0)) return 0;
        Vector3 direction = DirectionToLight(origin, light.Origin, out double distance);
        return ModelOccludes(origin, direction, distance, normal == Vector3.Zero ? ContainingModels(point) : null) ||
            WorldOccludes(origin, direction, distance) ? 0 : 1;
    }

    internal Vector3 SecondaryIrradiance(Vector3 point, Vector3 normal, Vector3 total, byte primaryIndex, byte sunIndex)
    {
        if (primaryIndex <= SunCount) return total;
        var (light, color, _) = _localLights.First(value => value.PrimaryIndex == primaryIndex);
        Vector3 direct = LocalRadiance(point, point + normal * RayOffset, light, color, out Vector3 direction) *
            MathF.Max(0, Vector3.Dot(normal, direction));
        Vector3 sun = SunColor(sunIndex) * (MathF.Max(0, Vector3.Dot(normal, SunDirection(sunIndex))) *
            SunVisibility(point, normal, sunIndex));
        return Vector3.Max(Vector3.Zero, total - direct) + sun;
    }

    internal bool BeginDirectFace(int face, Vector3 anchor, Vector3 uAxis, Vector3 vAxis,
        Vector2 minimum, float luxelSize, int width, int height)
    {
        MaterialSource material = _materials[face];
        if (material.IsSky || material.IsWater || material.Surface.IsBlended ||
            material.Surface.AlphaTest is not null || material.Surface.TechniqueType != IW4.Game.Assets.TechniqueSet.MaterialTechniqueType.Lit)
            return false;
        _directFaces[face] = new DirectFace(anchor, uAxis, vAxis, minimum, luxelSize, width, height);
        return true;
    }

    internal static bool IsDirectCacheLuxel(int x, int y, int width, int height) =>
        (x % BounceLuxelStep == 0 || x == width - 1) &&
        (y % BounceLuxelStep == 0 || y == height - 1);

    internal void RecordDirectLuxel(int face, int x, int y, Vector3 irradiance, float sunVisibility)
    {
        DirectFace directFace = _directFaces[face] ??
            throw new InvalidOperationException("The face has no direct bounce field.");
        directFace.Set(x, y, irradiance, sunVisibility);
    }

    internal void FreezeDirectFaces() => _directFacesFrozen = true;

    internal bool IsInsideSolid(Vector3 point, float inset = RayOffset)
    {
        foreach (var brush in _solidBrushes)
        {
            bool inside = true;
            foreach (var (normal, anchor) in brush)
                if (!(BrushGeometry.Dot(normal, point - anchor) < -inset))
                {
                    inside = false;
                    break;
                }
            if (inside) return true;
        }
        return false;
    }

    private bool IsOnSolidBoundary(Vector3 point)
    {
        // Grid points are exact multiples of the native spacing and can lie
        // directly on brush planes. The CPU's double triangle predicate keeps
        // entering zero-distance contacts that a native t>=0 query may omit.
        const double tolerance = 0.0001;
        foreach (var brush in _solidBrushes)
        {
            bool nearFace = false;
            bool outside = false;
            foreach (var (normal, anchor) in brush)
            {
                double distance = BrushGeometry.Dot(normal, point - anchor);
                if (distance > tolerance) { outside = true; break; }
                if (distance >= -tolerance) nearFace = true;
            }
            if (!outside && nearFace) return true;
        }
        return false;
    }

    private bool IsNearWorldSurface(Vector3 point)
    {
        // Non-solid terrain can pass exactly through a light-grid point too.
        // Probe a tiny segment through that point using the existing world BVH;
        // only those contact samples need the CPU's double hit predicate.
        const float radius = 0.001f;
        Vector3 direction = Vector3.Normalize(new Vector3(1, 2, 3));
        Vector3 origin = point - direction * radius;
        var candidates = _worldRays.Query(origin, direction, radius * 2, CancellationToken);
        while (candidates.MoveNext())
        {
            MapRenderSurface polygon = Polygons[candidates.Current];
            for (int corner = 1; corner < polygon.Vertices.Length - 1; corner++)
                if (SurfaceRaycast.RayTriangle(origin, direction, polygon.Vertices[0],
                        polygon.Vertices[corner], polygon.Vertices[corner + 1], out float distance) &&
                    distance <= radius * 2)
                    return true;
        }
        return false;
    }

    internal bool CastsSunShadow(int face) => !IsSky(face) && !_materials[face].IsWater &&
        _materials[face].Surface.HasShadowMapTechnique;

    internal float SunVisibility(Vector3 point, Vector3 normal, byte sunIndex)
        => SampleSunVisibility(point, normal, sunIndex, default);

    private float SampleSunVisibility(Vector3 point, Vector3 normal, byte sunIndex, ReadOnlySpan<RayHit> candidateRow)
    {
        int candidateCount = candidateRow.IsEmpty ? -1 : candidateRow[0].Surface;
        if (candidateCount == GpuRayTraversal.ShadowOccluded) return 0;
        Vector3 origin = point + normal * RayOffset;
        Vector3 direction = SunDirection(sunIndex);
        // The native shadow result includes static models. Rays from a grid
        // point can exclude a containing model, so those retain the CPU path.
        if ((candidateRow.IsEmpty || candidateRow[0].Distance < 0 ||
             candidateCount == GpuRayTraversal.ShadowModelUncertain || normal == Vector3.Zero) &&
            ModelOccludes(origin, direction, double.PositiveInfinity,
                normal == Vector3.Zero ? ContainingModels(point) : null)) return 0;
        if (candidateCount == GpuRayTraversal.ShadowUnoccluded) return 1;
        bool occluded = candidateCount >= 0 && candidateCount < GpuRayTraversal.ResultStride
            ? WorldOccludes(origin, direction, double.PositiveInfinity, candidateRow.Slice(1, candidateCount))
            : WorldOccludes(origin, direction, double.PositiveInfinity);
        return occluded ? 0 : 1;
    }

    internal Vector3 DiffuseIrradiance(Vector3 point, Vector3 normal, byte sunIndex)
        => SampleDiffuseIrradiance(point, normal, default, indirectOnly: false, sunIndex: sunIndex);

    internal Vector3 IndirectIrradiance(Vector3 point, Vector3 normal, byte sunIndex)
        => SampleDiffuseIrradiance(point, normal, default, indirectOnly: true, sunIndex: sunIndex);

    internal GpuRayTraversal? CreateGpuTraversal(IProgress<string>? progress)
    {
        if (!OperatingSystem.IsMacOS())
            return _worldRays.CreateGpuTraversal([], [], 0, CancellationToken, progress);
        int modelTriangleCount = _shadowModels.Sum(model =>
            model.Triangles.Count(triangle => triangle.Material.Surface.HasShadowMapTechnique));
        var surfaces = new GpuRayTraversal.Surface[checked(Polygons.Count + modelTriangleCount)];
        var vertices = new Vector3[checked(Polygons.Sum(polygon => polygon.Vertices.Length) +
            modelTriangleCount * 3)];
        int offset = 0;
        for (int index = 0; index < Polygons.Count; index++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            MapRenderSurface polygon = Polygons[index];
            MaterialSurfaceState state = _materials[index].Surface;
            RaySurfaceFlags flags = 0;
            if (IsSky(index)) flags |= RaySurfaceFlags.Sky;
            if (_materials[index].IsWater) flags |= RaySurfaceFlags.Water;
            if (CastsSunShadow(index)) flags |= RaySurfaceFlags.CastsShadow;
            if (state.ShadowAlphaTest is not null) flags |= RaySurfaceFlags.ShadowAlphaTest;
            if (state.CullFace == GfxCullFace.Back) flags |= RaySurfaceFlags.CullBack;
            if (state.CullFace == GfxCullFace.Front) flags |= RaySurfaceFlags.CullFront;
            if (state.ShadowCullFace == GfxCullFace.Back) flags |= RaySurfaceFlags.ShadowCullBack;
            if (state.ShadowCullFace == GfxCullFace.Front) flags |= RaySurfaceFlags.ShadowCullFront;
            surfaces[index] = new(polygon.Normal, offset, polygon.Vertices.Length, flags);
            polygon.Vertices.CopyTo(vertices, offset);
            offset += polygon.Vertices.Length;
        }
        int surfaceIndex = Polygons.Count;
        foreach (ShadowModel model in _shadowModels)
        foreach (var (triangle, material, image) in model.Triangles)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (!material.Surface.HasShadowMapTechnique) continue;
            RaySurfaceFlags flags = RaySurfaceFlags.CastsShadow;
            if (image is not null) flags |= RaySurfaceFlags.ShadowAlphaTest;
            if (material.Surface.ShadowCullFace == GfxCullFace.Back)
                flags |= RaySurfaceFlags.ShadowCullBack;
            if (material.Surface.ShadowCullFace == GfxCullFace.Front)
                flags |= RaySurfaceFlags.ShadowCullFront;
            surfaces[surfaceIndex++] = new(triangle.Normal, offset, 3, flags);
            triangle.Vertices.CopyTo(vertices, offset);
            offset += 3;
        }
        GpuRayTraversal? traversal = _worldRays.CreateGpuTraversal(surfaces, vertices,
            Polygons.Count, CancellationToken, progress);
        if (OperatingSystem.IsMacOS() && traversal is MetalRayTraversal metal)
            ConfigureSkyIrradiance(metal, surfaces.Length, vertices.Length);
        return traversal;
    }

    [SupportedOSPlatform("macos")]
    private void ConfigureSkyIrradiance(MetalRayTraversal metal, int surfaceCount, int vertexCount)
    {
        var materials = new MetalRayTraversal.SkyMaterial[surfaceCount];
        var attributes = new MetalRayTraversal.SkyVertex[vertexCount];
        var images = new ArrayBufferWriter<byte>();
        var imageOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        int vertexOffset = 0;
        for (int face = 0; face < Polygons.Count; face++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            MapRenderSurface polygon = Polygons[face];
            MaterialSource material = _materials[face];
            MaterialSurfaceState state = material.Surface;
            bool sky = IsSky(face);
            bool needsImage = !material.IsWater &&
                (sky || state.AlphaTest is not null || state.ShadowAlphaTest is not null);
            int imageOffset = 0;
            int width = 1, height = 1, faces = 1;
            if (needsImage)
            {
                ImageSourceMipLevel image = _images[face];
                width = image.Width;
                height = image.Height;
                faces = sky ? 6 : 1;
                ReadOnlySpan<byte> pixels = image.RgbaBytes.Span;
                if (width <= 0 || height <= 0 || pixels.Length !=
                    (long)width * height * faces * 4) return;
                imageOffset = AppendImage(material.ImagePath, pixels);
            }
            materials[face] = new(imageOffset, width, height, faces,
                (uint)material.SamplerState, (uint?)state.AlphaTest ?? 0,
                state.IsBlended ? 1u : 0u, (uint?)state.ShadowAlphaTest ?? 0);
            for (int corner = 0; corner < polygon.Vertices.Length; corner++)
            {
                if (needsImage && !sky &&
                    (corner >= polygon.TextureCoordinates.Length || corner >= polygon.Colors.Length)) return;
                attributes[vertexOffset + corner] = needsImage && !sky
                    ? new(polygon.TextureCoordinates[corner], polygon.Colors[corner].W)
                    : new(Vector2.Zero, 1);
            }
            vertexOffset += polygon.Vertices.Length;
        }
        int modelSurface = Polygons.Count;
        foreach (ShadowModel model in _shadowModels)
        foreach (var (triangle, material, image) in model.Triangles)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (!material.Surface.HasShadowMapTechnique) continue;
            int imageOffset = 0, width = 1, height = 1;
            if (image is { } pixels)
            {
                width = pixels.Width;
                height = pixels.Height;
                ReadOnlySpan<byte> rgba = pixels.RgbaBytes.Span;
                if (width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4)
                    return;
                imageOffset = AppendImage(material.ImagePath, rgba);
            }
            materials[modelSurface++] = new(imageOffset, width, height, 1,
                (uint)material.SamplerState, 0, 0,
                (uint?)material.Surface.ShadowAlphaTest ?? 0);
            for (int corner = 0; corner < 3; corner++)
                attributes[vertexOffset + corner] = new(triangle.TextureCoordinates[corner],
                    triangle.Colors[corner].W);
            vertexOffset += 3;
        }
        if (images.WrittenCount > 0)
            metal.TryConfigureSky(materials, attributes, images.WrittenSpan, _skyDirections);

        int AppendImage(string path, ReadOnlySpan<byte> pixels)
        {
            if (imageOffsets.TryGetValue(path, out int existing)) return existing;
            int offset = images.WrittenCount;
            pixels.CopyTo(images.GetSpan(pixels.Length));
            images.Advance(pixels.Length);
            imageOffsets.Add(path, offset);
            return offset;
        }
    }

    internal void BakeDiffuseSamples(Vector3[] points, Vector3[] normals, Vector3[] irradiance, int count,
        byte sunIndex, GpuRayTraversal traversal, ParallelOptions parallelOptions, bool indirectOnly = false)
    {
        if (OperatingSystem.IsMacOS() && !indirectOnly && !_directFacesFrozen &&
            traversal is MetalRayTraversal metal &&
            TryBakeDirectSkySamples(points, normals, irradiance, count, sunIndex, metal, parallelOptions))
            return;
        var offsets = ArrayPool<int>.Shared.Rent(count + 1);
        var masks = ArrayPool<ulong>.Shared.Rent(count);
        var rays = ArrayPool<GpuRayTraversal.Ray>.Shared.Rent(GpuRayTraversal.MaximumRayCount);
        var candidates = ArrayPool<RayHit>.Shared.Rent(
            GpuRayTraversal.MaximumRayCount * GpuRayTraversal.ResultStride);
        try
        {
            offsets[0] = 0;
            Parallel.For(0, count, parallelOptions, sample =>
            {
                if ((sample & 255) == 0) CancellationToken.ThrowIfCancellationRequested();
                ulong mask = 0;
                int activeCount = 0;
                for (int direction = 0; direction < SkySampleCount; direction++)
                {
                    float cosine = MathF.Max(0, Vector3.Dot(normals[sample], _skyDirections[direction]));
                    if (cosine > 0)
                    {
                        mask |= 1UL << direction;
                        activeCount++;
                    }
                }
                masks[sample] = mask;
                offsets[sample + 1] = activeCount;
            });
            for (int sample = 0; sample < count; sample++)
            {
                if ((sample & 255) == 0) CancellationToken.ThrowIfCancellationRequested();
                offsets[sample + 1] = checked(offsets[sample] + offsets[sample + 1]);
            }
            if (count == 0) return;

            int first = 0, last = BatchEnd(0);
            bool pending = Submit(first, last);
            while (first < count)
            {
                CancellationToken.ThrowIfCancellationRequested();
                int nextFirst = last;
                int nextLast = nextFirst < count ? BatchEnd(nextFirst) : nextFirst;
                bool submitAhead = traversal.MaximumInFlightBatches > 1;
                bool nextPending = submitAhead && nextFirst < count && Submit(nextFirst, nextLast);
                bool ready = pending && traversal.TryRead(candidates, CancellationToken);
                if (!submitAhead) nextPending = nextFirst < count && Submit(nextFirst, nextLast);
                pending = nextPending;
                int batchFirst = first, sampleCount = last - first;
                Parallel.For(0, sampleCount, parallelOptions, sample =>
                {
                    int index = batchFirst + sample;
                    int firstRay = offsets[index] - offsets[batchFirst];
                    int activeCount = offsets[index + 1] - offsets[index];
                    irradiance[index] = SampleDiffuseIrradiance(points[index], normals[index], ready
                        ? candidates.AsSpan(firstRay * GpuRayTraversal.ResultStride,
                            activeCount * GpuRayTraversal.ResultStride)
                        : default, indirectOnly, sunIndex);
                });
                first = nextFirst;
                last = nextLast;
            }

            int BatchEnd(int batchFirst)
            {
                int batchLast = batchFirst + 1;
                while (batchLast < count &&
                       offsets[batchLast + 1] - offsets[batchFirst] <= GpuRayTraversal.MaximumRayCount)
                    batchLast++;
                return batchLast;
            }

            bool Submit(int batchFirst, int batchLast)
            {
                int ray = 0;
                for (int sample = batchFirst; sample < batchLast; sample++)
                {
                    Vector3 origin = points[sample] + normals[sample] * RayOffset;
                    for (int direction = 0; direction < SkySampleCount; direction++)
                        if ((masks[sample] & (1UL << direction)) != 0)
                            rays[ray++] = new(origin, _skyDirections[direction]);
                }
                return traversal.TrySubmit(rays.AsSpan(0, ray), shadow: false, CancellationToken);
            }
        }
        finally
        {
            ArrayPool<RayHit>.Shared.Return(candidates);
            ArrayPool<GpuRayTraversal.Ray>.Shared.Return(rays);
            ArrayPool<ulong>.Shared.Return(masks);
            ArrayPool<int>.Shared.Return(offsets);
        }
    }

    [SupportedOSPlatform("macos")]
    private bool TryBakeDirectSkySamples(Vector3[] points, Vector3[] normals, Vector3[] irradiance,
        int count, byte sunIndex, MetalRayTraversal metal, ParallelOptions parallelOptions)
    {
        var samples = ArrayPool<GpuRayTraversal.Ray>.Shared.Rent(MetalRayTraversal.SkySampleCapacity);
        try
        {
            float ambientScale = _stages.AmbientScale(sunIndex);
            for (int first = 0; first < count; first += MetalRayTraversal.SkySampleCapacity)
            {
                int batchCount = Math.Min(MetalRayTraversal.SkySampleCapacity, count - first);
                for (int sample = 0; sample < batchCount; sample++)
                    samples[sample] = new(points[first + sample], normals[first + sample]);
                if (!metal.TryBakeSkyIrradiance(samples.AsSpan(0, batchCount), ambientScale,
                        irradiance.AsSpan(first, batchCount), CancellationToken)) return false;
            }
            Parallel.For(0, count, parallelOptions, sample =>
            {
                Vector3 point = points[sample], normal = normals[sample];
                Vector3 origin = point + normal * RayOffset;
                Vector3 sky = irradiance[sample];
                if (!float.IsFinite(sky.X) || !float.IsFinite(sky.Y) || !float.IsFinite(sky.Z))
                {
                    irradiance[sample] = SampleDiffuseIrradiance(point, normal, default,
                        indirectOnly: false, sunIndex: sunIndex);
                    return;
                }
                if (IsInsideSolid(origin, 0)) sky = Vector3.Zero;
                irradiance[sample] = AddLocalIrradiance(point, normal, origin, sky);
            });
            return true;
        }
        finally { ArrayPool<GpuRayTraversal.Ray>.Shared.Return(samples); }
    }

    internal void BakeSunSamples(Vector3[] points, Vector3 normal, float[] visibility, int count,
        byte sunIndex, GpuRayTraversal traversal, ParallelOptions parallelOptions)
    {
        int batchCapacity = GpuRayTraversal.MaximumRayCount;
        var rays = ArrayPool<GpuRayTraversal.Ray>.Shared.Rent(batchCapacity);
        var candidates = ArrayPool<RayHit>.Shared.Rent(batchCapacity * GpuRayTraversal.ResultStride);
        try
        {
            bool pending = Submit(0);
            for (int first = 0; first < count; first += batchCapacity)
            {
                CancellationToken.ThrowIfCancellationRequested();
                int sampleCount = Math.Min(batchCapacity, count - first);
                bool submitAhead = traversal.MaximumInFlightBatches > 1;
                bool nextPending = submitAhead && first + sampleCount < count && Submit(first + sampleCount);
                bool ready = pending && traversal.TryRead(candidates, CancellationToken);
                if (!submitAhead) nextPending = first + sampleCount < count && Submit(first + sampleCount);
                pending = nextPending;
                Parallel.For(0, sampleCount, parallelOptions, sample =>
                {
                    int index = first + sample;
                    int offset = sample * GpuRayTraversal.ResultStride;
                    visibility[index] = SampleSunVisibility(points[index], normal, sunIndex, ready
                        ? candidates.AsSpan(offset, GpuRayTraversal.ResultStride)
                        : default);
                });
            }

            bool Submit(int first)
            {
                int sampleCount = Math.Min(batchCapacity, count - first);
                for (int sample = 0; sample < sampleCount; sample++)
                    rays[sample] = new(points[first + sample] + normal * RayOffset, SunDirection(sunIndex));
                return traversal.TrySubmit(rays.AsSpan(0, sampleCount), shadow: true, CancellationToken);
            }
        }
        finally
        {
            ArrayPool<RayHit>.Shared.Return(candidates);
            ArrayPool<GpuRayTraversal.Ray>.Shared.Return(rays);
        }
    }

    private Vector3 SampleDiffuseIrradiance(Vector3 point, Vector3 normal, ReadOnlySpan<RayHit> skyCandidates,
        bool indirectOnly, byte sunIndex)
    {
        Vector3 sum = Vector3.Zero;
        float totalWeight = 0;
        float ambientScale = _stages.AmbientScale(sunIndex);
        Vector3 origin = point + normal * RayOffset;
        // A face can continue underneath an adjoining brush. Subsamples inside
        // that solid must not see sky through its culled exit faces.
        bool insideSolid = IsInsideSolid(origin, 0);
        int activeDirection = 0;
        for (int directionIndex = 0; directionIndex < _skyDirections.Length; directionIndex++)
        {
            Vector3 direction = _skyDirections[directionIndex];
            float cosine = MathF.Max(0, Vector3.Dot(normal, direction));
            if (cosine > 0)
            {
                if (!insideSolid)
                {
                    int offset = activeDirection * GpuRayTraversal.ResultStride;
                    int count = skyCandidates.IsEmpty ? -1 : skyCandidates[offset].Surface;
                    Vector3 radiance = count < 0 ? DiffuseRayRadiance(origin, direction, indirectOnly,
                        ambientScale: ambientScale) :
                        count == 0 ? Vector3.Zero : DiffuseRayRadiance(origin, direction, indirectOnly,
                            ambientScale: ambientScale,
                            candidates: skyCandidates.Slice(offset + 1, count));
                    sum += radiance * cosine;
                }
                activeDirection++;
                totalWeight += cosine;
            }
        }
        // Normalize spherical quadrature so a constant visible environment
        // preserves its radiance exactly; blocked directions still contribute weight.
        sum /= totalWeight;
        return indirectOnly ? sum : AddLocalIrradiance(point, normal, origin, sum);
    }

    private Vector3 AddLocalIrradiance(Vector3 point, Vector3 normal, Vector3 origin, Vector3 sum)
    {
        foreach (var (light, color, _) in _localLights)
        {
            Vector3 radiance = LocalRadiance(point, origin, light, color, out Vector3 direction);
            sum += radiance * MathF.Max(0, Vector3.Dot(normal, direction));
        }
        return sum;
    }

    internal void DiffuseIrradianceDirections(Vector3 point, Span<Vector3> result, byte primaryIndex,
        ReadOnlySpan<RayHit> skyCandidates = default)
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (result.Length != _lightGridNormals.Length)
            throw new ArgumentException("The light-grid sample requires one value per direction.", nameof(result));
        result.Clear();
        byte sunIndex = SunIndexForPrimary(point, primaryIndex);
        float ambientScale = _stages.AmbientScale(sunIndex);
        HashSet<int>? containingModels = ContainingModels(point);
        bool insideSolid = IsInsideSolid(point, 0);
        for (int directionIndex = 0; directionIndex < _skyDirections.Length; directionIndex++)
        {
            Vector3 direction = _skyDirections[directionIndex];
            int offset = directionIndex * GpuRayTraversal.ResultStride;
            int count = skyCandidates.IsEmpty ? -1 : skyCandidates[offset].Surface;
            Vector3 radiance = insideSolid || count == 0 ? Vector3.Zero :
                count > 0 && count < GpuRayTraversal.ResultStride
                    ? DiffuseRayRadiance(point, direction, indirectOnly: false,
                        containingModels: containingModels,
                        candidates: skyCandidates.Slice(offset + 1, count), ambientScale: ambientScale)
                    : DiffuseRayRadiance(point, direction, indirectOnly: false,
                        containingModels: containingModels, ambientScale: ambientScale);
            for (int sample = 0; sample < result.Length; sample++)
            {
                float weight = _lightGridSkyWeights[directionIndex * result.Length + sample];
                result[sample] += radiance * weight;
            }
        }
        for (int sample = 0; sample < result.Length; sample++) result[sample] /= _lightGridSkyWeightTotals[sample];
        foreach (var (light, color, index) in _localLights)
        {
            if (index != 0 && index == primaryIndex) continue;
            Vector3 radiance = LocalRadiance(point, point, light, color, out Vector3 direction, containingModels);
            for (int sample = 0; sample < result.Length; sample++)
                result[sample] += radiance * MathF.Max(0, Vector3.Dot(_lightGridNormals[sample], direction));
        }
        if (primaryIndex == 0 || primaryIndex > SunCount)
        {
            float visibility = SunVisibility(point, Vector3.Zero, sunIndex);
            for (int sample = 0; sample < result.Length; sample++)
                result[sample] += SunColor(sunIndex) *
                    (visibility * MathF.Max(0, Vector3.Dot(_lightGridNormals[sample], SunDirection(sunIndex))));
        }
    }

    internal void BakeLightGridSamples(Vector3[] points, byte[] primaryIndices, Vector3[] irradiance,
        int count, GpuRayTraversal? traversal, ParallelOptions parallelOptions)
    {
        const int directions = SkySampleCount;
        int samples = _lightGridNormals.Length;
        var rays = ArrayPool<GpuRayTraversal.Ray>.Shared.Rent(count * directions);
        var hits = ArrayPool<RayHit>.Shared.Rent(count * directions * GpuRayTraversal.ResultStride);
        var cpuOnly = ArrayPool<bool>.Shared.Rent(count);
        try
        {
            bool ready = false;
            if (traversal is not null)
            {
                for (int sample = 0; sample < count; sample++)
                {
                    cpuOnly[sample] = IsOnSolidBoundary(points[sample]) || IsNearWorldSurface(points[sample]);
                    for (int direction = 0; direction < directions; direction++)
                        rays[sample * directions + direction] = new(points[sample], _skyDirections[direction]);
                }
                ready = traversal.TrySubmit(rays.AsSpan(0, count * directions), shadow: false,
                    CancellationToken) && traversal.TryRead(hits, CancellationToken);
            }
            Parallel.For(0, count, parallelOptions, sample =>
                DiffuseIrradianceDirections(points[sample], irradiance.AsSpan(sample * samples, samples),
                    primaryIndices[sample], ready && !cpuOnly[sample]
                        ? hits.AsSpan(sample * directions * GpuRayTraversal.ResultStride,
                            directions * GpuRayTraversal.ResultStride)
                        : default));
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(cpuOnly);
            ArrayPool<RayHit>.Shared.Return(hits);
            ArrayPool<GpuRayTraversal.Ray>.Shared.Return(rays);
        }
    }

    private Vector3 LocalRadiance(Vector3 point, Vector3 shadowOrigin, MapLight light, Vector3 color,
        out Vector3 direction, HashSet<int>? containingModels = null)
    {
        direction = DirectionToLight(point, light.Origin, out double distance);
        float attenuation = light.IsPrimary ? PrimaryLocalLightProfile.Sample((float)(distance / light.Radius)) :
            (float)Math.Max(1 - distance / light.Radius, 0);
        if (attenuation <= 0 || distance == 0) return Vector3.Zero;
        // Match the preview's finite point-source diffuse direction at its origin.
        if (distance < 0.0001) direction *= (float)(distance / 0.0001);
        if (light.IsSpotlight)
        {
            float cosine = Vector3.Dot(light.Direction, -direction);
            if (light.IsPrimary)
                attenuation *= PrimaryLocalLightProfile.SpotAttenuation(cosine,
                    MathF.Cos(light.InnerAngle), MathF.Cos(light.OuterAngle), light.Exponent);
            else
            {
                float angle = MathF.Acos(Math.Clamp(cosine, -1, 1));
                if (angle >= light.OuterAngle) return Vector3.Zero;
                if (light.Exponent > 0)
                    attenuation *= MathF.Pow(Math.Clamp((light.OuterAngle - angle) /
                        (light.OuterAngle - light.InnerAngle), 0, 1), light.Exponent);
            }
        }
        if (attenuation <= 0) return Vector3.Zero;

        Vector3 shadowDirection = DirectionToLight(shadowOrigin, light.Origin, out double shadowDistance);
        if (ModelOccludes(shadowOrigin, shadowDirection, shadowDistance, containingModels)) return Vector3.Zero;
        if (WorldOccludes(shadowOrigin, shadowDirection, shadowDistance)) return Vector3.Zero;
        return color * attenuation;
    }

    private static Vector3 DirectionToLight(Vector3 point, Vector3 origin, out double distance)
    {
        double x = (double)origin.X - point.X, y = (double)origin.Y - point.Y, z = (double)origin.Z - point.Z;
        distance = Math.Sqrt(x * x + y * y + z * z);
        return distance == 0 ? Vector3.Zero : new Vector3((float)(x / distance), (float)(y / distance), (float)(z / distance));
    }

    internal Vector3 CaptureRadiance(Vector3 origin, Vector3 direction,
        ReadOnlySpan<RayHit> candidates = default)
    {
        Vector3 radiance = Vector3.Zero;
        float transmission = 1;
        var (hits, count) = TraceCandidates(origin, direction, sampleColor: true, candidates);
        try
        {
            for (int index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (IsSky(hit.Face)) return radiance + ReadSky(hit.Face, direction) * transmission;
                Vector3 point = hit.Point;
                Vector3 normal = hit.Normal;
                if (Vector3.Dot(normal, direction) > 0) normal = -normal;
                Vector3 color = new Vector3(hit.Texel.X, hit.Texel.Y, hit.Texel.Z) *
                    new Vector3(hit.Color.X, hit.Color.Y, hit.Color.Z);
                byte sunIndex = Polygons[hit.Face].SunPrimaryLightIndex;
                Vector3 lighting = DiffuseIrradiance(point, normal, sunIndex) + SunColor(sunIndex) *
                    (MathF.Max(0, Vector3.Dot(normal, SunDirection(sunIndex))) * SunVisibility(point, normal, sunIndex));
                // Native lit alpha materials premultiply their output. Composite the
                // diffuse capture using the texture and painted vertex alpha together.
                radiance += color * color * lighting * (transmission * hit.Opacity);
                transmission *= 1 - hit.Opacity;
                if (transmission <= 0) break;
            }
            return radiance;
        }
        finally
        {
            ArrayPool<SurfaceHit>.Shared.Return(hits);
        }
    }

    internal void CaptureRadianceBatch(Vector3 origin, Vector3[] directions, Vector3[] radiance,
        GpuRayTraversal? traversal, ParallelOptions parallelOptions)
    {
        if (radiance.Length < directions.Length)
            throw new ArgumentException("The reflection result array is too small.", nameof(radiance));
        var rays = ArrayPool<GpuRayTraversal.Ray>.Shared.Rent(GpuRayTraversal.MaximumRayCount);
        var hits = ArrayPool<RayHit>.Shared.Rent(
            GpuRayTraversal.MaximumRayCount * GpuRayTraversal.ResultStride);
        try
        {
            for (int first = 0; first < directions.Length; first += GpuRayTraversal.MaximumRayCount)
            {
                int batchFirst = first;
                int count = Math.Min(GpuRayTraversal.MaximumRayCount, directions.Length - first);
                bool ready = false;
                if (traversal is not null)
                {
                    for (int sample = 0; sample < count; sample++)
                        rays[sample] = new(origin, directions[first + sample]);
                    ready = traversal.TrySubmit(rays.AsSpan(0, count), shadow: false, CancellationToken) &&
                        traversal.TryRead(hits, CancellationToken);
                }
                Parallel.For(0, count, parallelOptions, sample =>
                {
                    int index = batchFirst + sample;
                    int row = sample * GpuRayTraversal.ResultStride;
                    int hitCount = ready ? hits[row].Surface : -1;
                    radiance[index] = hitCount == 0 ? Vector3.Zero :
                        CaptureRadiance(origin, directions[index], hitCount > 0 &&
                            hitCount < GpuRayTraversal.ResultStride
                                ? hits.AsSpan(row + 1, hitCount) : default);
                });
            }
        }
        finally
        {
            ArrayPool<RayHit>.Shared.Return(hits);
            ArrayPool<GpuRayTraversal.Ray>.Shared.Return(rays);
        }
    }

    private Vector3 SkyRadiance(Vector3 point, Vector3 direction, HashSet<int>? containingModels = null,
        ReadOnlySpan<RayHit> candidates = default)
    {
        if (ModelOccludes(point, direction, double.PositiveInfinity, containingModels)) return Vector3.Zero;
        float transmission = 1;
        var (hits, count) = TraceCandidates(point, direction, sampleColor: false, candidates);
        try
        {
            for (int index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (IsSky(hit.Face)) return ReadSky(hit.Face, direction) * transmission;
                transmission *= 1 - hit.Opacity;
                if (transmission <= 0) break;
            }
            return Vector3.Zero;
        }
        finally
        {
            ArrayPool<SurfaceHit>.Shared.Return(hits);
        }
    }

    private Vector3 DiffuseRayRadiance(Vector3 origin, Vector3 direction, bool indirectOnly,
        HashSet<int>? containingModels = null, ReadOnlySpan<RayHit> candidates = default, float ambientScale = 1)
    {
        // Direct sampling runs before the field is frozen. The later receivers
        // inspect one ordered world-hit stream for both sky and one diffuse bounce.
        if (!_directFacesFrozen)
            return indirectOnly ? Vector3.Zero : SkyRadiance(origin, direction, containingModels, candidates) * ambientScale;

        float transmission = 1;
        var (hits, count) = TraceCandidates(origin, direction, sampleColor: true, candidates);
        try
        {
            for (int index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (IsSky(hit.Face))
                    return indirectOnly || ModelOccludes(origin, direction, double.PositiveInfinity, containingModels)
                        ? Vector3.Zero : ReadSky(hit.Face, direction) * (transmission * ambientScale);
                if (_directFaces[hit.Face] is { } directFace)
                {
                    MapRenderSurface surface = Polygons[hit.Face];
                    // The direct field was baked on the face's authored side.
                    // A reverse-side hit still blocks the ray but cannot emit it.
                    if (Vector3.Dot(surface.Normal, direction) >= 0) return Vector3.Zero;
                    if (ModelOccludes(origin, direction, hit.Distance, containingModels)) return Vector3.Zero;
                    Vector3 point = hit.Point;
                    Vector3 normal = hit.Normal;
                    if (Vector3.Dot(normal, surface.Normal) <= 0) return Vector3.Zero;
                    Vector3 color = Vector3.Clamp(new Vector3(hit.Texel.X, hit.Texel.Y, hit.Texel.Z) *
                        new Vector3(hit.Color.X, hit.Color.Y, hit.Color.Z), Vector3.Zero, Vector3.One);
                    DirectSample direct = directFace.Sample(point);
                    byte sunIndex = surface.SunPrimaryLightIndex;
                    Vector3 lighting = direct.Irradiance + SunColor(sunIndex) *
                        (MathF.Max(0, Vector3.Dot(normal, SunDirection(sunIndex))) * direct.SunVisibility);
                    return color * color * lighting * transmission;
                }
                // Alpha-tested holes never enter this stream; blended surfaces
                // transmit according to the same opacity and hit order as sky.
                transmission *= 1 - hit.Opacity;
                if (transmission <= 0) return Vector3.Zero;
            }
            return Vector3.Zero;
        }
        finally
        {
            ArrayPool<SurfaceHit>.Shared.Return(hits);
        }
    }

    private bool ModelOccludes(Vector3 origin, Vector3 direction, double maximumDistance, HashSet<int>? excluded)
    {
        for (int index = 0; index < _shadowModels.Length; index++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            ShadowModel model = _shadowModels[index];
            if (excluded?.Contains(index) == true) continue;
            var candidates = model.Rays.Query(origin, direction, maximumDistance, CancellationToken);
            while (candidates.MoveNext())
            {
                var (surface, material, image) = model.Triangles[candidates.Current];
                if (!material.Surface.HasShadowMapTechnique) continue;
                bool back = Vector3.Dot(surface.Normal, -direction) >= 0;
                if (material.Surface.ShadowCullFace == GfxCullFace.Back && back ||
                    material.Surface.ShadowCullFace == GfxCullFace.Front && !back) continue;
                if (!SurfaceRaycast.RayTriangle(origin, direction, surface.Vertices[0], surface.Vertices[1],
                        surface.Vertices[2], out float distance) || distance >= maximumDistance ||
                    distance <= 0.0001f && Vector3.Dot(surface.Normal, direction) >= 0) continue;
                if (image is { } pixels)
                {
                    var attributes = surface.Sample(surface.ClampToPolygon(origin + direction * distance));
                    float alpha = Math.Clamp(ReadPixel(pixels, 0, attributes.Texture.X, attributes.Texture.Y,
                        material.SamplerState).W * attributes.Color.W, 0, 1);
                    if (!PassesAlphaTest(material.Surface.ShadowAlphaTest, alpha)) continue;
                }
                return true;
            }
        }
        return false;
    }

    private HashSet<int>? ContainingModels(Vector3 point)
    {
        HashSet<int>? containing = null;
        for (int index = 0; index < _shadowModels.Length; index++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            ShadowModel model = _shadowModels[index];
            if (point.X < model.Minimum.X || point.Y < model.Minimum.Y || point.Z < model.Minimum.Z ||
                point.X > model.Maximum.X || point.Y > model.Maximum.Y || point.Z > model.Maximum.Z) continue;
            // A light-grid sample inside its receiving model must not shadow
            // itself. Solid angle distinguishes actual mesh interiors from the
            // empty space in a concave model's bounding box.
            double angle = 0;
            bool contact = false;
            foreach (var (surface, _, _) in model.Triangles)
            {
                Vector3 a = surface.Vertices[0] - point, b = surface.Vertices[1] - point, c = surface.Vertices[2] - point;
                if (SurfaceRaycast.RayTriangle(point + surface.Normal * RayOffset, -surface.Normal,
                        surface.Vertices[0], surface.Vertices[1], surface.Vertices[2], out float distance) &&
                    MathF.Abs(distance - RayOffset) <= 0.0001f)
                {
                    contact = true;
                    break;
                }
                a = Vector3.Normalize(a); b = Vector3.Normalize(b); c = Vector3.Normalize(c);
                angle += 2 * Math.Atan2(Vector3.Dot(a, Vector3.Cross(b, c)),
                    1.0 + Vector3.Dot(a, b) + Vector3.Dot(b, c) + Vector3.Dot(c, a));
            }
            if (contact || Math.Abs(angle) > 2 * Math.PI) (containing ??= []).Add(index);
        }
        return containing;
    }

    private static bool PassesAlphaTest(GfxAlphaTest? test, float alpha) => test switch
    {
        GfxAlphaTest.GreaterThanZero => alpha > 0,
        GfxAlphaTest.LessThan128 => alpha < 128f / 255,
        GfxAlphaTest.GreaterThanOrEqualTo128 => alpha >= 128f / 255,
        _ => true
    };

    private Vector3 ReadSky(int face, Vector3 direction)
    {
        (int cubeFace, float u, float v) = CubeCoordinates(direction);
        Vector4 texel = ReadPixel(_images[face], cubeFace, u, v,
            MaterialSamplerState.FilterLinear | MaterialSamplerState.ClampU | MaterialSamplerState.ClampV);
        // Native sky.hlsl multiplies sampled RGB by alpha, then squares RGB
        // before its gamma-write output stage.
        Vector3 value = new Vector3(texel.X, texel.Y, texel.Z) * texel.W;
        return value * value;
    }

    private bool WorldOccludes(Vector3 origin, Vector3 direction, double maximumDistance)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var candidates = _worldRays.Query(origin, direction, maximumDistance, CancellationToken);
        while (candidates.MoveNext())
            if (WorldSurfaceOccludes(candidates.Current, origin, direction, maximumDistance)) return true;
        return false;
    }

    private bool WorldOccludes(Vector3 origin, Vector3 direction, double maximumDistance,
        ReadOnlySpan<RayHit> candidates)
    {
        if (candidates.IsEmpty) return false;
        if (candidates[0].Distance >= 0)
        {
            foreach (RayHit hit in candidates)
            {
                CancellationToken.ThrowIfCancellationRequested();
                if (hit.Distance >= maximumDistance) continue;
                if (TryReadWorldSurface(hit.Surface, default, shadow: true, sampleColor: false,
                        out _, out _, out _, hit))
                    return true;
            }
            return false;
        }
        var query = _worldRays.Query(origin, direction, double.PositiveInfinity, CancellationToken);
        foreach (RayHit candidate in candidates)
        {
            CancellationToken.ThrowIfCancellationRequested();
            int index = candidate.Surface;
            if (query.IntersectsSurface(index) &&
                WorldSurfaceOccludes(index, origin, direction, maximumDistance)) return true;
        }
        return false;
    }

    private bool WorldSurfaceOccludes(int index, Vector3 origin, Vector3 direction, double maximumDistance)
    {
        if (!CastsSunShadow(index) ||
            !TryWorldSurfaceHit(index, origin, direction, maximumDistance, shadow: true,
                out _, out Vector3 hitPoint)) return false;
        return TryReadWorldSurface(index, hitPoint, shadow: true, sampleColor: false, out _, out _, out _);
    }

    private (SurfaceHit[] Hits, int Count) TraceCandidates(
        Vector3 origin, Vector3 direction, bool sampleColor, ReadOnlySpan<RayHit> gpuCandidates)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var hitPool = ArrayPool<SurfaceHit>.Shared;
        var hits = hitPool.Rent(16);
        int hitCount = 0;
        try
        {
            Span<RayHit> candidates = stackalloc RayHit[64];
            RayHit[]? rented = null;
            try
            {
                int candidateCount = 0;
                if (!gpuCandidates.IsEmpty)
                {
                    if (gpuCandidates[0].Distance >= 0)
                    {
                        // Native triangle hits are complete; proceed directly to material evaluation.
                        gpuCandidates.CopyTo(candidates);
                        candidateCount = gpuCandidates.Length;
                    }
                    else
                    {
                        // Candidate-only backends still require the original leaf predicate.
                        var query = _worldRays.Query(origin, direction, double.PositiveInfinity, CancellationToken);
                        foreach (RayHit candidate in gpuCandidates)
                            if (query.IntersectsSurface(candidate.Surface))
                                candidates[candidateCount++] = candidate;
                    }
                }
                else
                {
                    var query = _worldRays.Query(origin, direction, double.PositiveInfinity, CancellationToken);
                    while (query.MoveNext())
                    {
                        if (candidateCount == candidates.Length)
                        {
                            RayHit[] grown = ArrayPool<RayHit>.Shared.Rent(checked(candidateCount * 2));
                            candidates.CopyTo(grown);
                            if (rented is not null) ArrayPool<RayHit>.Shared.Return(rented);
                            rented = grown;
                            candidates = grown;
                        }
                        candidates[candidateCount++] = new(query.Current, -1);
                    }
                }
                candidates = candidates[..candidateCount];
                // Shared-boundary suppression depends on the original face order.
                if (candidates.Length > 1) candidates.Sort(static (a, b) => a.Surface.CompareTo(b.Surface));
                for (int candidate = 0; candidate < candidateCount; candidate++)
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    int index = candidates[candidate].Surface;
                    MapRenderSurface polygon = Polygons[index];
                    RayHit intersection = candidates[candidate];
                    float distance = intersection.Distance;
                    Vector3 hitPoint;
                    if (distance >= 0)
                    {
                        float b = Math.Clamp(intersection.Coordinates.X, 0, 1);
                        float c = Math.Clamp(intersection.Coordinates.Y, 0, 1 - b);
                        hitPoint = polygon.Vertices[0] * (1 - b - c) +
                            polygon.Vertices[intersection.Corner] * b + polygon.Vertices[intersection.Corner + 1] * c;
                    }
                    else if (!TryWorldSurfaceHit(index, origin, direction, double.PositiveInfinity, shadow: false,
                                 out distance, out hitPoint)) continue;
                    bool duplicate = false;
                    for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                    {
                        var hit = hits[hitIndex];
                        if (MathF.Abs(hit.Distance - distance) < 0.001f &&
                            (Polygons[hit.Face].SourceIndex == polygon.SourceIndex ||
                             polygon.SharesBoundaryAt(Polygons[hit.Face], hitPoint)))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (duplicate || !TryReadWorldSurface(index, hitPoint, shadow: false, sampleColor,
                            out Vector4 texel, out float opacity, out var attributes,
                            intersection.Distance >= 0 ? intersection : null)) continue;
                    if (hitCount == hits.Length)
                    {
                        var grown = hitPool.Rent(checked(hitCount * 2));
                        Array.Copy(hits, grown, hitCount);
                        hitPool.Return(hits);
                        hits = grown;
                    }
                    hits[hitCount++] = new(index, distance, opacity, texel, hitPoint, attributes.Color, attributes.Normal);
                }
            }
            finally
            {
                if (rented is not null) ArrayPool<RayHit>.Shared.Return(rented);
            }
            if (hitCount > 1)
                hits.AsSpan(0, hitCount).Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            // Resolve coincident overlays in native material order. Group against a
            // fixed distance so the comparison remains transitive.
            for (int start = 0; start < hitCount;)
            {
                int end = start + 1;
                while (end < hitCount && hits[end].Distance - hits[start].Distance < 0.001f) end++;
                if (end - start > 1) hits.AsSpan(start, end - start).Sort(_coincidentHitComparison);
                start = end;
            }
            return (hits, hitCount);
        }
        catch
        {
            hitPool.Return(hits);
            throw;
        }
    }

    private bool TryWorldSurfaceHit(int index, Vector3 origin, Vector3 direction, double maximumDistance, bool shadow,
        out float distance, out Vector3 hitPoint)
    {
        MapRenderSurface polygon = Polygons[index];
        MaterialSurfaceState state = _materials[index].Surface;
        // Shadow visibility is viewed from the light toward this point,
        // opposite the receiver-to-light ray used for intersection.
        bool back = Vector3.Dot(polygon.Normal, shadow ? -direction : direction) >= 0;
        GfxCullFace cull = shadow ? state.ShadowCullFace : state.CullFace;
        if (!IsSky(index) && (cull == GfxCullFace.Back && back || cull == GfxCullFace.Front && !back))
        {
            distance = 0;
            hitPoint = default;
            return false;
        }
        for (int corner = 1; corner < polygon.Vertices.Length - 1; corner++)
        {
            if (!SurfaceRaycast.RayTriangle(origin, direction, polygon.Vertices[0], polygon.Vertices[corner],
                    polygon.Vertices[corner + 1], out float candidate) || candidate >= maximumDistance) continue;
            // A receiver on a floor/wall join can start on the adjoining face.
            // Keep entering contacts so rays cannot escape through that solid.
            if (candidate <= 0.0001f && Vector3.Dot(polygon.Normal, direction) >= 0) continue;
            distance = candidate;
            // The accepted hit lies on the face; rounding a long ray's distance
            // back to float can move its reconstructed point beyond a thin edge.
            hitPoint = polygon.ClampToPolygon(origin + direction * candidate);
            return true;
        }
        distance = 0;
        hitPoint = default;
        return false;
    }

    private bool TryReadWorldSurface(int index, Vector3 hitPoint, bool shadow, bool sampleColor,
        out Vector4 texel, out float opacity,
        out (Vector2 Texture, Vector4 Color, Vector3 Normal) attributes, RayHit? completedHit = null)
    {
        attributes = default;
        // The native water shader derives its appearance from the runtime
        // simulation and reflections. Do not bake its placeholder colorMap as
        // opaque color or occlusion into editor-generated lighting and probes.
        if (_materials[index].IsWater)
        {
            texel = Vector4.Zero;
            opacity = 0;
            return false;
        }
        MapRenderSurface polygon = Polygons[index];
        MaterialSurfaceState state = _materials[index].Surface;
        texel = Vector4.One;
        opacity = 1;
        if (!IsSky(index) && (shadow && state.ShadowAlphaTest is not null ||
                !shadow && (sampleColor || state.IsBlended || state.AlphaTest is not null)))
        {
            if (completedHit is { } hit)
            {
                float b = Math.Clamp(hit.Coordinates.X, 0, 1);
                float c = Math.Clamp(hit.Coordinates.Y, 0, 1 - b);
                attributes = polygon.Sample(hit.Corner, b, c);
            }
            else
                attributes = polygon.Sample(hitPoint);
            texel = ReadPixel(_images[index], 0, attributes.Texture.X, attributes.Texture.Y, _materials[index].SamplerState);
            float alpha = Math.Clamp(texel.W * attributes.Color.W, 0, 1);
            if (!PassesAlphaTest(shadow ? state.ShadowAlphaTest : state.AlphaTest, alpha)) return false;
            opacity = !shadow && state.IsBlended ? alpha : 1;
        }
        return opacity > 0;
    }

    internal static Vector3 CubeDirection(int face, float u, float v)
    {
        float s = 2 * u - 1, t = 2 * v - 1;
        return Vector3.Normalize(face switch
        {
            0 => new Vector3(1, -t, -s), 1 => new Vector3(-1, -t, s),
            2 => new Vector3(s, 1, t), 3 => new Vector3(s, -1, -t),
            4 => new Vector3(s, -t, 1), 5 => new Vector3(-s, -t, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(face))
        });
    }

    internal static (int Face, float U, float V) CubeCoordinates(Vector3 direction)
    {
        Vector3 absolute = Vector3.Abs(direction);
        (int face, float s, float t, float major) = absolute.X >= absolute.Y && absolute.X >= absolute.Z
            ? direction.X >= 0 ? (0, -direction.Z, -direction.Y, absolute.X) : (1, direction.Z, -direction.Y, absolute.X)
            : absolute.Y >= absolute.Z
                ? direction.Y >= 0 ? (2, direction.X, direction.Z, absolute.Y) : (3, direction.X, -direction.Z, absolute.Y)
                : direction.Z >= 0 ? (4, direction.X, -direction.Y, absolute.Z) : (5, -direction.X, -direction.Y, absolute.Z);
        return (face, (s / major + 1) * 0.5f, (t / major + 1) * 0.5f);
    }

    private static Vector4 ReadPixel(ImageSourceMipLevel image, int face, float u, float v, MaterialSamplerState sampler)
    {
        bool clampU = (sampler & MaterialSamplerState.ClampU) != 0, clampV = (sampler & MaterialSamplerState.ClampV) != 0;
        u = clampU ? Math.Clamp(u, 0, 1) : u - MathF.Floor(u);
        v = clampV ? Math.Clamp(v, 0, 1) : v - MathF.Floor(v);
        float x = u * image.Width - 0.5f, y = v * image.Height - 0.5f;
        if ((sampler & MaterialSamplerState.FilterMask) == MaterialSamplerState.FilterNearest)
            return Texel((int)MathF.Floor(x + 0.5f), (int)MathF.Floor(y + 0.5f));
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        return Vector4.Lerp(Vector4.Lerp(Texel(x0, y0), Texel(x0 + 1, y0), x - x0),
            Vector4.Lerp(Texel(x0, y0 + 1), Texel(x0 + 1, y0 + 1), x - x0), y - y0);

        Vector4 Texel(int tx, int ty)
        {
            tx = clampU ? Math.Clamp(tx, 0, image.Width - 1) : (tx % image.Width + image.Width) % image.Width;
            ty = clampV ? Math.Clamp(ty, 0, image.Height - 1) : (ty % image.Height + image.Height) % image.Height;
            int offset = ((face * image.Height + ty) * image.Width + tx) * 4;
            ReadOnlySpan<byte> data = image.RgbaBytes.Span;
            return new Vector4(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]) / 255f;
        }
    }
}
