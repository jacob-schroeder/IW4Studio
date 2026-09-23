namespace IW4.Render.EditorPreview;

public enum MapRenderEditorVegetationAnimationStatus
{
    DisabledWithoutAlphaCutoutState = 1,
    DisabledAssetFamilyNotRecognized = 2,
    EnabledEditorVegetationHeuristic = 3
}

/// <summary>
/// Explicit editor-only vegetation motion. This is a bounded visualization
/// approximation for selected static alpha-cutout model/material passes whose
/// vertex programs expose no authored wind input. Scripted animated models use
/// a separate entity/DObj/XAnim path.
/// </summary>
public sealed class MapRenderEditorVegetationAnimationPlan
{
    internal MapRenderEditorVegetationAnimationPlan(
        MapRenderEditorVegetationAnimationStatus status,
        bool isEnabled,
        float amplitude,
        float angularFrequency,
        float spatialFrequency,
        string reason)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (!float.IsFinite(amplitude) || amplitude < 0f ||
            !float.IsFinite(angularFrequency) || angularFrequency < 0f ||
            !float.IsFinite(spatialFrequency) || spatialFrequency < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (isEnabled != (status == MapRenderEditorVegetationAnimationStatus
                .EnabledEditorVegetationHeuristic) ||
            (isEnabled &&
             (amplitude <= 0f || angularFrequency <= 0f ||
              spatialFrequency <= 0f)) ||
            (!isEnabled &&
             (amplitude != 0f || angularFrequency != 0f ||
              spatialFrequency != 0f)))
        {
            throw new ArgumentException(
                "Vegetation animation status and parameters are inconsistent.");
        }

        Status = status;
        IsEnabled = isEnabled;
        Amplitude = amplitude;
        AngularFrequency = angularFrequency;
        SpatialFrequency = spatialFrequency;
        Reason = reason;
    }

    public MapRenderEditorVegetationAnimationStatus Status { get; }

    public bool IsEnabled { get; }

    public float Amplitude { get; }

    public float AngularFrequency { get; }

    public float SpatialFrequency { get; }

    public string Reason { get; }
}
