using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace IW4.Game.Math;

[StructLayout(LayoutKind.Explicit)]
public record struct Vec4
{
    [FieldOffset(0), JsonIgnore] public InlineArray4<float> V;

    [field: FieldOffset(0)] public float X { readonly get; set; }
    [field: FieldOffset(sizeof(float))] public float Y { readonly get; set; }
    [field: FieldOffset(2 * sizeof(float))] public float Z { readonly get; set; }
    [field: FieldOffset(3 * sizeof(float))] public float W { readonly get; set; }

    [JsonIgnore] public float R { readonly get => X; set => X = value; }
    [JsonIgnore] public float G { readonly get => Y; set => Y = value; }
    [JsonIgnore] public float B { readonly get => Z; set => Z = value; }
    [JsonIgnore] public float A { readonly get => W; set => W = value; }

    public Vec4(float x, float y, float z, float w) : this()
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public readonly bool Equals(Vec4 other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);
}
