namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// One native GfxWorld sun-shadow static-model candidate after partition DPVS
/// visibility and the draw-inst flags+0x26 rejection. GfxStaticModelInst and
/// GfxStaticModelDrawInst share this index; EditorPreview also retains it as
/// the rendered object's identity. GfxShadowGeometry.smodelIndex is not part
/// of this sun path.
/// </summary>
public readonly record struct MapRenderSunShadowStaticCasterIdentity(
    int StaticModelIndex,
    int DrawInstanceIndex,
    int ObjectIndex);
