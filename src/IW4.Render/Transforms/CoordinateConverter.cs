using System.Numerics;

namespace IW4.Render.Transforms;

/// <summary>
/// Converts between native game coordinates and the host viewer coordinates.
/// These two transforms are exact inverses; no camera or projection policy is
/// applied here.
/// </summary>
internal static class MapRenderCoordinateConverter
{
    internal static Matrix4x4 RenderToGameMatrix { get; } = new(
        1f, 0f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, -1f, 0f, 0f,
        0f, 0f, 0f, 1f);

    public static Vector3 GameToRenderPosition(Vector3 value) =>
        new(value.X, value.Z, -value.Y);

    public static Vector3 RenderToGamePosition(Vector3 value) =>
        new(value.X, -value.Z, value.Y);

    public static Vector3 RenderToGameUnitDirection(Vector3 value) =>
        Vector3.Normalize(RenderToGamePosition(value));
}
