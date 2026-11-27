using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace IW4.Game.Math;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec2
{
    [FieldOffset(0), JsonIgnore] public InlineArray2<float> V;

    [FieldOffset(0)] public float a;
    [FieldOffset(sizeof(float))] public float b;

    // Weapon source files serialize the original a/b field names.
    [JsonIgnore] public float X { readonly get => a; set => a = value; }
    [JsonIgnore] public float Y { readonly get => b; set => b = value; }

    public Vec2(float x, float y) : this()
    {
        X = x;
        Y = y;
    }

    public readonly bool Equals(Vec2 other) =>
        X.Equals(other.X) && Y.Equals(other.Y);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y);
}
