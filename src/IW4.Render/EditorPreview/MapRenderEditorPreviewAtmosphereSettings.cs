using System.Numerics;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Explicit editor-owned atmosphere controls. Active game fog is runtime
/// state and is not serialized in GfxWorld, so these values are never inferred
/// from <c>FogTypesAllowed</c> or presented as authored PS3 fog.
/// </summary>
public sealed record MapRenderEditorPreviewAtmosphereSettings
{
    public MapRenderEditorPreviewAtmosphereSettings(
        bool enabled,
        Vector3 fogColor,
        float startDistance,
        float endDistance,
        float maxOpacity)
    {
        if (!IsFiniteUnitColor(fogColor))
            throw new ArgumentOutOfRangeException(nameof(fogColor));
        if (!float.IsFinite(startDistance) || startDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(startDistance));
        if (!float.IsFinite(endDistance) || endDistance <= startDistance)
            throw new ArgumentOutOfRangeException(nameof(endDistance));
        if (!float.IsFinite(maxOpacity) || maxOpacity is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(maxOpacity));

        Enabled = enabled;
        FogColor = fogColor;
        StartDistance = startDistance;
        EndDistance = endDistance;
        MaxOpacity = maxOpacity;
    }

    public bool Enabled { get; }

    public Vector3 FogColor { get; }

    public float StartDistance { get; }

    public float EndDistance { get; }

    public float MaxOpacity { get; }

    private static bool IsFiniteUnitColor(Vector3 value) =>
        float.IsFinite(value.X) && value.X is >= 0f and <= 1f &&
        float.IsFinite(value.Y) && value.Y is >= 0f and <= 1f &&
        float.IsFinite(value.Z) && value.Z is >= 0f and <= 1f;
}
