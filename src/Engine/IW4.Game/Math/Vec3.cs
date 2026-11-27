using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace IW4.Game.Math;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec3
{
    [FieldOffset(0), JsonIgnore] public InlineArray3<float> V;

    [field: FieldOffset(0)] public float X { readonly get; set; }
    [field: FieldOffset(sizeof(float))] public float Y { readonly get; set; }
    [field: FieldOffset(2 * sizeof(float))] public float Z { readonly get; set; }

    public Vec3(float x, float y, float z) : this()
    {
        X = x;
        Y = y;
        Z = z;
    }

    public readonly bool Equals(Vec3 other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);
}
