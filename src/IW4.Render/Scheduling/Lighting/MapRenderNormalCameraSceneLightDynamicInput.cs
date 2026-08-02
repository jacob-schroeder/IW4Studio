using System.Numerics;

namespace IW4.Render.Scheduling.Lighting;

/// <summary>
/// Explicit normal-camera dynamic inputs read by the PS3 Event20 scene-light
/// constant path.
/// </summary>
public sealed class MapRenderNormalCameraSceneLightDynamicInput
{
    public MapRenderNormalCameraSceneLightDynamicInput(
        float diffuseColorScale,
        float specularColorScale,
        Vector2 charPrimaryLightScale,
        MapRenderSceneLightShadowAllocationState shadowAllocation,
        string producerIdentity,
        long sourceRevision)
    {
        if (!float.IsFinite(diffuseColorScale))
            throw new ArgumentOutOfRangeException(nameof(diffuseColorScale));
        if (!float.IsFinite(specularColorScale))
            throw new ArgumentOutOfRangeException(nameof(specularColorScale));
        if (!float.IsFinite(charPrimaryLightScale.X) ||
            !float.IsFinite(charPrimaryLightScale.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(charPrimaryLightScale));
        }
        ArgumentNullException.ThrowIfNull(shadowAllocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        if (sourceRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));

        DiffuseColorScale = diffuseColorScale;
        SpecularColorScale = specularColorScale;
        CharPrimaryLightScale = charPrimaryLightScale;
        ShadowAllocation = shadowAllocation;
        ProducerIdentity = producerIdentity;
        SourceRevision = sourceRevision;
    }

    public float DiffuseColorScale { get; }

    public float SpecularColorScale { get; }

    /// <summary>
    /// Retained as exact viewInfo ownership. The Event20 world route uses
    /// hero lighting false, so this float2 does not scale rows 0x01/0x02.
    /// </summary>
    public Vector2 CharPrimaryLightScale { get; }

    public MapRenderSceneLightShadowAllocationState ShadowAllocation
        { get; }

    public string ProducerIdentity { get; }

    public long SourceRevision { get; }
}
