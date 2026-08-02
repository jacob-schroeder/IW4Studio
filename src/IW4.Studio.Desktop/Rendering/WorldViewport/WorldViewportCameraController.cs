using System.Numerics;
using IW4.Render;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

[Flags]
internal enum WorldViewportNavigationInput
{
    None = 0,
    Forward = 1 << 0,
    Backward = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
    Up = 1 << 4,
    Down = 1 << 5,
    YawLeft = 1 << 6,
    YawRight = 1 << 7,
    Fast = 1 << 8
}

/// <summary>
/// Pure authoring-camera policy shared by the Avalonia viewport and focused
/// tests. It owns no input device, visual, renderer, or OpenGL state.
/// </summary>
internal static class WorldViewportCameraController
{
    internal const float NormalMovementUnitsPerSecond = 700f;
    internal const float FastMovementUnitsPerSecond = 2200f;
    internal const float YawRadiansPerSecond = 1.6f;
    internal const float MouseRadiansPerLogicalPixel = 0.004f;
    internal const float MaximumElapsedSeconds = 0.1f;
    private const float MaximumPitchRadians = 1.55f;

    internal static MapRenderCamera Update(
        MapRenderCamera camera,
        WorldViewportNavigationInput input,
        double elapsedSeconds)
    {
        float delta = float.IsFinite((float)elapsedSeconds)
            ? Math.Clamp((float)elapsedSeconds, 0f, MaximumElapsedSeconds)
            : 0f;
        if (delta == 0f)
            return camera;

        float yaw = camera.YawRadians;
        if (input.HasFlag(WorldViewportNavigationInput.YawLeft))
            yaw -= YawRadiansPerSecond * delta;
        if (input.HasFlag(WorldViewportNavigationInput.YawRight))
            yaw += YawRadiansPerSecond * delta;

        var orientedCamera = camera with { YawRadians = yaw };
        Vector3 movement = Vector3.Zero;
        if (input.HasFlag(WorldViewportNavigationInput.Forward))
            movement += orientedCamera.Forward;
        if (input.HasFlag(WorldViewportNavigationInput.Backward))
            movement -= orientedCamera.Forward;
        if (input.HasFlag(WorldViewportNavigationInput.Right))
            movement += orientedCamera.Right;
        if (input.HasFlag(WorldViewportNavigationInput.Left))
            movement -= orientedCamera.Right;
        if (input.HasFlag(WorldViewportNavigationInput.Up))
            movement += Vector3.UnitY;
        if (input.HasFlag(WorldViewportNavigationInput.Down))
            movement -= Vector3.UnitY;

        if (movement == Vector3.Zero)
            return orientedCamera;

        float speed = input.HasFlag(WorldViewportNavigationInput.Fast)
            ? FastMovementUnitsPerSecond
            : NormalMovementUnitsPerSecond;
        return orientedCamera with
        {
            Position = orientedCamera.Position +
                Vector3.Normalize(movement) * speed * delta
        };
    }

    internal static MapRenderCamera ApplyMouseLook(
        MapRenderCamera camera,
        Vector2 logicalPixelDelta) =>
        camera with
        {
            YawRadians = camera.YawRadians +
                logicalPixelDelta.X * MouseRadiansPerLogicalPixel,
            PitchRadians = Math.Clamp(
                camera.PitchRadians -
                    logicalPixelDelta.Y * MouseRadiansPerLogicalPixel,
                -MaximumPitchRadians,
                MaximumPitchRadians)
        };

    /// <summary>
    /// Frames renderer-neutral game-coordinate bounds without leaking renderer
    /// coordinate conventions into the semantic document.
    /// </summary>
    internal static bool TryFrameBounds(
        MapRenderCamera camera,
        MapBounds? gameBounds,
        out MapRenderCamera framed)
    {
        framed = camera;
        if (gameBounds is not
            {
                IsFinite: true,
                HalfSize.X: >= 0f,
                HalfSize.Y: >= 0f,
                HalfSize.Z: >= 0f
            } bounds)
        {
            return false;
        }

        Vector3 center =
            WorldViewportCoordinateSpace.GameToRender(bounds.MidPoint);
        Vector3 halfSize = GameToRenderHalfSize(bounds.HalfSize);
        float radius = MathF.Max(1f, halfSize.Length());
        float halfFov = Math.Clamp(
            camera.FieldOfViewRadians * 0.5f,
            0.05f,
            1.5f);
        float distance = MathF.Max(
            64f,
            radius / MathF.Tan(halfFov) * 1.25f);
        Vector3 position = center - camera.Forward * distance;
        if (!IsFinite(position))
            return false;

        framed = camera with
        {
            Position = position,
            FarPlane = MathF.Max(
                camera.FarPlane,
                distance + radius * 4f)
        };
        return true;
    }

    private static Vector3 GameToRenderHalfSize(MapVector3 value) =>
        new(value.X, value.Z, value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
