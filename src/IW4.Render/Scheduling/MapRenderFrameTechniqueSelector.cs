using IW4.Render.Scheduling.Dpvs;
using IW4.Assets.Assets.Material;

namespace IW4.Render.Scheduling;

/// <summary>
/// Joins same-revision three-view page classification with independently
/// produced scene-light selector pages. It performs no readiness inference or
/// fallback technique scan.
/// </summary>
public sealed class MapRenderFrameTechniqueSelector
{
    public MapRenderFrameTechniqueSelector(
        MapRenderWorldDpvsThreeViewFrame visibility,
        MapRenderTechniqueSelectionContext techniques)
    {
        Visibility = visibility ??
            throw new ArgumentNullException(nameof(visibility));
        Techniques = techniques ??
            throw new ArgumentNullException(nameof(techniques));
        if (visibility.Revision != techniques.Revision)
        {
            throw new ArgumentException(
                "DPVS and scene-light technique state must belong to the same frame revision.");
        }
        if (techniques.SceneLights.SunShadowAtlasReady is { } ready &&
            !ReferenceEquals(ready.Frame, visibility))
        {
            throw new ArgumentException(
                "Shadow-allocated technique state must reference the exact three-view frame whose atlas completed.");
        }
    }

    public MapRenderWorldDpvsThreeViewFrame Visibility { get; }

    public MapRenderTechniqueSelectionContext Techniques { get; }

    public bool TrySelectWorldSurface(
        int surfaceIndex,
        int primaryLightIndex,
        out MapRenderFrameTechniqueSelection? selection)
    {
        if (!TryResolveWorldSurface(
                surfaceIndex,
                primaryLightIndex,
                out MapRenderFrameTechniqueSelectionValue value))
        {
            selection = null;
            return false;
        }

        selection = new(
            Visibility.Revision,
            surfaceIndex,
            primaryLightIndex,
            value.PageMembership,
            value.SurfaceType,
            value.SceneLightVariant,
            value.TechniqueSlot,
            value.ShadowMapAllocated);
        return true;
    }

    /// <summary>
    /// Allocation-free selector used by the renderer's per-surface frame walk.
    /// It retains the exact page/allocation axes of the public result without
    /// constructing one reference-type diagnostic result per visible surface.
    /// </summary>
    internal bool TryResolveWorldSurface(
        int surfaceIndex,
        int primaryLightIndex,
        out MapRenderFrameTechniqueSelectionValue selection)
    {
        MapRenderWorldSurfacePageMembership membership =
            Visibility.WorldSurfaces.Classify(surfaceIndex);
        if (membership == MapRenderWorldSurfacePageMembership.Excluded)
        {
            selection = default;
            return false;
        }

        if ((uint)primaryLightIndex >=
            (uint)Techniques.SceneLights.Selectors.SceneLightCount)
        {
            throw new ArgumentOutOfRangeException(nameof(primaryLightIndex));
        }

        GfxDrawSurfSurfaceType surfaceType = membership switch
        {
            MapRenderWorldSurfacePageMembership.PageZero =>
                GfxDrawSurfSurfaceType.Triangles,
            MapRenderWorldSurfacePageMembership.PageOne =>
                GfxDrawSurfSurfaceType.TrianglesNoSunShadow,
            _ => throw new InvalidOperationException(
                "An included world surface must own Event20 page zero or one.")
        };
        MapRenderSceneLightSelectorState sceneLights =
            Techniques.SceneLights.Selectors;
        int variant = sceneLights.GetEffectiveVariant(primaryLightIndex);
        int techniqueSlot = Techniques.GetTechniqueSlot(
            surfaceType,
            primaryLightIndex);
        selection = new(
            membership,
            surfaceType,
            variant,
            techniqueSlot,
            sceneLights.IsAlternateVariantAllocated(primaryLightIndex));
        return true;
    }

    public bool TrySelectStaticModelSurface(
        MapRenderStaticModelReceiverIdentity identity,
        out MapRenderStaticModelFrameTechniqueSelection? selection)
    {
        if (!TryResolveStaticModelSurface(
                identity,
                out MapRenderStaticModelFrameTechniqueSelectionValue value))
        {
            selection = null;
            return false;
        }

        selection = new(
            Visibility.Revision,
            identity,
            value.Page,
            value.SurfaceType,
            value.SceneLightVariant,
            value.TechniqueSlot,
            value.ShadowMapAllocated);
        return true;
    }

    /// <summary>
    /// Allocation-free static receiver selector for the renderer's immutable
    /// identity catalog. Public callers retain the richer reference result.
    /// </summary>
    internal bool TryResolveStaticModelSurface(
        MapRenderStaticModelReceiverIdentity identity,
        out MapRenderStaticModelFrameTechniqueSelectionValue selection)
    {
        MapRenderStaticModelReceiverClassification classification =
            Visibility.StaticModelReceivers.Classify(identity);
        if (classification.Page is not { } page)
        {
            selection = default;
            return false;
        }

        if ((uint)identity.PrimaryLightIndex >=
            (uint)Techniques.SceneLights.Selectors.SceneLightCount)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }

        GfxDrawSurfSurfaceType surfaceType = page switch
        {
            MapRenderStaticModelReceiverPage.StaticModelRigidPage2 =>
                GfxDrawSurfSurfaceType.StaticModelRigid,
            MapRenderStaticModelReceiverPage
                .StaticModelRigidNoSunShadowPage3 =>
                GfxDrawSurfSurfaceType.StaticModelRigidNoSunShadow,
            _ => throw new InvalidOperationException(
                "An included rigid static-model surface must own native page two or three.")
        };
        MapRenderSceneLightSelectorState sceneLights =
            Techniques.SceneLights.Selectors;
        int sceneLightVariant = sceneLights.GetEffectiveVariant(
            identity.PrimaryLightIndex);
        int techniqueSlot = Techniques.GetTechniqueSlot(
            surfaceType,
            identity.PrimaryLightIndex);
        selection = new(
            page,
            surfaceType,
            sceneLightVariant,
            techniqueSlot,
            sceneLights.IsAlternateVariantAllocated(
                identity.PrimaryLightIndex));
        return true;
    }
}

internal readonly record struct MapRenderFrameTechniqueSelectionValue(
    MapRenderWorldSurfacePageMembership PageMembership,
    GfxDrawSurfSurfaceType SurfaceType,
    int SceneLightVariant,
    int TechniqueSlot,
    bool ShadowMapAllocated);

internal readonly record struct MapRenderStaticModelFrameTechniqueSelectionValue(
    MapRenderStaticModelReceiverPage Page,
    GfxDrawSurfSurfaceType SurfaceType,
    int SceneLightVariant,
    int TechniqueSlot,
    bool ShadowMapAllocated);
