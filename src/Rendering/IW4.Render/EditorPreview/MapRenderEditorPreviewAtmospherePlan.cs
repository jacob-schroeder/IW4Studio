using System.Numerics;

namespace IW4.Render.EditorPreview;

public enum MapRenderEditorPreviewAtmosphereStatus
{
    DisabledByEditorSettings = 1,
    EditorPreset = 2,
    ExplicitEditorSettings = 3
}

/// <summary>
/// Validated atmosphere inputs consumed only by the generic EditorPreview
/// shader. This plan is an editor visualization policy, not map-authored fog.
/// </summary>
public sealed class MapRenderEditorPreviewAtmospherePlan
{
    internal MapRenderEditorPreviewAtmospherePlan(
        MapRenderEditorPreviewAtmosphereStatus status,
        bool isEnabled,
        Vector3 fogColor,
        float startDistance,
        float endDistance,
        float maxOpacity,
        string reason)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = status;
        IsEnabled = isEnabled;
        FogColor = fogColor;
        StartDistance = startDistance;
        EndDistance = endDistance;
        MaxOpacity = maxOpacity;
        Reason = reason;
    }

    public MapRenderEditorPreviewAtmosphereStatus Status { get; }

    public bool IsEnabled { get; }

    public Vector3 FogColor { get; }

    public float StartDistance { get; }

    public float EndDistance { get; }

    public float MaxOpacity { get; }

    public string Reason { get; }
}
