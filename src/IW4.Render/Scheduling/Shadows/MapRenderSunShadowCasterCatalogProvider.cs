using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Render.Scheduling.Dpvs;

namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// World-scoped fast-worker caster admission. Immutable topology and two
/// disjoint partition workspaces are retained across moving-camera frames.
/// </summary>
public sealed class MapRenderSunShadowCasterCatalogProvider
{
    private static readonly ParallelOptions PartitionParallelOptions = new()
    {
        MaxDegreeOfParallelism = 2
    };

    private readonly MapRenderSunShadowCasterTopology _topology;
    private readonly MapRenderSunShadowCasterPartitionWorkspace[]
        _partitionWorkspaces;
    private readonly Action[] _partitionBuildActions;
    private readonly MapRenderWorldDpvsViewVisibility?[]
        _activePartitionVisibilities = new MapRenderWorldDpvsViewVisibility?[2];
    private readonly MapRenderSunShadowCasterPartition?[]
        _activePartitionResults = new MapRenderSunShadowCasterPartition?[2];
    private int _isActive;
    private long _buildCount;

    public MapRenderSunShadowCasterCatalogProvider(GfxWorldAsset world)
    {
        _topology = new(world);
        _partitionWorkspaces =
        [
            new(
                _topology.WorldCasterCapacity,
                _topology.StaticCasterCapacity),
            new(
                _topology.WorldCasterCapacity,
                _topology.StaticCasterCapacity)
        ];
        _partitionBuildActions =
        [
            BuildActivePartition0,
            BuildActivePartition1
        ];
    }

    public GfxWorldAsset World => _topology.World;

    public int WorldCasterCapacity => _topology.WorldCasterCapacity;

    public int StaticCasterCapacity => _topology.StaticCasterCapacity;

    public long BuildCount => Interlocked.Read(ref _buildCount);

    public MapRenderSunShadowCasterCatalogBuildResult BuildFastWorker(
        MapRenderWorldDpvsThreeViewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return BuildFastWorker(
            frame.Revision,
            frame.Camera,
            frame.SunShadowPartition0,
            frame.SunShadowPartition1);
    }

    /// <summary>
    /// Builds caster admission directly from the immutable successful result
    /// owned by a background DPVS preparation job. The caller supplies the
    /// renderer's publication revision; provider SourceRevision remains asset
    /// provenance and is not a frame identity.
    /// </summary>
    public MapRenderSunShadowCasterCatalogBuildResult BuildFastWorker(
        long revision,
        MapRenderWorldDpvsVisibilityBuildResult visibility)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(visibility);
        if (!visibility.IsSuccess)
        {
            throw new ArgumentException(
                "Fast-worker caster admission requires one successful immutable three-view DPVS result.",
                nameof(visibility));
        }

