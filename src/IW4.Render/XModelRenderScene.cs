using System.Numerics;

namespace IW4.Render;

public sealed class XModelRenderScene
{
    internal XModelRenderScene(
        string name,
        IReadOnlyList<XModelRenderLod> lods,
        int defaultLodIndex,
        MapRenderBounds bounds,
        IReadOnlyList<string> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(lods);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Name = name;
        Lods = Array.AsReadOnly(lods.ToArray());
        DefaultLodIndex = defaultLodIndex;
        Bounds = bounds;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public string Name { get; }

    public IReadOnlyList<XModelRenderLod> Lods { get; }

    public int DefaultLodIndex { get; }

    public MapRenderBounds Bounds { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

public sealed class XModelRenderLod
{
    internal XModelRenderLod(
        int lodIndex,
        float distance,
        MapRenderBounds bounds,
        IReadOnlyList<XModelRenderSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        LodIndex = lodIndex;
        Distance = distance;
        Bounds = bounds;
        Surfaces = Array.AsReadOnly(surfaces.ToArray());
        TriangleCount = checked(Surfaces.Sum(surface =>
            surface.Indices.Count / 3));
        VertexCount = checked(Surfaces.Sum(surface =>
            surface.Positions.Count));
    }

    public int LodIndex { get; }

    public float Distance { get; }

    public MapRenderBounds Bounds { get; }

    public IReadOnlyList<XModelRenderSurface> Surfaces { get; }

    public int TriangleCount { get; }

    public int VertexCount { get; }
}

public sealed class XModelRenderSurface
{
    internal XModelRenderSurface(
        int geometrySurfaceIndex,
        int parentMaterialIndex,
        string materialName,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uvs,
        IReadOnlyList<uint> indices,
        MapRenderBounds bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(uvs);
        ArgumentNullException.ThrowIfNull(indices);
        if (positions.Count != normals.Count ||
            positions.Count != uvs.Count)
        {
            throw new ArgumentException(
                "XModel surface vertex channels must have equal lengths.",
                nameof(positions));
        }
        if (indices.Count % 3 != 0)
        {
            throw new ArgumentException(
                "XModel surface indices must contain complete triangles.",
                nameof(indices));
        }

        GeometrySurfaceIndex = geometrySurfaceIndex;
        ParentMaterialIndex = parentMaterialIndex;
        MaterialName = materialName;
        Positions = Array.AsReadOnly(positions.ToArray());
        Normals = Array.AsReadOnly(normals.ToArray());
        UVs = Array.AsReadOnly(uvs.ToArray());
        Indices = Array.AsReadOnly(indices.ToArray());
        Bounds = bounds;
    }

    public int GeometrySurfaceIndex { get; }

    public int ParentMaterialIndex { get; }

    public string MaterialName { get; }

    public IReadOnlyList<Vector3> Positions { get; }

    public IReadOnlyList<Vector3> Normals { get; }

    public IReadOnlyList<Vector2> UVs { get; }

    public IReadOnlyList<uint> Indices { get; }

    public MapRenderBounds Bounds { get; }
}
