using System.Numerics;
using System.Runtime.InteropServices;

namespace IW4.Render.EditorPreview;

[StructLayout(LayoutKind.Sequential)]
public readonly struct FxPreviewVertex(
    Vector3 position, Vector3 normal, Vector2 uv, Vector4 color,
    Vector3 tangent = default, bool hasTangent = false,
    float positionW = 1f)
{
    public readonly Vector3 Position = position;
    public readonly Vector3 Normal = normal;
    public readonly Vector2 Uv = uv;
    public readonly Vector4 Color = color;
    public readonly Vector3 Tangent = tangent;
    public readonly bool HasTangent = hasTangent;
    public readonly float PositionW = positionW;
}
