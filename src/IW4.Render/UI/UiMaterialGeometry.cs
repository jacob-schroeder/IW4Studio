using System.Numerics;

namespace IW4.Render.UI;

/// <summary>
/// One packet-native UI vertex. The proven trivial_vertcol_simple2d contract
/// consumes position at RSX input 0, color at input 3, and UV at input 8.
/// Fragment tint semantics are texture RGBA multiplied by vertex RGBA.
/// </summary>
public readonly record struct UiMaterialVertex(
    Vector4 Position,
    Vector2 TextureCoordinate,
    Vector4 Color)
{
    public bool IsFinite =>
        IsFiniteVector(Position) &&
        IsFiniteVector(TextureCoordinate) &&
        IsFiniteVector(Color);

    private static bool IsFiniteVector(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFiniteVector(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}

/// <summary>
/// Four vertices in top-left, top-right, bottom-right, bottom-left order.
/// Backends submit the fixed 0,1,2 / 2,3,0 triangle sequence.
/// </summary>
public sealed class UiMaterialQuad
{
    private static readonly ushort[] IndicesValue = [0, 1, 2, 2, 3, 0];
    private readonly UiMaterialVertex[] _vertices;

    public UiMaterialQuad(
        UiMaterialVertex topLeft,
        UiMaterialVertex topRight,
        UiMaterialVertex bottomRight,
        UiMaterialVertex bottomLeft)
    {
        _vertices = [topLeft, topRight, bottomRight, bottomLeft];
        if (_vertices.Any(vertex => !vertex.IsFinite))
        {
            throw new ArgumentException(
                "UI material quad vertices must contain only finite values.");
        }

        Vertices = Array.AsReadOnly(_vertices);
    }

    public IReadOnlyList<UiMaterialVertex> Vertices { get; }

    public static IReadOnlyList<ushort> TriangleIndices { get; } =
        Array.AsReadOnly(IndicesValue);
}
