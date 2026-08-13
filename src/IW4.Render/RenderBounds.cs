using System.Numerics;

namespace IW4.Render;

public readonly record struct RenderBounds(Vector3 Min, Vector3 Max)
{
    public static RenderBounds Empty { get; } =
        new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity));

    public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;
    public Vector3 Center => IsValid ? (Min + Max) * 0.5f : Vector3.Zero;
    public float Radius => IsValid ? MathF.Max(1f, Vector3.Distance(Min, Max) * 0.5f) : 1024f;

    public RenderBounds Include(Vector3 point)
    {
        return IsValid
            ? new RenderBounds(Vector3.Min(Min, point), Vector3.Max(Max, point))
            : new RenderBounds(point, point);
    }
}
