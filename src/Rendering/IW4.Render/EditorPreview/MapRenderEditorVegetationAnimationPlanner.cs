using IW4.Render.Techniques;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Conservatively identifies supported static foliage families. Material and
/// model identities must both match and the selected pass must be an alpha
/// cutout, preventing grass-named terrain and unrelated alpha materials from
/// moving.
/// </summary>
public static class MapRenderEditorVegetationAnimationPlanner
{
    public const float DefaultAmplitude = 1.5f;
    public const float DefaultAngularFrequency = 0.9f;
    public const float DefaultSpatialFrequency = 0.035f;

    public static MapRenderEditorVegetationAnimationPlan Create(
        RenderState state,
        string? materialName,
        IReadOnlyList<string> modelNames) =>
        Create(
            [state],
            materialName,
            modelNames);

    public static MapRenderEditorVegetationAnimationPlan Create(
        IReadOnlyList<RenderState> completePassStates,
        string? materialName,
        IReadOnlyList<string> modelNames)
    {
        ArgumentNullException.ThrowIfNull(completePassStates);
        ArgumentNullException.ThrowIfNull(modelNames);

        if (completePassStates.Count == 0 ||
            completePassStates.Any(state => !state.HasState) ||
            !completePassStates.Any(state => state.AlphaTestEnabled))
        {
            return Disabled(
                MapRenderEditorVegetationAnimationStatus
                    .DisabledWithoutAlphaCutoutState,
                "The selected static-model pass is not a decoded alpha cutout.");
        }

        bool materialRecognized = IsVegetationMaterial(materialName);
        bool modelsRecognized = modelNames.Count > 0 &&
            modelNames.All(IsVegetationModel);
        if (!materialRecognized || !modelsRecognized)
        {
            return Disabled(
                MapRenderEditorVegetationAnimationStatus
                    .DisabledAssetFamilyNotRecognized,
                "Static model and material identities do not both match the bounded editor vegetation families.");
        }

        return new MapRenderEditorVegetationAnimationPlan(
            MapRenderEditorVegetationAnimationStatus
                .EnabledEditorVegetationHeuristic,
            isEnabled: true,
            DefaultAmplitude,
            DefaultAngularFrequency,
            DefaultSpatialFrequency,
            "Live Preview applies small deterministic sway to a supported alpha-cutout foliage family; no authored wind state is claimed.");
    }

    public static float HeightWeight(
        float localHeight,
        float localMinimumHeight,
        float localMaximumHeight)
    {
        if (!float.IsFinite(localHeight) ||
            !float.IsFinite(localMinimumHeight) ||
            !float.IsFinite(localMaximumHeight) ||
            localMaximumHeight <= localMinimumHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(localMaximumHeight));
        }

        float normalized = Math.Clamp(
            (localHeight - localMinimumHeight) /
            (localMaximumHeight - localMinimumHeight),
            0f,
            1f);
        return normalized * normalized;
    }

    public static float SwayOffset(
        MapRenderEditorVegetationAnimationPlan plan,
        float timeSeconds,
        float worldX,
        float worldZ,
        float heightWeight)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!float.IsFinite(timeSeconds) ||
            !float.IsFinite(worldX) ||
            !float.IsFinite(worldZ) ||
            !float.IsFinite(heightWeight) ||
            heightWeight is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }
        if (!plan.IsEnabled)
            return 0f;

        float phase =
            timeSeconds * plan.AngularFrequency +
            worldX * plan.SpatialFrequency +
            worldZ * plan.SpatialFrequency * 1.37f;
        float wave = (
            MathF.Sin(phase) +
            0.35f * MathF.Sin(phase * 0.61f + 1.7f)) / 1.35f;
        return plan.Amplitude * heightWeight * wave;
    }

    private static bool IsVegetationModel(string? name)
    {
        string leaf = Leaf(name);
        return leaf.StartsWith("foliage_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVegetationMaterial(string? name)
    {
        string leaf = Leaf(name);
        return leaf.StartsWith("mtl_foliage_", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leaf, "mtl_drygrass", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(leaf, "mtl_lightgrass", StringComparison.OrdinalIgnoreCase);
    }

    private static string Leaf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        int separator = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        return separator >= 0 ? name[(separator + 1)..] : name;
    }

    private static MapRenderEditorVegetationAnimationPlan Disabled(
        MapRenderEditorVegetationAnimationStatus status,
        string reason) =>
        new(
            status,
            isEnabled: false,
            amplitude: 0f,
            angularFrequency: 0f,
            spatialFrequency: 0f,
            reason);
}
