using System.Buffers;
using System.Numerics;
using IW4.AssetExchange.SourceFormat.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Codecs.GfxMap;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Compilation.Lighting;

/// <summary>CPU sampling of authored world surfaces and sky images for the direct-light bake.</summary>
internal sealed class BrushLightingScene
{
    private const int SkySampleCount = 48;
    private const float RayOffset = 0.0625f;
    private readonly MaterialSource[] _materials;
    private readonly ImageSourceMipLevel[] _images;
    private readonly Vector3[] _skyDirections;
    private readonly MapBrush[] _solidBrushes;
    private readonly Vector3 _sunColor;
    private readonly (MapLight Light, Vector3 LinearColor)[] _localLights;
    private readonly ShadowModel[] _shadowModels;
    private readonly LightingRayHierarchy _worldRays;

    private sealed record ShadowModel(Vector3 Minimum, Vector3 Maximum,
        (MapRenderSurface Surface, MaterialSource Material, ImageSourceMipLevel? Image)[] Triangles,
        LightingRayHierarchy Rays);

    internal BrushLightingScene(MapDocument document, IReadOnlyList<MapRenderSurface> polygons,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        CancellationToken cancellationToken = default)
    {
        Polygons = polygons;
        CancellationToken = cancellationToken;
        if (!MapSunProperties.TryRead(document.World, out MapSunProperties? source, out string? error) || source is not { } sun)
            throw new InvalidDataException(error ?? "The lighting bake requires authored sunlight.");
        SunDirection = sun.Direction;
        Vector3 sunColor = sun.Color * sun.Intensity;
        _sunColor = new Vector3(GfxColorCodec.GammaToLinear(sunColor.X), GfxColorCodec.GammaToLinear(sunColor.Y), GfxColorCodec.GammaToLinear(sunColor.Z));
        var localLights = new List<(MapLight, Vector3)>();
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
            localLights.Add((light, color));
        }
        _localLights = localLights.ToArray();
        _materials = new MaterialSource[polygons.Count];
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
            if (material.IsSky) continue;
            foreach (Vector3 point in polygon.Vertices)
            {
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
        }
        if (!BrushGeometry.IsFinite(minimum) || !BrushGeometry.IsFinite(maximum))
            throw new InvalidDataException("Lighting requires finite non-sky brush bounds.");
        _solidBrushes = document.World.Brushes.Where(brush => brush.Faces.All(face =>
            materials.TryGetValue(face.Material, out var material) && !material.IsSky &&
            !material.Surface.IsBlended && material.Surface.AlphaTest is null)).ToArray();
        var shadowModels = new List<ShadowModel>();
        foreach (MapEntity entity in document.Entities.Where(entity => entity.ClassName == "misc_model"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MapStaticModelCompiler.CastsShadow(entity)) continue;
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
        for (int index = 0; index < _skyDirections.Length; index++)
        {
            float z = 1 - 2 * (index + 0.5f) / _skyDirections.Length;
            float angle = index * 2.39996323f;
            float radius = MathF.Sqrt(1 - z * z);
            _skyDirections[index] = new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, z);
        }
    }

    internal IReadOnlyList<MapRenderSurface> Polygons { get; }
    internal CancellationToken CancellationToken { get; }
    internal Vector3 Minimum { get; }
    internal Vector3 Maximum { get; }
    internal Vector3 SunDirection { get; }
    internal bool IsSky(int face) => _materials[face].IsSky;

    internal bool IsInsideSolid(Vector3 point, float inset = RayOffset) => _solidBrushes.Any(brush =>
        brush.Faces.All(face => BrushGeometry.Dot(face.Normal, point - face.A) < -inset));

    internal bool CastsSunShadow(int face) => !IsSky(face) && _materials[face].Surface.HasShadowMapTechnique;

    internal float SunVisibility(Vector3 point, Vector3 normal)
    {
        float transmission = 1;
        Vector3 origin = point + normal * RayOffset;
        if (ModelOccludes(origin, SunDirection, double.PositiveInfinity,
                normal == Vector3.Zero ? ContainingModels(point) : null)) return 0;
        foreach (var hit in Trace(origin, SunDirection, shadow: true))
        {
            transmission *= 1 - hit.Opacity;
            if (transmission <= 0) return 0;
        }
        return transmission;
    }

    internal Vector3 DiffuseIrradiance(Vector3 point, Vector3 normal)
    {
        Vector3 sum = Vector3.Zero;
        float totalWeight = 0;
        Vector3 origin = point + normal * RayOffset;
        // A face can continue underneath an adjoining brush. Subsamples inside
        // that solid must not see sky through its culled exit faces.
        bool insideSolid = IsInsideSolid(origin, 0);
        foreach (Vector3 direction in _skyDirections)
        {
            float cosine = MathF.Max(0, Vector3.Dot(normal, direction));
            if (cosine > 0)
            {
                if (!insideSolid) sum += SkyRadiance(origin, direction) * cosine;
                totalWeight += cosine;
            }
        }
        // Normalize spherical quadrature so a constant visible environment
        // preserves its radiance exactly; blocked directions still contribute weight.
        sum /= totalWeight;
        foreach (var (light, color) in _localLights)
        {
            Vector3 radiance = LocalRadiance(point, origin, light, color, out Vector3 direction);
            sum += radiance * MathF.Max(0, Vector3.Dot(normal, direction));
        }
        return sum;
    }

    internal Vector3[] DiffuseIrradianceDirections(Vector3 point)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var result = new Vector3[GfxLightGridCodec.SampleDirections.Count];
        var weights = new float[result.Length];
        HashSet<int>? containingModels = ContainingModels(point);
        bool insideSolid = IsInsideSolid(point, 0);
        foreach (Vector3 direction in _skyDirections)
        {
            Vector3 radiance = insideSolid ? Vector3.Zero : SkyRadiance(point, direction, containingModels);
            for (int sample = 0; sample < result.Length; sample++)
            {
                var value = GfxLightGridCodec.SampleDirections[sample];
                Vector3 normal = Vector3.Normalize(new Vector3(value.X, value.Y, value.Z));
                float weight = MathF.Max(0, Vector3.Dot(normal, direction));
                result[sample] += radiance * weight;
                weights[sample] += weight;
            }
        }
        for (int sample = 0; sample < result.Length; sample++) result[sample] /= weights[sample];
        foreach (var (light, color) in _localLights)
        {
            Vector3 radiance = LocalRadiance(point, point, light, color, out Vector3 direction, containingModels);
            for (int sample = 0; sample < result.Length; sample++)
            {
                var value = GfxLightGridCodec.SampleDirections[sample];
                Vector3 normal = Vector3.Normalize(new Vector3(value.X, value.Y, value.Z));
                result[sample] += radiance * MathF.Max(0, Vector3.Dot(normal, direction));
            }
        }
        return result;
    }

    private Vector3 LocalRadiance(Vector3 point, Vector3 shadowOrigin, MapLight light, Vector3 color,
        out Vector3 direction, HashSet<int>? containingModels = null)
    {
        direction = DirectionToLight(point, light.Origin, out double distance);
        float attenuation = (float)Math.Max(1 - distance / light.Radius, 0);
        if (attenuation <= 0 || distance == 0) return Vector3.Zero;
        // Match the preview's finite point-source diffuse direction at its origin.
        if (distance < 0.0001) direction *= (float)(distance / 0.0001);
        if (light.IsSpotlight)
        {
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(light.Direction, -direction), -1, 1));
            if (angle >= light.OuterAngle) return Vector3.Zero;
            if (light.Exponent > 0)
                attenuation *= MathF.Pow(Math.Clamp((light.OuterAngle - angle) /
                    (light.OuterAngle - light.InnerAngle), 0, 1), light.Exponent);
        }
        if (attenuation <= 0) return Vector3.Zero;

        Vector3 shadowDirection = DirectionToLight(shadowOrigin, light.Origin, out double shadowDistance);
        if (ModelOccludes(shadowOrigin, shadowDirection, shadowDistance, containingModels)) return Vector3.Zero;
        foreach (var hit in Trace(shadowOrigin, shadowDirection, shadow: true, maximumDistance: shadowDistance))
        {
            attenuation *= 1 - hit.Opacity;
            if (attenuation <= 0) return Vector3.Zero;
        }
        return color * attenuation;
    }

    private static Vector3 DirectionToLight(Vector3 point, Vector3 origin, out double distance)
    {
        double x = (double)origin.X - point.X, y = (double)origin.Y - point.Y, z = (double)origin.Z - point.Z;
        distance = Math.Sqrt(x * x + y * y + z * z);
        return distance == 0 ? Vector3.Zero : new Vector3((float)(x / distance), (float)(y / distance), (float)(z / distance));
    }

    internal Vector3 CaptureRadiance(Vector3 origin, Vector3 direction)
    {
        Vector3 radiance = Vector3.Zero;
        float transmission = 1;
        foreach (var hit in Trace(origin, direction, shadow: false))
        {
            if (IsSky(hit.Face)) return radiance + ReadSky(hit.Face, direction) * transmission;
            Vector3 point = origin + direction * hit.Distance;
            var attributes = Polygons[hit.Face].Sample(point);
            Vector3 normal = attributes.Normal;
            if (Vector3.Dot(normal, direction) > 0) normal = -normal;
            Vector3 color = new Vector3(hit.Texel.X, hit.Texel.Y, hit.Texel.Z) *
                new Vector3(attributes.Color.X, attributes.Color.Y, attributes.Color.Z);
            Vector3 lighting = DiffuseIrradiance(point, normal) + _sunColor *
                (MathF.Max(0, Vector3.Dot(normal, SunDirection)) * SunVisibility(point, normal));
            // Native lit alpha materials premultiply their output. Composite the
            // diffuse capture using the texture and painted vertex alpha together.
            radiance += color * color * lighting * (transmission * hit.Opacity);
            transmission *= 1 - hit.Opacity;
            if (transmission <= 0) break;
        }
        return radiance;
    }

    private Vector3 SkyRadiance(Vector3 point, Vector3 direction, HashSet<int>? containingModels = null)
    {
        if (ModelOccludes(point, direction, double.PositiveInfinity, containingModels)) return Vector3.Zero;
        float transmission = 1;
        foreach (var hit in Trace(point, direction, shadow: false))
        {
            if (IsSky(hit.Face)) return ReadSky(hit.Face, direction) * transmission;
            transmission *= 1 - hit.Opacity;
            if (transmission <= 0) break;
        }
        return Vector3.Zero;
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
                        surface.Vertices[2], out float distance, out _) || distance >= maximumDistance ||
                    distance <= 0.0001f && Vector3.Dot(surface.Normal, direction) >= 0) continue;
                if (image is { } pixels)
                {
                    var attributes = surface.Sample(origin + direction * distance);
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
                        surface.Vertices[0], surface.Vertices[1], surface.Vertices[2], out float distance, out _) &&
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

    private List<(int Face, float Distance, float Opacity, Vector4 Texel)> Trace(Vector3 origin, Vector3 direction, bool shadow,
        double maximumDistance = double.PositiveInfinity)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var hits = new List<(int Face, float Distance, float Opacity, Vector4 Texel)>();
        Span<int> candidates = stackalloc int[64];
        int[]? rented = null;
        try
        {
            int count = 0;
            var query = _worldRays.Query(origin, direction, maximumDistance, CancellationToken);
            while (query.MoveNext())
            {
                if (count == candidates.Length)
                {
                    int[] grown = ArrayPool<int>.Shared.Rent(checked(count * 2));
                    candidates.CopyTo(grown);
                    if (rented is not null) ArrayPool<int>.Shared.Return(rented);
                    rented = grown;
                    candidates = grown;
                }
                candidates[count++] = query.Current;
            }
            candidates = candidates[..count];
            // Shared-boundary suppression depends on the original face order.
            candidates.Sort();
            foreach (int index in candidates)
            {
                CancellationToken.ThrowIfCancellationRequested();
                if (shadow && !CastsSunShadow(index)) continue;
                MapRenderSurface polygon = Polygons[index];
                MaterialSurfaceState state = _materials[index].Surface;
                // Shadow visibility is viewed from the light toward this point,
                // opposite the receiver-to-light ray used for intersection.
                bool back = Vector3.Dot(polygon.Normal, shadow ? -direction : direction) >= 0;
                GfxCullFace cull = shadow ? state.ShadowCullFace : state.CullFace;
                if (!IsSky(index) && (cull == GfxCullFace.Back && back || cull == GfxCullFace.Front && !back))
                    continue;
                for (int corner = 1; corner < polygon.Vertices.Length - 1; corner++)
                {
                    if (!SurfaceRaycast.RayTriangle(origin, direction, polygon.Vertices[0], polygon.Vertices[corner],
                            polygon.Vertices[corner + 1], out float candidate, out _) || candidate >= maximumDistance) continue;
                    // A receiver on a floor/wall join can start on the adjoining face.
                    // Keep entering contacts so rays cannot escape through that solid.
                    if (candidate <= 0.0001f && Vector3.Dot(polygon.Normal, direction) >= 0) continue;
                    Vector3 hitPoint = origin + direction * candidate;
                    if (hits.Any(hit => MathF.Abs(hit.Distance - candidate) < 0.001f &&
                        (Polygons[hit.Face].SourceIndex == polygon.SourceIndex || polygon.SharesBoundaryAt(Polygons[hit.Face], hitPoint))))
                        break;
                    Vector4 texel = Vector4.One;
                    float opacity = 1;
                    if (!IsSky(index) && (!shadow || state.ShadowAlphaTest is not null))
                    {
                        var attributes = polygon.Sample(hitPoint);
                        texel = ReadPixel(_images[index], 0, attributes.Texture.X, attributes.Texture.Y, _materials[index].SamplerState);
                        float alpha = Math.Clamp(texel.W * attributes.Color.W, 0, 1);
                        if (!PassesAlphaTest(shadow ? state.ShadowAlphaTest : state.AlphaTest, alpha)) break;
                        opacity = !shadow && state.IsBlended ? alpha : 1;
                    }
                    if (opacity > 0) hits.Add((index, candidate, opacity, texel));
                    break;
                }
            }
        }
        finally
        {
            if (rented is not null) ArrayPool<int>.Shared.Return(rented);
        }
        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        // Resolve coincident overlays in native material order. Group against a
        // fixed distance so the comparison remains transitive.
        for (int start = 0; start < hits.Count;)
        {
            int end = start + 1;
            while (end < hits.Count && hits[end].Distance - hits[start].Distance < 0.001f) end++;
            hits.Sort(start, end - start, Comparer<(int Face, float Distance, float Opacity, Vector4 Texel)>.Create((a, b) =>
            {
                int material = _materials[b.Face].Surface.SortKey.CompareTo(_materials[a.Face].Surface.SortKey);
                return material != 0 ? material : a.Face.CompareTo(b.Face);
            }));
            start = end;
        }
        return hits;
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
