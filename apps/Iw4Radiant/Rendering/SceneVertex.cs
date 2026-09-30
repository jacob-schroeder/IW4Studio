using System.Numerics;
using System.Runtime.InteropServices;

namespace Iw4Radiant.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SceneVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4 color, byte sunIndex = 1)
{
    internal SceneVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector3 color)
        : this(position, normal, uv, new Vector4(color, 1)) { }
    internal readonly Vector3 Position = position;
    internal readonly Vector3 Normal = normal;
    internal readonly Vector2 Uv = uv;
    internal readonly Vector4 Color = color;
    // Source triangles keep the compiler's sunlight owner even when a material batch spans stages.
    internal readonly byte SunIndex = sunIndex;

    internal SceneVertex WithSunIndex(byte index) => new(Position, Normal, Uv, Color, index);
}
