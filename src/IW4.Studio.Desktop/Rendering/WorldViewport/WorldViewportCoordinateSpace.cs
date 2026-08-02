using System.Numerics;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

/// <summary>
/// Owns the exact axis permutation between semantic IW4 game coordinates and
/// the host viewport. It applies no camera, projection, or unit conversion.
/// </summary>
internal static class WorldViewportCoordinateSpace
{
    /// <summary>
    /// Maps game <c>(x, y, z)</c> to render <c>(x, z, -y)</c>.
    /// </summary>
    internal static Vector3 GameToRender(MapVector3 value) =>
        new(value.X, value.Z, -value.Y);

    /// <summary>
    /// Maps render <c>(x, y, z)</c> to game <c>(x, -z, y)</c>.
    /// This is the exact inverse of <see cref="GameToRender"/>.
    /// </summary>
    internal static MapVector3 RenderToGame(Vector3 value) =>
        new(value.X, -value.Z, value.Y);
}
