using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render;

public readonly record struct MapRenderBounds(Vector3 Min, Vector3 Max)
{
    public static MapRenderBounds Empty { get; } =
        new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity));

    public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;
    public Vector3 Center => IsValid ? (Min + Max) * 0.5f : Vector3.Zero;
    public float Radius => IsValid ? MathF.Max(1f, Vector3.Distance(Min, Max) * 0.5f) : 1024f;

    public MapRenderBounds Include(Vector3 point)
    {
        return IsValid
            ? new MapRenderBounds(Vector3.Min(Min, point), Vector3.Max(Max, point))
            : new MapRenderBounds(point, point);
    }
}
