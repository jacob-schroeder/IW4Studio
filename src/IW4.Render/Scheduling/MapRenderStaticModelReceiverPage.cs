namespace IW4.Render.Scheduling;

/// <summary>
/// The two PS3 draw-method pages used by normal-camera opaque rigid static
/// models. Numeric page ownership retains the PS3 values. The surface-type
/// names follow the matching IW4 symbols.
/// </summary>
public enum MapRenderStaticModelReceiverPage : byte
{
    StaticModelRigidPage2 = (byte)MapRenderSurfaceType.StaticModelRigid,
    StaticModelRigidNoSunShadowPage3 =
        (byte)MapRenderSurfaceType.StaticModelRigidNoSunShadow,

    // Compatibility aliases for the original catalog-local ordinal names.
    // They denote the first and second static receiver pages; they were never
    // native PS3 page numbers zero and one.
    PageZero = StaticModelRigidPage2,
    PageOne = StaticModelRigidNoSunShadowPage3
}

/// <summary>
/// Static authored-region rules that are independent from current-frame DPVS
/// membership. Dynamic region-zero page selection remains owned by
/// <see cref="MapRenderStaticModelReceiverVisibilityState"/>.
/// </summary>
public static class MapRenderStaticModelReceiverRouting
{
    public static bool IsNativeOpaquePage(
        MapRenderStaticModelReceiverPage page) =>
        (byte)page is
            (byte)MapRenderStaticModelReceiverPage.StaticModelRigidPage2 or
            (byte)MapRenderStaticModelReceiverPage
                .StaticModelRigidNoSunShadowPage3;

    /// <summary>
    /// Returns whether an authored material can ever be submitted through the
    /// requested opaque static receiver page. Region zero may dynamically use
    /// either page; authored region four always uses native page three.
    /// Regions one, two, three, and five have other or unresolved owners and
    /// fail closed here.
    /// </summary>
    public static bool CanPrepareAuthoredRegion(
        MapRenderStaticModelReceiverPage page,
        byte cameraRegion) => (byte)page switch
    {
        (byte)MapRenderStaticModelReceiverPage.StaticModelRigidPage2 =>
            cameraRegion == 0,
        (byte)MapRenderStaticModelReceiverPage
            .StaticModelRigidNoSunShadowPage3 =>
            cameraRegion is 0 or 4,
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };
}
