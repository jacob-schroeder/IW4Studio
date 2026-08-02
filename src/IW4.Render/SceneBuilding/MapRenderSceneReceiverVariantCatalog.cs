using System.Collections.ObjectModel;
using IW4.Render.Geometry;
using IW4.Render.Scheduling;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Strong key for one world receiver page/allocation channel.
/// </summary>
public readonly record struct MapRenderWorldReceiverVariantKey
{
    public MapRenderWorldReceiverVariantKey(
        MapRenderWorldSurfacePageMembership page,
        MapRenderTechniqueVariantAllocation allocation)
    {
        if (page is not (
                MapRenderWorldSurfacePageMembership.PageZero or
                MapRenderWorldSurfacePageMembership.PageOne))
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }
        if (!Enum.IsDefined(allocation))
            throw new ArgumentOutOfRangeException(nameof(allocation));

        Page = page;
        Allocation = allocation;
    }

    public MapRenderWorldSurfacePageMembership Page { get; }

    public MapRenderTechniqueVariantAllocation Allocation { get; }
}

/// <summary>
/// Strong key for one rigid static-model receiver page/allocation channel.
/// </summary>
public readonly record struct MapRenderStaticModelReceiverVariantKey
{
    public MapRenderStaticModelReceiverVariantKey(
        MapRenderStaticModelReceiverPage page,
        MapRenderTechniqueVariantAllocation allocation)
    {
        if (!MapRenderStaticModelReceiverRouting.IsNativeOpaquePage(page))
            throw new ArgumentOutOfRangeException(nameof(page));
        if (!Enum.IsDefined(allocation))
            throw new ArgumentOutOfRangeException(nameof(allocation));

        Page = page;
        Allocation = allocation;
    }

    public MapRenderStaticModelReceiverPage Page { get; }

    public MapRenderTechniqueVariantAllocation Allocation { get; }
}

/// <summary>
/// Scene-owned, structurally immutable receiver submissions for every exact
/// PS3 page/allocation combination. An empty channel means its exact authored
/// technique could not be materialized; callers must not substitute another
/// page, allocation, or generic preview pass.
/// </summary>
public sealed class MapRenderSceneReceiverVariantCatalog
{
    private static readonly MapRenderWorldSurfacePageMembership[] WorldPages =
    [
        MapRenderWorldSurfacePageMembership.PageZero,
        MapRenderWorldSurfacePageMembership.PageOne
    ];

    private static readonly MapRenderStaticModelReceiverPage[] StaticPages =
    [
        MapRenderStaticModelReceiverPage.StaticModelRigidPage2,
        MapRenderStaticModelReceiverPage
            .StaticModelRigidNoSunShadowPage3
    ];

    private static readonly MapRenderTechniqueVariantAllocation[] Allocations =
    [
        MapRenderTechniqueVariantAllocation.Unshadowed,
        MapRenderTechniqueVariantAllocation.ShadowMapAllocated
    ];

    private readonly IReadOnlyDictionary<
        MapRenderWorldReceiverVariantKey,
        IReadOnlyList<MapRenderTexturedBatch>> _world;
    private readonly IReadOnlyDictionary<
        MapRenderStaticModelReceiverVariantKey,
        IReadOnlyList<MapRenderInstancedTexturedBatch>> _staticModels;

    internal MapRenderSceneReceiverVariantCatalog(
        IReadOnlyDictionary<
            MapRenderWorldReceiverVariantKey,
            IReadOnlyList<MapRenderTexturedBatch>> world,
        IReadOnlyDictionary<
            MapRenderStaticModelReceiverVariantKey,
            IReadOnlyList<MapRenderInstancedTexturedBatch>> staticModels)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(staticModels);

