using System.Numerics;
using System.Runtime.InteropServices;

namespace IW4.Render.Geometry;

// One immutable scene and bounded in-flight ray batches. Completed hits go straight to
// material evaluation; candidate-only backends leave distance negative for CPU intersection.
public abstract class GpuRayTraversal : IDisposable
{
    public const int MaximumRayCount = 32768;
    public const int ResultStride = 17; // Header Surface is a count or status, followed by up to sixteen hits.
    public const int ShadowOccluded = -2;
    public const int ShadowUnoccluded = -3;
    public const int ShadowModelUncertain = -4;
    private const float CoordinateLimit = 1073741824f;
    private const float MinimumDirection = 1f / CoordinateLimit;

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BoundsNode(Vector3 minimum, int escape, Vector3 maximum, int surface)
    {
        public readonly Vector3 Minimum = minimum;
        public readonly int Escape = escape;
        public readonly Vector3 Maximum = maximum;
        public readonly int Surface = surface;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public readonly struct Ray(Vector3 origin, Vector3 direction)
    {
        [FieldOffset(0)] public readonly Vector3 Origin = origin;
        [FieldOffset(16)] public readonly Vector3 Direction = direction;
    }

    // A row contains either completed hits or candidate-only hits (negative distance).
    // Completed hits carry the fan corner and weights of its second/third vertices.
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    public readonly struct Hit(int surface, float distance, int corner = 0, Vector2 coordinates = default)
    {
        [FieldOffset(0)] public readonly int Surface = surface;
        [FieldOffset(4)] public readonly float Distance = distance;
        [FieldOffset(8)] public readonly int Corner = corner;
        [FieldOffset(12)] public readonly Vector2 Coordinates = coordinates;
    }

    [Flags]
    public enum SurfaceFlags : uint
    {
        Sky = 1,
        Water = 2,
        CastsShadow = 4,
        ShadowAlphaTest = 8,
        CullBack = 16,
        CullFront = 32,
        ShadowCullBack = 64,
        ShadowCullFront = 128
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public readonly struct Surface(Vector3 normal, int vertexOffset, int vertexCount, SurfaceFlags flags)
    {
        [FieldOffset(0)] public readonly Vector3 Normal = normal;
        [FieldOffset(12)] public readonly int VertexOffset = vertexOffset;
        [FieldOffset(16)] public readonly int VertexCount = vertexCount;
        [FieldOffset(20)] public readonly SurfaceFlags Flags = flags;
    }

    public abstract string Name { get; }
    public virtual int MaximumInFlightBatches => 1;
    public string? FailureReason { get; protected set; }
    public abstract bool TrySubmit(ReadOnlySpan<Ray> rays, bool shadow, CancellationToken cancellationToken);
    public abstract bool TryRead(Span<Hit> results, CancellationToken cancellationToken);
    public abstract void Dispose();

    protected static bool SupportedCoordinate(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        MathF.Abs(value.X) <= CoordinateLimit && MathF.Abs(value.Y) <= CoordinateLimit && MathF.Abs(value.Z) <= CoordinateLimit;

    protected static bool SupportedDirection(Vector3 value) => Component(value.X) && Component(value.Y) && Component(value.Z);
    private static bool Component(float value) => value == 0 ||
        float.IsFinite(value) && MathF.Abs(value) >= MinimumDirection && MathF.Abs(value) <= 1;
}
