using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

/// <summary>
/// Projects semantic game-space selection bounds into a renderer-only visual.
/// A matching transform draft owns the effective bounds so the outline follows
/// direct manipulation without creating a semantic revision.
/// </summary>
internal static class WorldViewportSelectionOutlineProjection
{
    private const float MinimumInflation = 0.5f;
    private const float RelativeInflation = 0.0025f;
    private static readonly Vector3 AccentColor =
        new(0.46f, 1f, 0.02f);

    internal static bool TryCreate(
        MapObjectId selectedObjectId,
        MapBounds? semanticBounds,
        IWorldViewportTranslationTool? translationTool,
        out MapRenderEditorSelectionOutline outline)
    {
        if (!TryResolveBounds(
                selectedObjectId,
                semanticBounds,
                translationTool,
                out MapBounds bounds))
        {
            outline = default;
            return false;
        }

        float largestHalfSize = MathF.Max(
            bounds.HalfSize.X,
            MathF.Max(bounds.HalfSize.Y, bounds.HalfSize.Z));
        float inflation = MathF.Max(
            MinimumInflation,
            largestHalfSize * RelativeInflation);
        MapVector3 visualHalfSize = new(
            bounds.HalfSize.X + inflation,
            bounds.HalfSize.Y + inflation,
            bounds.HalfSize.Z + inflation);
        if (!visualHalfSize.IsFinite)
        {
            outline = default;
            return false;
        }

        Vector3 renderMidPoint =
            WorldViewportCoordinateSpace.GameToRender(bounds.MidPoint);
        Vector3 renderHalfSize = new(
            visualHalfSize.X,
            visualHalfSize.Z,
            visualHalfSize.Y);
        if (!IsFinite(renderMidPoint - renderHalfSize) ||
            !IsFinite(renderMidPoint + renderHalfSize))
        {
            outline = default;
            return false;
        }
        outline = new MapRenderEditorSelectionOutline(
            renderMidPoint,
            renderHalfSize,
            AccentColor);
        return true;
    }

    internal static bool TryResolveBounds(
        MapObjectId selectedObjectId,
        MapBounds? semanticBounds,
        IWorldViewportTranslationTool? translationTool,
        out MapBounds bounds)
    {
        MapBounds? effectiveBounds =
            translationTool is not null &&
            translationTool.TargetObjectId == selectedObjectId
                ? translationTool.Bounds
                : semanticBounds;
        return TryValidate(effectiveBounds, out bounds);
    }

    private static bool TryValidate(
        MapBounds? candidate,
        out MapBounds bounds)
    {
        if (candidate is not { } value ||
            !value.IsFinite ||
            value.HalfSize.X < 0f ||
            value.HalfSize.Y < 0f ||
            value.HalfSize.Z < 0f)
        {
            bounds = default;
            return false;
        }

        bounds = value;
        return true;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
