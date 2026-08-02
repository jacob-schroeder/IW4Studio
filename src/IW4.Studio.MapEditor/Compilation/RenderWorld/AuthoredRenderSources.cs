using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld;

/// <summary>
/// Semantic owner of one canonical render mesh. Ownership is required source
/// data and is never inferred from material, geometry, name, bounds, or
/// proximity.
/// </summary>
public enum RenderMeshOwnershipKind
{
    StandaloneWorld = 0,
    InlineBrushModel = 1
}

public abstract class RenderMeshSourceOwnership
{
    private protected RenderMeshSourceOwnership(
        RenderMeshOwnershipKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        Kind = kind;
    }

    public RenderMeshOwnershipKind Kind { get; }
    public abstract MapObjectId? InlineBrushModelObjectId { get; }
}

public sealed class StandaloneWorldRenderMeshOwnership
    : RenderMeshSourceOwnership
{
    public StandaloneWorldRenderMeshOwnership()
        : base(RenderMeshOwnershipKind.StandaloneWorld)
    {
    }

    public override MapObjectId? InlineBrushModelObjectId => null;
}

public sealed class InlineBrushModelRenderMeshOwnership
    : RenderMeshSourceOwnership
{
    public InlineBrushModelRenderMeshOwnership(
        MapObjectId inlineBrushModelObjectId)
        : base(RenderMeshOwnershipKind.InlineBrushModel)
    {
        if (inlineBrushModelObjectId.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inlineBrushModelObjectId));
        }

        InlineBrushModelObjectId = inlineBrushModelObjectId;
    }

    public override MapObjectId? InlineBrushModelObjectId { get; }
}

/// <summary>
/// Declares which ordered side of every indexed triangle is the authored
/// front face. The M3 compiler preserves index order exactly; it does not
/// guess or reverse winding.
/// </summary>
public enum RenderTriangleWinding
{
    CounterClockwiseFrontFace = 0,
    ClockwiseFrontFace = 1
}

public readonly record struct AuthoredRenderUv
{
    public AuthoredRenderUv(float u, float v)
    {
        if (!float.IsFinite(u) || !float.IsFinite(v))
        {
            throw new ArgumentOutOfRangeException(
                nameof(u),
                "Render texture coordinates must be finite.");
        }

        U = u;
        V = v;
    }

    public float U { get; }
    public float V { get; }
    public bool IsFinite => float.IsFinite(U) && float.IsFinite(V);
}

public readonly record struct AuthoredRenderColor(
    byte Red,
    byte Green,
    byte Blue,
    byte Alpha);

/// <summary>
/// Canonical input row for the bounded TEX_1_NRM_1-like profile.
/// Normal and tangent are explicit unit vectors; silently deriving or
/// normalizing either channel would change authored semantic input.
/// </summary>
public readonly record struct AuthoredRenderVertex
{
    public const double UnitLengthTolerance = 1e-4;
    public const double OrthogonalityTolerance = 1e-4;

    public AuthoredRenderVertex(
        MapVector3 position,
        AuthoredRenderColor color,
        AuthoredRenderUv textureCoordinates,
        AuthoredRenderUv lightmapCoordinates,
        MapVector3 normal,
        MapVector3 tangent)
    {
        Validate(
            position,
            textureCoordinates,
            lightmapCoordinates,
            normal,
            tangent);

        Position = position;
        Color = color;
        TextureCoordinates = textureCoordinates;
        LightmapCoordinates = lightmapCoordinates;
        Normal = normal;
        Tangent = tangent;
    }

    public MapVector3 Position { get; }
    public AuthoredRenderColor Color { get; }
    public AuthoredRenderUv TextureCoordinates { get; }
    public AuthoredRenderUv LightmapCoordinates { get; }
    public MapVector3 Normal { get; }
    public MapVector3 Tangent { get; }

    internal static void RequireCanonical(
        AuthoredRenderVertex value,
        string parameterName)
    {
        try
        {
            Validate(
                value.Position,
                value.TextureCoordinates,
                value.LightmapCoordinates,
                value.Normal,
                value.Tangent);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                exception.Message,
                parameterName,
                exception);
        }
    }

    private static void Validate(
        MapVector3 position,
        AuthoredRenderUv textureCoordinates,
        AuthoredRenderUv lightmapCoordinates,
        MapVector3 normal,
        MapVector3 tangent)
    {
        if (!position.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "Render positions must be finite.");
        }
        if (!textureCoordinates.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(textureCoordinates),
                "Base texture coordinates must be finite.");
        }
        if (!lightmapCoordinates.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lightmapCoordinates),
                "Lightmap texture coordinates must be finite.");
        }
        if (!normal.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normal),
                "Render normals must be finite.");
        }
        if (!tangent.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tangent),
                "Render tangents must be finite.");
        }

        double normalLengthSquared = LengthSquared(normal);
        if (Math.Abs(normalLengthSquared - 1d) >
            UnitLengthTolerance)
        {
            throw new ArgumentException(
                "Render normals must already have unit length.",
                nameof(normal));
        }

        double tangentLengthSquared = LengthSquared(tangent);
        if (Math.Abs(tangentLengthSquared - 1d) >
            UnitLengthTolerance)
        {
            throw new ArgumentException(
                "Render tangents must already have unit length.",
                nameof(tangent));
        }

        double dot =
            (double)normal.X * tangent.X +
            (double)normal.Y * tangent.Y +
            (double)normal.Z * tangent.Z;
        if (!double.IsFinite(dot) ||
            Math.Abs(dot) > OrthogonalityTolerance)
        {
            throw new ArgumentException(
                "Render normal and tangent inputs must be orthogonal.",
                nameof(tangent));
        }
    }

    private static double LengthSquared(MapVector3 value) =>
        (double)value.X * value.X +
        (double)value.Y * value.Y +
        (double)value.Z * value.Z;
}

