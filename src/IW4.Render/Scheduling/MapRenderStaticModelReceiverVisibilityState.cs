using IW4.Assets.Assets.Material;
using IW4.Render.Scheduling.Dpvs;

namespace IW4.Render.Scheduling;

/// <summary>
/// Immutable static-model receiver inputs owned by one atomic three-view DPVS
/// revision. Camera visibility comes from view zero. The OR of static-model
/// visibility in views one and two selects native page two for CameraRegion
/// zero; otherwise region zero is remapped to native page three. Authored
/// CameraRegion four always uses page three.
/// </summary>
public sealed class MapRenderStaticModelReceiverVisibilityState
{
    private readonly MapRenderWorldDpvsViewVisibility _camera;
    private readonly MapRenderWorldDpvsViewVisibility _sunShadowPartition0;
    private readonly MapRenderWorldDpvsViewVisibility _sunShadowPartition1;

    internal MapRenderStaticModelReceiverVisibilityState(
        long revision,
        MapRenderWorldDpvsViewVisibility camera,
        MapRenderWorldDpvsViewVisibility sunShadowPartition0,
        MapRenderWorldDpvsViewVisibility sunShadowPartition1)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(sunShadowPartition0);
        ArgumentNullException.ThrowIfNull(sunShadowPartition1);
        if (camera.ViewIndex != MapRenderWorldDpvsViewIndex.Camera ||
            sunShadowPartition0.ViewIndex !=
            MapRenderWorldDpvsViewIndex.SunShadowPartition0 ||
            sunShadowPartition1.ViewIndex !=
            MapRenderWorldDpvsViewIndex.SunShadowPartition1)
        {
            throw new ArgumentException(
                "Static receiver state requires camera, partition-zero, and partition-one view ownership.");
        }
        if (camera.StaticModelCount !=
                sunShadowPartition0.StaticModelCount ||
            camera.StaticModelCount !=
                sunShadowPartition1.StaticModelCount)
        {
            throw new ArgumentException(
                "All three DPVS views must describe the same static-model population.");
        }

        Revision = revision;
        StaticModelCount = camera.StaticModelCount;
        _camera = camera;
        _sunShadowPartition0 = sunShadowPartition0;
        _sunShadowPartition1 = sunShadowPartition1;
    }

    public long Revision { get; }

    public int StaticModelCount { get; }

    public MapRenderStaticModelReceiverClassification Classify(
        MapRenderStaticModelReceiverIdentity identity)
    {
        if ((uint)identity.ObjectIndex >= (uint)StaticModelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identity),
                "The receiver object index is outside this DPVS revision.");
        }

        if (!TestMsbFirstBit(
                _camera.StaticModelBitSpan,
                identity.ObjectIndex))
            return new(identity, null);

        MapRenderStaticModelReceiverPage? page =
            identity.CameraRegion switch
            {
                GfxCameraRegionType.LitOpaque => TestMsbFirstBit(
                         _sunShadowPartition0.StaticModelBitSpan,
                         identity.ObjectIndex) ||
                     TestMsbFirstBit(
                         _sunShadowPartition1.StaticModelBitSpan,
                         identity.ObjectIndex)
                    ? MapRenderStaticModelReceiverPage
                        .StaticModelRigidPage2
                    : MapRenderStaticModelReceiverPage
                        .StaticModelRigidNoSunShadowPage3,
                GfxCameraRegionType.LightMapOpaque =>
                    MapRenderStaticModelReceiverPage
                    .StaticModelRigidNoSunShadowPage3,
                _ => null
            };
        return new(identity, page);
    }

    private static bool TestMsbFirstBit(
        ReadOnlySpan<uint> words,
        int index)
    {
        uint mask = 0x8000_0000u >> (index & 31);
        return (words[index >> 5] & mask) != 0;
    }
}
