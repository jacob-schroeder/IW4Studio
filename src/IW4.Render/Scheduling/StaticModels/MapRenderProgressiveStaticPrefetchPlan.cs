using System.Numerics;

namespace IW4.Render.Scheduling.StaticModels;

/// <summary>
/// Immutable camera neighborhood used to pre-admit static-model resources
/// before an interactive camera turn or bounded translation reaches them.
/// </summary>
public sealed class MapRenderProgressiveStaticPrefetchPlan
{
    private const int MaximumYawViewCount = 4096;
    private const float TranslationPrefetchDistance = 768f;

    private readonly RenderCamera[] _cameras;

    private MapRenderProgressiveStaticPrefetchPlan(
        RenderCamera initialCamera,
        float aspectRatio,
        RenderCamera[] cameras)
    {
        InitialCamera = initialCamera;
        AspectRatio = aspectRatio;
        _cameras = cameras;
    }

    /// <summary>
    /// The exact camera supplied by the caller. Renderer-owned visibility and
    /// selected-LOD scratch must be restored to this view after walking
    /// <see cref="Cameras"/>.
    /// </summary>
    public RenderCamera InitialCamera { get; }

    public float AspectRatio { get; }

    public int CameraCount => _cameras.Length;

    /// <summary>
    /// Stable position-major neighborhood. Each of the nine positions owns
    /// the minimal evenly-spaced full-yaw ring for the supplied projection;
    /// the first entry is exactly <see cref="InitialCamera"/>.
    /// </summary>
    public ReadOnlySpan<RenderCamera> Cameras => _cameras;

    public static MapRenderProgressiveStaticPrefetchPlan CreateNeighborhood(
        RenderCamera initialCamera,
        float aspectRatio)
    {
        if (!(aspectRatio > 0f) || !float.IsFinite(aspectRatio))
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        if (!(initialCamera.FieldOfViewRadians > 0f) ||
            initialCamera.FieldOfViewRadians >= MathF.PI ||
            !float.IsFinite(initialCamera.FieldOfViewRadians))
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCamera),
                "The camera field of view must be finite and between zero and π.");
        }

        double verticalHalfFieldOfView =
            initialCamera.FieldOfViewRadians * 0.5d;
        double horizontalFieldOfView = 2d * Math.Atan(
            Math.Tan(verticalHalfFieldOfView) * aspectRatio);
        if (!(horizontalFieldOfView > 0d) ||
            !double.IsFinite(horizontalFieldOfView))
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCamera),
                "The camera projection does not produce a finite horizontal field of view.");
        }

        double requiredViewCount =
            Math.Ceiling(Math.Tau / horizontalFieldOfView);
        if (requiredViewCount > MaximumYawViewCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCamera),
                $"The camera projection requires more than " +
                $"{MaximumYawViewCount} yaw-prefetch views.");
        }
        int yawViewCount = Math.Max(
            1,
            checked((int)requiredViewCount));

        Vector3 horizontalForward = new(
            initialCamera.Forward.X,
            0f,
            initialCamera.Forward.Z);
        horizontalForward =
            horizontalForward.LengthSquared() > 0.000001f
                ? Vector3.Normalize(horizontalForward)
                : -Vector3.UnitZ;
        Vector3 horizontalRight = Vector3.Normalize(
            Vector3.Cross(horizontalForward, Vector3.UnitY));
        Span<Vector3> offsets =
        [
            Vector3.Zero,
            horizontalForward * (TranslationPrefetchDistance * 0.5f),
            -horizontalForward * (TranslationPrefetchDistance * 0.5f),
            horizontalForward * TranslationPrefetchDistance,
            -horizontalForward * TranslationPrefetchDistance,
            horizontalRight * (TranslationPrefetchDistance * 0.5f),
            -horizontalRight * (TranslationPrefetchDistance * 0.5f),
            horizontalRight * TranslationPrefetchDistance,
            -horizontalRight * TranslationPrefetchDistance
        ];
        var cameras = new RenderCamera[checked(
            yawViewCount * offsets.Length)];
        double yawStep = Math.Tau / yawViewCount;
        int outputIndex = 0;
        for (int offsetIndex = 0;
             offsetIndex < offsets.Length;
             offsetIndex++)
        {
            Vector3 position = initialCamera.Position + offsets[offsetIndex];
            for (int viewIndex = 0;
                 viewIndex < yawViewCount;
                 viewIndex++)
            {
                cameras[outputIndex++] =
                    offsetIndex == 0 && viewIndex == 0
                        ? initialCamera
                        : initialCamera with
                        {
                            Position = position,
                            YawRadians = initialCamera.YawRadians +
                                (float)(viewIndex * yawStep)
                        };
            }
        }

        return new MapRenderProgressiveStaticPrefetchPlan(
            initialCamera,
            aspectRatio,
            cameras);
    }
}
