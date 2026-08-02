using System.Numerics;
using IW4.Render;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

/// <summary>
/// Semantic constraint selected for a viewport translation gesture.
/// </summary>
internal enum WorldViewportTranslationConstraint
{
    ViewPlane,
    GameX,
    GameY,
    GameZ
}

/// <summary>
/// Pure policy for converting a total logical-pixel drag into an absolute
/// semantic origin. The calculation is always anchored to the gesture start,
/// so pointer sampling frequency cannot introduce cumulative drift.
/// </summary>
internal static class WorldViewportTranslationManipulator
{
    private const float MinimumMagnitudeSquared = 1e-10f;

    /// <summary>
    /// Resolves the absolute game-coordinate candidate for a drag.
    /// <paramref name="startingBounds"/> supplies the visual depth when
    /// available; otherwise <paramref name="startingOrigin"/> is used.
    /// Optional grid snapping quantizes displacement relative to the gesture
    /// start. Invalid or degenerate input fails closed to the starting origin.
    /// </summary>
    internal static MapVector3 ResolveCandidate(
        MapVector3 startingOrigin,
        MapBounds? startingBounds,
        MapRenderCamera camera,
        Vector2 viewportLogicalSize,
        Vector2 totalLogicalPixelDrag,
        WorldViewportTranslationConstraint constraint,
        float? gridSize = null)
    {
        if (!startingOrigin.IsFinite ||
            !IsValidBounds(startingBounds) ||
            !IsFinite(camera.Position) ||
            !float.IsFinite(camera.YawRadians) ||
            !float.IsFinite(camera.PitchRadians) ||
            !IsValidFieldOfView(camera.FieldOfViewRadians) ||
            !IsFinite(viewportLogicalSize) ||
            !(viewportLogicalSize.X > 0f) ||
            !(viewportLogicalSize.Y > 0f) ||
            !IsFinite(totalLogicalPixelDrag) ||
            !Enum.IsDefined(constraint) ||
            !IsValidGridSize(gridSize))
        {
            return startingOrigin;
        }

        if (totalLogicalPixelDrag == Vector2.Zero)
            return startingOrigin;

        Vector3 forward = camera.Forward;
        Vector3 right = camera.Right;
        Vector3 up = camera.Up;
        if (!IsUsableUnitVector(forward) ||
            !IsUsableUnitVector(right) ||
            !IsUsableUnitVector(up))
        {
            return startingOrigin;
        }

        MapVector3 gameDepthAnchor =
            startingBounds?.MidPoint ?? startingOrigin;
        Vector3 renderDepthAnchor =
            WorldViewportCoordinateSpace.GameToRender(gameDepthAnchor);
        float depth = Vector3.Dot(
            renderDepthAnchor - camera.Position,
            forward);
        float tangent = MathF.Tan(camera.FieldOfViewRadians * 0.5f);
        float renderUnitsPerLogicalPixel =
            2f * depth * tangent / viewportLogicalSize.Y;
        if (!(depth > 0f) ||
            !(renderUnitsPerLogicalPixel > 0f) ||
            !float.IsFinite(renderUnitsPerLogicalPixel))
        {
            return startingOrigin;
        }

        Vector3 renderDelta;
        if (constraint == WorldViewportTranslationConstraint.ViewPlane)
        {
            renderDelta =
                right *
                    (totalLogicalPixelDrag.X *
                     renderUnitsPerLogicalPixel) +
                up *
                    (-totalLogicalPixelDrag.Y *
                     renderUnitsPerLogicalPixel);
        }
        else
        {
            Vector3 renderAxis = ResolveRenderAxis(constraint);
            var projectedAxis = new Vector2(
                Vector3.Dot(renderAxis, right),
                -Vector3.Dot(renderAxis, up));
            float projectedMagnitudeSquared =
                projectedAxis.LengthSquared();
            if (!(projectedMagnitudeSquared >
                    MinimumMagnitudeSquared) ||
                !float.IsFinite(projectedMagnitudeSquared))
            {
                return startingOrigin;
            }

            float distanceAlongAxis =
                Vector2.Dot(
                    totalLogicalPixelDrag,
                    projectedAxis) *
                renderUnitsPerLogicalPixel /
                projectedMagnitudeSquared;
            if (!float.IsFinite(distanceAlongAxis))
                return startingOrigin;

            renderDelta = renderAxis * distanceAlongAxis;
        }

        if (!IsFinite(renderDelta))
            return startingOrigin;

        Vector3 startingRenderOrigin =
            WorldViewportCoordinateSpace.GameToRender(startingOrigin);
        MapVector3 unsnapped =
            WorldViewportCoordinateSpace.RenderToGame(
                startingRenderOrigin + renderDelta);
        if (!unsnapped.IsFinite)
            return startingOrigin;

        MapVector3 candidate = gridSize is { } spacing
            ? SnapDisplacement(
                startingOrigin,
                unsnapped,
                spacing,
                constraint)
            : unsnapped;
        return candidate.IsFinite
            ? candidate
            : startingOrigin;
    }

    private static Vector3 ResolveRenderAxis(
        WorldViewportTranslationConstraint constraint) =>
        constraint switch
        {
            WorldViewportTranslationConstraint.GameX =>
                Vector3.UnitX,
            WorldViewportTranslationConstraint.GameY =>
                -Vector3.UnitZ,
            WorldViewportTranslationConstraint.GameZ =>
                Vector3.UnitY,
            _ => Vector3.Zero
        };

    private static MapVector3 SnapDisplacement(
        MapVector3 startingOrigin,
        MapVector3 candidate,
        float spacing,
        WorldViewportTranslationConstraint constraint)
    {
        MapVector3 delta = candidate - startingOrigin;
        float x = constraint is
            WorldViewportTranslationConstraint.ViewPlane or
            WorldViewportTranslationConstraint.GameX
                ? Snap(delta.X, spacing)
                : 0f;
        float y = constraint is
            WorldViewportTranslationConstraint.ViewPlane or
            WorldViewportTranslationConstraint.GameY
                ? Snap(delta.Y, spacing)
                : 0f;
        float z = constraint is
            WorldViewportTranslationConstraint.ViewPlane or
            WorldViewportTranslationConstraint.GameZ
                ? Snap(delta.Z, spacing)
                : 0f;
        return startingOrigin + new MapVector3(x, y, z);
    }

    private static float Snap(float value, float spacing)
    {
        float units = value / spacing;
        if (!float.IsFinite(units))
            return float.NaN;

        return MathF.Round(
                units,
                MidpointRounding.AwayFromZero) *
            spacing;
    }

    private static bool IsValidBounds(MapBounds? bounds) =>
        bounds is null ||
        bounds is
        {
            IsFinite: true,
            HalfSize.X: >= 0f,
            HalfSize.Y: >= 0f,
            HalfSize.Z: >= 0f
        };

    private static bool IsValidFieldOfView(float radians) =>
        float.IsFinite(radians) &&
        radians > 0f &&
        radians < MathF.PI;

    private static bool IsValidGridSize(float? gridSize) =>
        !gridSize.HasValue ||
        gridSize.Value > 0f &&
        float.IsFinite(gridSize.Value);

    private static bool IsUsableUnitVector(Vector3 value) =>
        IsFinite(value) &&
        value.LengthSquared() > MinimumMagnitudeSquared;

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