/// <summary>
/// One ordered triangle. Index order is interpreted according to the owning
/// mesh's explicit <see cref="RenderTriangleWinding"/>.
/// </summary>
public readonly record struct AuthoredIndexedRenderTriangle
{
    public AuthoredIndexedRenderTriangle(
        int index0,
        int index1,
        int index2)
    {
        if (index0 < 0)
            throw new ArgumentOutOfRangeException(nameof(index0));
        if (index1 < 0)
            throw new ArgumentOutOfRangeException(nameof(index1));
        if (index2 < 0)
            throw new ArgumentOutOfRangeException(nameof(index2));
        if (index0 == index1 ||
            index1 == index2 ||
            index2 == index0)
        {
            throw new ArgumentException(
                "A render triangle must reference three distinct vertices.");
        }

        Index0 = index0;
        Index1 = index1;
        Index2 = index2;
    }

    public int Index0 { get; }
    public int Index1 { get; }
    public int Index2 { get; }
}

/// <summary>
/// Immutable canonical indexed render-mesh source. Stable source and optional
/// inline-model identities are semantic compiler keys, not emitted ordinals.
/// </summary>
public sealed class AuthoredIndexedRenderMeshSource
{
    private const double DegenerateAreaSquaredTolerance = 1e-20;

    private readonly IReadOnlyList<AuthoredRenderVertex> _vertices;
    private readonly IReadOnlyList<AuthoredIndexedRenderTriangle> _triangles;

