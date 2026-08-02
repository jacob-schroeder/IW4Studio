using IW4.Assets.Assets.Material;
using IW4.Render.Materials;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Textures;

namespace IW4.Render.Geometry.Shadows;

/// <summary>
/// Exact element-buffer ownership retained for one world surface after
/// canonical-material packing. Indices are rebased to the packed vertex
/// buffer, so adjacent admitted spans may be issued as one draw run.
/// </summary>
internal readonly record struct MapRenderSunShadowWorldCasterSurfaceSpan(
    int SurfaceIndex,
    uint FirstIndex,
    uint IndexCount);

internal readonly record struct MapRenderSunShadowWorldCasterDrawRun(
    uint FirstIndex,
    uint IndexCount);

/// <summary>
/// One host upload for world caster surfaces that share the exact canonical
/// MaterialAsset object. The representative slot-2 plan remains authoritative;
/// packing validates that every repeated plan and payload agrees with it.
/// </summary>
internal sealed class MapRenderSunShadowWorldCasterPackedBatch
{
    internal MapRenderSunShadowWorldCasterPackedBatch(
        MapRenderSunShadowCasterMaterialPlan material,
        MapRenderSunShadowCasterGeometry geometry,
        MapRenderUvRoute? cutoutUvRoute,
        MapRenderTexture? cutoutTexture,
        IReadOnlyList<MapRenderSunShadowWorldCasterSurfaceSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(spans);
        if (spans.Count == 0)
        {
            throw new ArgumentException(
                "A packed world caster batch requires at least one surface span.",
                nameof(spans));
        }

        MapRenderWorldSunShadowCasterBatch.ValidateMaterialPayload(
            material,
            geometry,
            cutoutUvRoute,
            cutoutTexture);
        Material = material;
        Geometry = geometry;
        CutoutUvRoute = cutoutUvRoute;
        CutoutTexture = cutoutTexture;
        Spans = Array.AsReadOnly(spans.ToArray());
    }

    public MaterialAsset CanonicalMaterial => Material.Material;

    public MapRenderSunShadowCasterMaterialPlan Material { get; }

    public MapRenderSunShadowCasterGeometry Geometry { get; }

    public MapRenderUvRoute? CutoutUvRoute { get; }

    public MapRenderTexture? CutoutTexture { get; }

    public IReadOnlyList<MapRenderSunShadowWorldCasterSurfaceSpan> Spans
    { get; }
}

/// <summary>
/// Conservative world-caster upload packing. Reference identity is deliberate:
/// equal names, technique names, states, or textures never authorize merging
/// distinct canonical material objects.
/// </summary>
internal static class MapRenderSunShadowWorldCasterPacker
{
    internal static IReadOnlyList<MapRenderSunShadowWorldCasterPackedBatch>
        Pack(IReadOnlyList<MapRenderWorldSunShadowCasterBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);
        var groups = new Dictionary<MaterialAsset, GroupBuilder>(
            ReferenceEqualityComparer.Instance);
        var orderedGroups = new List<GroupBuilder>();
        var surfaceIndices = new HashSet<int>();

        foreach (MapRenderWorldSunShadowCasterBatch batch in batches)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (!surfaceIndices.Add(batch.SurfaceIndex))
            {
                throw new ArgumentException(
                    $"World caster surface {batch.SurfaceIndex} was materialized more than once.",
                    nameof(batches));
            }

