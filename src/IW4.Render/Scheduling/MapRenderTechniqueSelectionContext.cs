using IW4.Assets.Assets.Material;

namespace IW4.Render.Scheduling;

/// <summary>
/// One immutable draw-method and selector-page snapshot for an operational
/// render frame. All thirteen pages are generated once during construction.
/// </summary>
public sealed class MapRenderTechniqueSelectionContext
{
    private readonly byte[] _techniquePages;

    public MapRenderTechniqueSelectionContext(
        MapRenderDrawMethod drawMethod,
        MapRenderSceneLightSelectorFrameState sceneLights,
        bool flaggedTechniqueOverrideEnabled = false)
    {
        DrawMethod = drawMethod ??
            throw new ArgumentNullException(nameof(drawMethod));
        SceneLights = sceneLights ??
            throw new ArgumentNullException(nameof(sceneLights));
        FlaggedTechniqueOverrideEnabled = flaggedTechniqueOverrideEnabled;
        _techniquePages =
            new byte[MapRenderDrawMethodPageProducer.PageStorageLength];

        MapRenderDrawMethodPageProducer.Populate(
            _techniquePages,
            DrawMethod.TechniqueTableSpan,
            sceneLights.Selectors.VariantSelectorSpan,
            sceneLights.Selectors.AlternateVariantGateEnabled,
            sceneLights.Selectors.AlternateVariantBitSpan,
            sceneLights.Selectors.SceneLightCount);
    }

    public long Revision => SceneLights.Revision;

    public MapRenderDrawMethod DrawMethod { get; }

    public MapRenderSceneLightSelectorFrameState SceneLights { get; }

    public bool FlaggedTechniqueOverrideEnabled { get; }

    public ReadOnlySpan<byte> GetTechniquePage(
        GfxDrawSurfSurfaceType surfaceType)
    {
        int pageIndex = (int)surfaceType;
        if ((uint)pageIndex >= MapRenderDrawMethodPageProducer.PageCount)
            throw new ArgumentOutOfRangeException(nameof(surfaceType));

        return MapRenderDrawMethodPageProducer.GetPage(
            _techniquePages,
            pageIndex);
    }

    public int GetTechniqueSlot(
        GfxDrawSurfSurfaceType surfaceType,
        int sceneLightIndex)
    {
        if ((uint)sceneLightIndex >=
            (uint)SceneLights.Selectors.SceneLightCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));
        }

        return GetTechniquePage(surfaceType)[sceneLightIndex];
    }
}
