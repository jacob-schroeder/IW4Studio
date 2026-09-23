using System.Numerics;

namespace IW4.Render.Scheduling.Lighting;

/// <summary>
/// Exact adapted scene-light table plus normal-camera operational inputs.
/// EyeOffset is supplied again at draw time for row 0x00 so camera movement
/// cannot retain a load-time position.
/// </summary>
public sealed class MapRenderWorldEvent20SceneLightFrameInput
{
    private readonly MapRenderWorldEvent20SceneLight[] _sceneLights;

    internal MapRenderWorldEvent20SceneLightFrameInput(
        IReadOnlyList<MapRenderWorldEvent20SceneLight> sceneLights,
        Vector3 eyeOffset,
        MapRenderNormalCameraSceneLightDynamicInput dynamicInput,
        long assetPoolRevision)
    {
        ArgumentNullException.ThrowIfNull(sceneLights);
        ArgumentNullException.ThrowIfNull(dynamicInput);
        if (!IsFinite(eyeOffset))
            throw new ArgumentOutOfRangeException(nameof(eyeOffset));
        if (assetPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(assetPoolRevision));

        _sceneLights = sceneLights.ToArray();
        if (_sceneLights.Any(static light => light is null))
        {
            throw new ArgumentException(
                "Adapted scene-light storage cannot contain null rows.",
                nameof(sceneLights));
        }
        if (dynamicInput.ShadowAllocation.SceneLightCount !=
            _sceneLights.Length)
        {
            throw new ArgumentException(
                "Adapted lights and shadow-allocation state must describe the same scene-light table.",
                nameof(dynamicInput));
        }

        SceneLights = Array.AsReadOnly(_sceneLights);
        EyeOffset = eyeOffset;
        DynamicInput = dynamicInput;
        AssetPoolRevision = assetPoolRevision;
    }

    public IReadOnlyList<MapRenderWorldEvent20SceneLight> SceneLights
        { get; }

    public Vector3 EyeOffset { get; }

    public MapRenderNormalCameraSceneLightDynamicInput DynamicInput { get; }

    public long AssetPoolRevision { get; }

    public int SceneLightCount => _sceneLights.Length;

    public MapRenderWorldEvent20SceneLight GetSceneLight(
        int sceneLightIndex)
    {
        if ((uint)sceneLightIndex >= (uint)_sceneLights.Length)
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));

        return _sceneLights[sceneLightIndex];
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