            MaterialAsset material = batch.Material.Material;
            if (!groups.TryGetValue(material, out GroupBuilder? group))
            {
                group = new GroupBuilder(batch);
                groups.Add(material, group);
                orderedGroups.Add(group);
            }
            else
            {
                group.Add(batch);
            }
        }

        return Array.AsReadOnly(
            orderedGroups.Select(group => group.Materialize()).ToArray());
    }

    /// <summary>
    /// Compacts surface spans in packed-EBO order. A rejected span remains a
    /// real gap and therefore prevents its neighbors from being joined.
    /// </summary>
    internal static int CompactAdmittedDrawRuns(
        IReadOnlyList<MapRenderSunShadowWorldCasterSurfaceSpan> spans,
        IReadOnlySet<int> admittedSurfaceIndices,
        Span<MapRenderSunShadowWorldCasterDrawRun> destination)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(admittedSurfaceIndices);
        if (destination.Length < spans.Count)
        {
            throw new ArgumentException(
                "The draw-run destination must cover the worst-case span count.",
                nameof(destination));
        }

        int count = 0;
        for (int spanIndex = 0;
             spanIndex < spans.Count;
             spanIndex++)
        {
            MapRenderSunShadowWorldCasterSurfaceSpan span =
                spans[spanIndex];
            if (!admittedSurfaceIndices.Contains(span.SurfaceIndex))
                continue;

            if (count != 0)
            {
                MapRenderSunShadowWorldCasterDrawRun previous =
                    destination[count - 1];
                if (checked(previous.FirstIndex + previous.IndexCount) ==
                    span.FirstIndex)
                {
                    destination[count - 1] = previous with
                    {
                        IndexCount = checked(
                            previous.IndexCount + span.IndexCount)
                    };
                    continue;
                }
            }

            destination[count++] = new(
                span.FirstIndex,
                span.IndexCount);
        }

        return count;
    }

    private sealed class GroupBuilder
    {
        private readonly MapRenderWorldSunShadowCasterBatch _representative;
        private readonly List<float> _vertices = [];
        private readonly List<uint> _indices = [];
        private readonly List<MapRenderSunShadowWorldCasterSurfaceSpan>
            _spans = [];

        internal GroupBuilder(MapRenderWorldSunShadowCasterBatch first)
        {
            _representative = first;
            AddPayload(first);
        }

        internal void Add(MapRenderWorldSunShadowCasterBatch batch)
        {
            ValidateCompatible(_representative, batch);
            AddPayload(batch);
        }

        internal MapRenderSunShadowWorldCasterPackedBatch Materialize()
        {
            MapRenderSunShadowCasterGeometry representativeGeometry =
                _representative.Geometry;
            var geometry = new MapRenderSunShadowCasterGeometry(
                representativeGeometry.HasCutoutUv,
                representativeGeometry.HasVertexColor,
                _vertices,
                _indices);
            return new MapRenderSunShadowWorldCasterPackedBatch(
                _representative.Material,
                geometry,
                _representative.CutoutUvRoute,
                _representative.CutoutTexture,
                _spans);
        }

        private void AddPayload(MapRenderWorldSunShadowCasterBatch batch)
        {
            MapRenderSunShadowCasterGeometry geometry = batch.Geometry;
            uint vertexBase = checked((uint)(
                _vertices.Count / geometry.VertexFloatCount));
            uint firstIndex = checked((uint)_indices.Count);
            _vertices.AddRange(geometry.Vertices);
            foreach (uint index in geometry.Indices)
                _indices.Add(checked(index + vertexBase));
            _spans.Add(new(
                batch.SurfaceIndex,
                firstIndex,
                checked((uint)geometry.Indices.Count)));
        }

        private static void ValidateCompatible(
            MapRenderWorldSunShadowCasterBatch representative,
            MapRenderWorldSunShadowCasterBatch candidate)
        {
            MapRenderSunShadowCasterMaterialPlan first =
                representative.Material;
            MapRenderSunShadowCasterMaterialPlan next = candidate.Material;
            bool samePlanOwners =
                ReferenceEquals(first.Material, next.Material) &&
                ReferenceEquals(first.TechniqueSet, next.TechniqueSet) &&
                ReferenceEquals(first.Technique, next.Technique) &&
                ReferenceEquals(first.Pass, next.Pass) &&
                ReferenceEquals(first.Sources, next.Sources);
            bool samePayloadContract =
                first.Kind == next.Kind &&
                first.State == next.State &&
                representative.Geometry.VertexFloatCount ==
                    candidate.Geometry.VertexFloatCount &&
                representative.Geometry.HasCutoutUv ==
                    candidate.Geometry.HasCutoutUv &&
                representative.Geometry.HasVertexColor ==
                    candidate.Geometry.HasVertexColor &&
                Equals(
                    representative.CutoutUvRoute,
                    candidate.CutoutUvRoute) &&
                ReferenceEquals(
                    representative.CutoutTexture,
                    candidate.CutoutTexture) &&
                (first, next) switch
                {
                    (MapRenderSunShadowCutoutCasterMaterialPlan firstCutout,
                     MapRenderSunShadowCutoutCasterMaterialPlan nextCutout) =>
                        firstCutout.UsesVertexColor ==
                            nextCutout.UsesVertexColor &&
                        ReferenceEquals(
                            firstCutout.Sampler.Texture,
                            nextCutout.Sampler.Texture) &&
                        ReferenceEquals(
                            firstCutout.Sampler.Image,
                            nextCutout.Sampler.Image),
                    (MapRenderSunShadowOpaqueCasterMaterialPlan,
                     MapRenderSunShadowOpaqueCasterMaterialPlan) => true,
                    _ => false
                };
            if (!samePlanOwners || !samePayloadContract)
            {
                throw new InvalidOperationException(
                    $"Canonical material '{first.Material.Info.Name ?? "<unnamed>"}' produced divergent slot-2 world caster plans or payloads; packing was refused.");
            }
        }
    }
}