    public AuthoredIndexedRenderMeshSource(
        MapObjectId objectId,
        RenderMeshSourceOwnership ownership,
        string symbolicMaterialName,
        RenderTriangleWinding triangleWinding,
        IEnumerable<AuthoredRenderVertex> vertices,
        IEnumerable<AuthoredIndexedRenderTriangle> triangles)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        ArgumentNullException.ThrowIfNull(ownership);
        ValidateOwnership(ownership);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolicMaterialName);
        if (symbolicMaterialName.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "Symbolic material names cannot contain NUL bytes.",
                nameof(symbolicMaterialName));
        }
        if (!Enum.IsDefined(triangleWinding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(triangleWinding));
        }
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);

        AuthoredRenderVertex[] vertexCopy = vertices.ToArray();
        if (vertexCopy.Length < 3)
        {
            throw new ArgumentException(
                "An indexed render mesh requires at least three vertices.",
                nameof(vertices));
        }
        for (int index = 0; index < vertexCopy.Length; index++)
        {
            AuthoredRenderVertex.RequireCanonical(
                vertexCopy[index],
                $"{nameof(vertices)}[{index}]");
        }

        AuthoredIndexedRenderTriangle[] triangleCopy =
            triangles.ToArray();
        if (triangleCopy.Length == 0)
        {
            throw new ArgumentException(
                "An indexed render mesh requires at least one triangle.",
                nameof(triangles));
        }
        for (int index = 0; index < triangleCopy.Length; index++)
        {
            AuthoredIndexedRenderTriangle triangle =
                triangleCopy[index];
            RequireVertexIndex(
                triangle.Index0,
                vertexCopy.Length,
                index,
                nameof(triangles));
            RequireVertexIndex(
                triangle.Index1,
                vertexCopy.Length,
                index,
                nameof(triangles));
            RequireVertexIndex(
                triangle.Index2,
                vertexCopy.Length,
                index,
                nameof(triangles));
            RequireNonDegenerate(
                vertexCopy[triangle.Index0].Position,
                vertexCopy[triangle.Index1].Position,
                vertexCopy[triangle.Index2].Position,
                index,
                nameof(triangles));
        }

        ObjectId = objectId;
        Ownership = ownership;
        SymbolicMaterialName = symbolicMaterialName;
        TriangleWinding = triangleWinding;
        _vertices =
            new ReadOnlyCollection<AuthoredRenderVertex>(vertexCopy);
        _triangles =
            new ReadOnlyCollection<AuthoredIndexedRenderTriangle>(
                triangleCopy);
    }

    public MapObjectId ObjectId { get; }
    public RenderMeshSourceOwnership Ownership { get; }
    public string SymbolicMaterialName { get; }
    public RenderTriangleWinding TriangleWinding { get; }
    public IReadOnlyList<AuthoredRenderVertex> Vertices => _vertices;
    public IReadOnlyList<AuthoredIndexedRenderTriangle> Triangles =>
        _triangles;

    private static void ValidateOwnership(
        RenderMeshSourceOwnership ownership)
    {
        switch (ownership.Kind)
        {
            case RenderMeshOwnershipKind.StandaloneWorld:
                if (ownership is not
                        StandaloneWorldRenderMeshOwnership ||
                    ownership.InlineBrushModelObjectId is not null)
                {
                    throw new ArgumentException(
                        "Standalone-world render ownership cannot carry " +
                        "an inline brush-model identity.",
                        nameof(ownership));
                }
                break;

            case RenderMeshOwnershipKind.InlineBrushModel:
                if (ownership is not
                        InlineBrushModelRenderMeshOwnership ||
                    ownership.InlineBrushModelObjectId is not
                        { } inlineModelId ||
                    inlineModelId.Value == Guid.Empty)
                {
                    throw new ArgumentException(
                        "Inline render ownership requires one explicit " +
                        "brush-model identity.",
                        nameof(ownership));
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(ownership));
        }
    }

    private static void RequireVertexIndex(
        int vertexIndex,
        int vertexCount,
        int triangleIndex,
        string parameterName)
    {
        if ((uint)vertexIndex < (uint)vertexCount)
            return;

        throw new ArgumentException(
            $"Render triangle {triangleIndex} references vertex " +
            $"{vertexIndex} outside the {vertexCount}-row source.",
            parameterName);
    }

    private static void RequireNonDegenerate(
        MapVector3 point0,
        MapVector3 point1,
        MapVector3 point2,
        int triangleIndex,
        string parameterName)
    {
        double edge0X = (double)point1.X - point0.X;
        double edge0Y = (double)point1.Y - point0.Y;
        double edge0Z = (double)point1.Z - point0.Z;
        double edge1X = (double)point2.X - point0.X;
        double edge1Y = (double)point2.Y - point0.Y;
        double edge1Z = (double)point2.Z - point0.Z;
        double crossX = edge0Y * edge1Z - edge0Z * edge1Y;
        double crossY = edge0Z * edge1X - edge0X * edge1Z;
        double crossZ = edge0X * edge1Y - edge0Y * edge1X;
        double areaSquared =
            crossX * crossX +
            crossY * crossY +
            crossZ * crossZ;
        if (double.IsFinite(areaSquared) &&
            areaSquared > DegenerateAreaSquaredTolerance)
        {
            return;
        }

        throw new ArgumentException(
            $"Render triangle {triangleIndex} is degenerate or outside " +
            "the finite compiler geometry range.",
            parameterName);
    }
}
