using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Operational normal-frame provider for the PS3
/// <c>rg.sunShadowFull == 1</c> path selected by R_RenderScene.
/// </summary>
public sealed class MapRenderWorldDpvsSunShadowFullFrameProvider :
    IMapRenderWorldDpvsSunShadowFrameProvider
{
    private readonly MapRenderWorldDpvsSunShadowFullSetupState? _setupState;

    public MapRenderWorldDpvsSunShadowFullFrameProvider(
        string producerIdentity,
        long sourceRevision,
        MapRenderWorldDpvsSunShadowFullSetupState? setupState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        if (sourceRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));

        ProducerIdentity = producerIdentity;
        SourceRevision = sourceRevision;
        _setupState = setupState;
    }

    public string ProducerIdentity { get; }

    public long SourceRevision { get; }

    public MapRenderWorldDpvsSunShadowFrameBuildResult Build(
        GfxWorldAsset world,
        MapRenderWorldDpvsNormalCameraFrame cameraFrame)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cameraFrame);
        if (_setupState is null)
        {
            return MapRenderWorldDpvsSunShadowFrameBuildResult.Failed(
                new(
                    MapRenderWorldDpvsSunShadowFrameFailureKind
                        .NativeSetupStateUnavailable,
                    "The renderer did not supply immutable PS3 full sun-shadow setup state."));
        }

        return MapRenderWorldDpvsSunShadowFullPlaneProducer.Build(
            ProducerIdentity,
            SourceRevision,
            world,
            cameraFrame,
            _setupState);
    }
}

