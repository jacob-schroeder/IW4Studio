using System.Numerics;
using System.Runtime.InteropServices;

namespace Iw4Radiant.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SceneVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector3 color)
{
    internal readonly Vector3 Position = position;
    internal readonly Vector3 Normal = normal;
    internal readonly Vector2 Uv = uv;
    internal readonly Vector3 Color = color;
}