        var worldCopy = new Dictionary<
            MapRenderWorldReceiverVariantKey,
            IReadOnlyList<MapRenderTexturedBatch>>();
        foreach (MapRenderWorldSurfacePageMembership page in WorldPages)
        foreach (MapRenderTechniqueVariantAllocation allocation in Allocations)
        {
            var key = new MapRenderWorldReceiverVariantKey(page, allocation);
            MapRenderTexturedBatch[] batches = world.TryGetValue(
                    key,
                    out IReadOnlyList<MapRenderTexturedBatch>? source)
                ? source.ToArray()
                : [];
            foreach (MapRenderTexturedBatch batch in batches)
            {
                if (batch.PickRanges.Count == 0 ||
                    batch.PickRanges.Any(range =>
                        range.Kind != MapRenderPickKind.GfxSurface ||
                        range.ObjectIndex < 0))
                {
                    throw new InvalidDataException(
                        $"World receiver channel {key} lost GfxSurface ownership.");
                }
            }
            worldCopy.Add(key, Array.AsReadOnly(batches));
        }

        var staticCopy = new Dictionary<
            MapRenderStaticModelReceiverVariantKey,
            IReadOnlyList<MapRenderInstancedTexturedBatch>>();
        foreach (MapRenderStaticModelReceiverPage page in StaticPages)
        foreach (MapRenderTechniqueVariantAllocation allocation in Allocations)
        {
            var key = new MapRenderStaticModelReceiverVariantKey(
                page,
                allocation);
            MapRenderInstancedTexturedBatch[] batches = staticModels.TryGetValue(
                    key,
                    out IReadOnlyList<MapRenderInstancedTexturedBatch>? source)
                ? source.ToArray()
                : [];
            foreach (MapRenderInstancedTexturedBatch batch in batches)
            {
                if (batch.LodIndex < 0 ||
                    batch.Instances.Count == 0)
                {
                    throw new InvalidDataException(
                        $"Static-model receiver channel {key} lost LOD/instance ownership.");
                }

                foreach (MapRenderStaticModelInstance instance in
                         batch.Instances)
                {
                    MapRenderStaticModelReceiverIdentity identity;
                    try
                    {
                        identity = new(instance, batch.LodIndex);
                    }
                    catch (ArgumentOutOfRangeException exception)
                    {
                        throw new InvalidDataException(
                            $"Static-model receiver channel {key} lost exact object/LOD/material-surface/primary-light ownership.",
                            exception);
                    }

                    if (!MapRenderStaticModelReceiverRouting
                            .CanPrepareAuthoredRegion(
                                page,
                                identity.CameraRegion))
                    {
                        throw new InvalidDataException(
                            $"Static-model receiver channel {key} contains CameraRegion {identity.CameraRegion} for object {identity.ObjectIndex}, LOD {identity.LodIndex}, material surface {identity.MaterialSurfaceIndex}; that authored region cannot use this page.");
                    }
                }
            }
            staticCopy.Add(key, Array.AsReadOnly(batches));
        }

        _world = new ReadOnlyDictionary<
            MapRenderWorldReceiverVariantKey,
            IReadOnlyList<MapRenderTexturedBatch>>(worldCopy);
        _staticModels = new ReadOnlyDictionary<
            MapRenderStaticModelReceiverVariantKey,
            IReadOnlyList<MapRenderInstancedTexturedBatch>>(staticCopy);
    }

    public IReadOnlyDictionary<
        MapRenderWorldReceiverVariantKey,
        IReadOnlyList<MapRenderTexturedBatch>> World => _world;

    public IReadOnlyDictionary<
        MapRenderStaticModelReceiverVariantKey,
        IReadOnlyList<MapRenderInstancedTexturedBatch>> StaticModels =>
        _staticModels;

    public IReadOnlyList<MapRenderTexturedBatch> GetWorldBatches(
        MapRenderWorldSurfacePageMembership page,
        MapRenderTechniqueVariantAllocation allocation) =>
        _world[new MapRenderWorldReceiverVariantKey(page, allocation)];

    public IReadOnlyList<MapRenderInstancedTexturedBatch>
        GetStaticModelBatches(
            MapRenderStaticModelReceiverPage page,
            MapRenderTechniqueVariantAllocation allocation) =>
        _staticModels[
            new MapRenderStaticModelReceiverVariantKey(page, allocation)];
}
