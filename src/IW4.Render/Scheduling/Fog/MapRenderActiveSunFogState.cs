using System.Numerics;

namespace IW4.Render.Scheduling.Fog;

/// <summary>
/// Active directional-sun fog fields copied to backend frame data before
/// PS3 <c>R_SetFrameFog</c> runs.
/// </summary>
public sealed class MapRenderActiveSunFogState
{
    public MapRenderActiveSunFogState(
        bool enabled,
        MapRenderBgra8Color color,
        Vector3 direction,
        float beginFadeAngleDegrees,
        float endFadeAngleDegrees,
        float scale)
    {
        if (!IsFinite(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (!float.IsFinite(beginFadeAngleDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(beginFadeAngleDegrees));
        }
        if (!float.IsFinite(endFadeAngleDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(endFadeAngleDegrees));
        }
        if (!float.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(scale));

        Enabled = enabled;
        Color = color;
        Direction = direction;
        BeginFadeAngleDegrees = beginFadeAngleDegrees;
        EndFadeAngleDegrees = endFadeAngleDegrees;
        Scale = scale;
    }

    public bool Enabled { get; }

    public MapRenderBgra8Color Color { get; }

    public Vector3 Direction { get; }

    public float BeginFadeAngleDegrees { get; }

    public float EndFadeAngleDegrees { get; }

    public float Scale { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
