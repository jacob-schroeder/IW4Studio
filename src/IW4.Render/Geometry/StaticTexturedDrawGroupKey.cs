using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;

namespace IW4.Render.Geometry;

/// <summary>
/// Identifies all selected technique passes for one static-model surface and
/// material. The key intentionally excludes the pass so the editor can replay
/// the complete group contiguously. The effective normal-camera selector slot
/// remains part of the key because page/light rows and emissive phase ownership
/// must not merge even when they currently resolve to the same fallback pass.
/// Reflection-probe identity is nullable and participates only when at least
/// one pass in the complete group consumes custom sampler destination 1.
/// </summary>
internal readonly record struct StaticTexturedDrawGroupKey(
    int LodIndex,
    XSurface Surface,
    MaterialAsset Material,
    int? SelectedTechniqueSlot,
    byte? ReflectionProbeIndex,
    byte SceneLightIndex = 0);
