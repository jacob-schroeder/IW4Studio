using System.Numerics;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Renderer-neutral projection of one editor selection as a render-space
/// axis-aligned box. Selection identity and authored coordinate semantics
/// remain owned by the editor; render backends receive only validated visual
/// geometry.
/// </summary>
public readonly record struct MapRenderEditorSelectionOutline
{
    public MapRenderEditorSelectionOutline(
        Vector3 midPoint,
        Vector3 halfSize,
        Vector3 color)
    {
        if (!IsFinite(midPoint))
        {
            throw new ArgumentOutOfRangeException(
                nameof(midPoint),
                "Selection-outline midpoints must be finite.");
        }
        if (!IsFinite(halfSize) ||
            halfSize.X < 0f ||
            halfSize.Y < 0f ||
            halfSize.Z < 0f ||
            !HasFiniteCorners(midPoint, halfSize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfSize),
                "Selection-outline half sizes must be finite and nonnegative.");
        }
        if (!IsFinite(color) ||
            color.X < 0f || color.X > 1f ||
            color.Y < 0f || color.Y > 1f ||
            color.Z < 0f || color.Z > 1f ||
            color == Vector3.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(color),
                "Selection-outline colors must be finite, visible normalized RGB values.");
        }

        MidPoint = midPoint;
        HalfSize = halfSize;
        Color = color;
    }

    public Vector3 MidPoint { get; }

    public Vector3 HalfSize { get; }

    public Vector3 Color { get; }

    public bool IsValid =>
        IsFinite(MidPoint) &&
        IsFinite(HalfSize) &&
        HalfSize.X >= 0f &&
        HalfSize.Y >= 0f &&
        HalfSize.Z >= 0f &&
        HasFiniteCorners(MidPoint, HalfSize) &&
        IsFinite(Color) &&
        Color.X is >= 0f and <= 1f &&
        Color.Y is >= 0f and <= 1f &&
        Color.Z is >= 0f and <= 1f &&
        Color != Vector3.Zero;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool HasFiniteCorners(
        Vector3 midPoint,
        Vector3 halfSize) =>
        IsFinite(midPoint - halfSize) &&
        IsFinite(midPoint + halfSize);
}

/// <summary>
/// Canonical eight-corner/twelve-edge topology shared by renderer backends
/// and deterministic tests.
/// </summary>
internal static class MapRenderEditorSelectionOutlineGeometry
{
    internal const int CornerCount = 8;
    internal const int EdgeIndexCount = 24;

    internal static ReadOnlySpan<uint> LineIndices =>
    [
        0, 1,
        1, 2,
        2, 3,
        3, 0,
        4, 5,
        5, 6,
        6, 7,
        7, 4,
        0, 4,
        1, 5,
        2, 6,
        3, 7
    ];

    internal static void WriteCorners(
        MapRenderEditorSelectionOutline outline,
        Span<Vector3> destination)
    {
        if (!outline.IsValid)
        {
            throw new ArgumentException(
                "Selection-outline geometry requires a valid projection.",
                nameof(outline));
        }
        if (destination.Length < CornerCount)
        {
            throw new ArgumentException(
                $"Selection-outline geometry requires {CornerCount} corner slots.",
                nameof(destination));
        }

        Vector3 minimum = outline.MidPoint - outline.HalfSize;
        Vector3 maximum = outline.MidPoint + outline.HalfSize;
        destination[0] = new Vector3(minimum.X, minimum.Y, minimum.Z);
        destination[1] = new Vector3(maximum.X, minimum.Y, minimum.Z);
        destination[2] = new Vector3(maximum.X, maximum.Y, minimum.Z);
        destination[3] = new Vector3(minimum.X, maximum.Y, minimum.Z);
        destination[4] = new Vector3(minimum.X, minimum.Y, maximum.Z);
        destination[5] = new Vector3(maximum.X, minimum.Y, maximum.Z);
        destination[6] = new Vector3(maximum.X, maximum.Y, maximum.Z);
        destination[7] = new Vector3(minimum.X, maximum.Y, maximum.Z);
    }

    internal static Vector3[] CreateLineVertices(
        MapRenderEditorSelectionOutline outline)
    {
        Span<Vector3> corners = stackalloc Vector3[CornerCount];
        WriteCorners(outline, corners);
        ReadOnlySpan<uint> indices = LineIndices;
        var vertices = new Vector3[indices.Length];
        for (int index = 0; index < indices.Length; index++)
            vertices[index] = corners[checked((int)indices[index])];
        return vertices;
    }
}
