using System.Numerics;
using System.Runtime.InteropServices;

namespace IW4.Render.EditorPreview;

[StructLayout(LayoutKind.Sequential)]
public readonly struct FxPreviewVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4 color)
{
    public readonly Vector3 Position = position;
    public readonly Vector3 Normal = normal;
    public readonly Vector2 Uv = uv;
    public readonly Vector4 Color = color;
}