        MapRenderWorldDpvsViewVisibility camera = GetView(
            visibility,
            MapRenderWorldDpvsViewIndex.Camera);
        MapRenderWorldDpvsViewVisibility partition0 = GetView(
            visibility,
            MapRenderWorldDpvsViewIndex.SunShadowPartition0);
        MapRenderWorldDpvsViewVisibility partition1 = GetView(
            visibility,
            MapRenderWorldDpvsViewIndex.SunShadowPartition1);
        return BuildFastWorker(
            revision,
            camera,
            partition0,
            partition1);
    }

    private MapRenderSunShadowCasterCatalogBuildResult BuildFastWorker(
        long revision,
        MapRenderWorldDpvsViewVisibility camera,
        MapRenderWorldDpvsViewVisibility partition0Visibility,
        MapRenderWorldDpvsViewVisibility partition1Visibility)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (_topology.Failure is { } topologyFailure)
        {
            return MapRenderSunShadowCasterCatalogBuildResult.Failed(
                topologyFailure);
        }

        if (camera.SurfaceCount != _topology.SurfaceCount ||
            partition0Visibility.SurfaceCount != _topology.SurfaceCount ||
            partition1Visibility.SurfaceCount != _topology.SurfaceCount)
        {
            return MapRenderSunShadowCasterCatalogBuildResult.Failed(new(
                MapRenderSunShadowCasterCatalogFailureKind
                    .FrameSurfaceCardinalityMismatch,
                $"Three-view frame revision {revision} covers camera/partition surface counts {camera.SurfaceCount}/{partition0Visibility.SurfaceCount}/{partition1Visibility.SurfaceCount}, not the world's {_topology.SurfaceCount}."));
        }
        if (camera.StaticModelCount != _topology.StaticModelCount ||
            partition0Visibility.StaticModelCount !=
                _topology.StaticModelCount ||
            partition1Visibility.StaticModelCount !=
                _topology.StaticModelCount)
        {
            return MapRenderSunShadowCasterCatalogBuildResult.Failed(new(
                MapRenderSunShadowCasterCatalogFailureKind
                    .FrameStaticModelCardinalityMismatch,
                $"Three-view frame revision {revision} covers camera/partition static-model counts {camera.StaticModelCount}/{partition0Visibility.StaticModelCount}/{partition1Visibility.StaticModelCount}, not the world's {_topology.StaticModelCount}."));
        }
        if (Interlocked.CompareExchange(ref _isActive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A world-scoped sun-shadow caster provider cannot build overlapping frame catalogs.");
        }

        try
        {
            _activePartitionVisibilities[0] =
                partition0Visibility;
            _activePartitionVisibilities[1] =
                partition1Visibility;
            _activePartitionResults[0] = null;
            _activePartitionResults[1] = null;
            Parallel.Invoke(
                PartitionParallelOptions,
                _partitionBuildActions);
            MapRenderSunShadowCasterPartition partition0 =
                _activePartitionResults[0] ??
                throw new InvalidOperationException(
                    "Sun-shadow caster partition zero did not publish its immutable result.");
            MapRenderSunShadowCasterPartition partition1 =
                _activePartitionResults[1] ??
                throw new InvalidOperationException(
                    "Sun-shadow caster partition one did not publish its immutable result.");
            Interlocked.Increment(ref _buildCount);
            return MapRenderSunShadowCasterCatalogBuildResult.Succeeded(
                new MapRenderSunShadowCasterCatalog(
                    revision,
                    partition0,
                    partition1));
        }
        finally
        {
            _activePartitionVisibilities[0] = null;
            _activePartitionVisibilities[1] = null;
            _activePartitionResults[0] = null;
            _activePartitionResults[1] = null;
            Interlocked.Exchange(ref _isActive, 0);
        }
    }

    private void BuildActivePartition0() =>
        BuildActivePartition(partitionIndex: 0);

    private void BuildActivePartition1() =>
        BuildActivePartition(partitionIndex: 1);

    private void BuildActivePartition(int partitionIndex)
    {
        MapRenderWorldDpvsViewVisibility visibility =
            _activePartitionVisibilities[partitionIndex] ??
            throw new InvalidOperationException(
                $"Sun-shadow caster partition {partitionIndex} has no active visibility packet.");
        _activePartitionResults[partitionIndex] =
            BuildPartition(
                partitionIndex,
                visibility,
                _partitionWorkspaces[partitionIndex]);
    }

    private MapRenderSunShadowCasterPartition BuildPartition(
        int partitionIndex,
        MapRenderWorldDpvsViewVisibility visibility,
        MapRenderSunShadowCasterPartitionWorkspace workspace)
    {
        workspace.Begin();
        try
        {
            EnumerateWorldCasters(visibility.SurfaceBitSpan, workspace);
            EnumerateStaticCasters(
                visibility.StaticModelBitSpan,
                workspace);
            // The partition constructor snapshots active ranges before this
            // lane is released for another frame.
            return new(
                partitionIndex,
                workspace.ActiveWorldSurfaceIndices,
                workspace.ActiveStaticDrawInstances);
        }
        finally
        {
            workspace.Exit();
        }
    }

    private void EnumerateWorldCasters(
        ReadOnlySpan<uint> visibility,
        MapRenderSunShadowCasterPartitionWorkspace workspace)
    {
        ReadOnlySpan<uint> casterMask =
            _topology.SurfaceCasterMaskMsb;
        for (int wordIndex = 0;
             wordIndex < casterMask.Length;
             wordIndex++)
        {
            uint admitted = visibility[wordIndex] &
                            casterMask[wordIndex];
            int baseIndex = checked(wordIndex * 32);
            while (admitted != 0)
            {
                int indexInWord =
                    BitOperations.LeadingZeroCount(admitted);
                workspace.AddWorldSurface(
                    checked(baseIndex + indexInWord));
                admitted ^= 0x8000_0000u >> indexInWord;
            }
        }
    }

    private void EnumerateStaticCasters(
        ReadOnlySpan<uint> visibility,
        MapRenderSunShadowCasterPartitionWorkspace workspace)
    {
        ReadOnlySpan<uint> eligibility =
            _topology.StaticCasterEligibilityMsb;
        for (int wordIndex = 0;
             wordIndex < eligibility.Length;
             wordIndex++)
        {
            uint admitted = visibility[wordIndex] &
                            eligibility[wordIndex];
            int baseIndex = checked(wordIndex * 32);
            while (admitted != 0)
            {
                int indexInWord =
                    BitOperations.LeadingZeroCount(admitted);
                workspace.AddStaticDrawInstance(
                    checked(baseIndex + indexInWord));
                admitted ^= 0x8000_0000u >> indexInWord;
            }
        }
    }

    private static MapRenderWorldDpvsViewVisibility GetView(
        MapRenderWorldDpvsVisibilityBuildResult visibility,
        MapRenderWorldDpvsViewIndex viewIndex)
    {
        IReadOnlyList<MapRenderWorldDpvsViewVisibility> completedViews =
            visibility.CompletedViews;
        for (int index = 0; index < completedViews.Count; index++)
        {
            MapRenderWorldDpvsViewVisibility candidate =
                completedViews[index];
            if (candidate.ViewIndex == viewIndex)
                return candidate;
        }

        throw new ArgumentException(
            $"A successful visibility result did not retain DPVS view {viewIndex}.",
            nameof(visibility));
    }

}
