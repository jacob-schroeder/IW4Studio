using System.Numerics;

namespace Iw4Radiant.MapSource;

internal sealed class MapTerrain
{
    public string Material { get; set; } = "";
    public string Lightmap { get; set; } = "lightmap_gray";
    public List<string> Directives { get; } = [];
    public string? Smoothing { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float LightmapSize { get; set; } = 16;
    public int Subdivision { get; set; } = 8;
    // Radiant serializes columns first: column * Height + row.
    public Vector3[] Vertices { get; set; } = [];
    public Vector2[] TextureCoordinates { get; set; } = [];
    public Vector2[] LightmapCoordinates { get; set; } = [];
    public Vector4[] Colors { get; set; } = [];
    public int[] EdgeFlags { get; set; } = [];

    public IEnumerable<(int A, int B, int C)> GetTriangles()
    {
        for (int x = 0; x < Width - 1; x++)
        for (int y = 0; y < Height - 1; y++)
        {
            int a = x * Height + y, b = (x + 1) * Height + y, c = b + 1, d = a + 1;
            if ((EdgeFlags[a] & 1) != 0)
            {
                yield return (a, b, d);
                yield return (b, c, d);
            }
            else
            {
                yield return (a, b, c);
                yield return (a, c, d);
            }
        }
    }

    public (Vector3 Min, Vector3 Max) GetBounds() =>
        (Vertices.Aggregate(Vector3.Min), Vertices.Aggregate(Vector3.Max));

    public void Translate(Vector3 offset)
    {
        Transform(Matrix4x4.CreateTranslation(offset));
    }

    public bool Transform(Matrix4x4 transform, IReadOnlyCollection<int>? vertexIndices = null)
    {
        if (!float.IsFinite(transform.M11) || !float.IsFinite(transform.M12) || !float.IsFinite(transform.M13) ||
            !float.IsFinite(transform.M21) || !float.IsFinite(transform.M22) || !float.IsFinite(transform.M23) ||
            !float.IsFinite(transform.M31) || !float.IsFinite(transform.M32) || !float.IsFinite(transform.M33) ||
            !float.IsFinite(transform.M41) || !float.IsFinite(transform.M42) || !float.IsFinite(transform.M43) ||
            transform.M14 != 0 || transform.M24 != 0 || transform.M34 != 0 || transform.M44 != 1)
            throw new ArgumentException("Terrain transforms must be finite affine matrices.", nameof(transform));

        int[] indices = vertexIndices is null
            ? Enumerable.Range(0, Vertices.Length).ToArray()
            : vertexIndices.Distinct().ToArray();
        var positions = new Vector3[indices.Length];
        bool changed = false;
        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i];
            if ((uint)index >= (uint)Vertices.Length)
                throw new ArgumentOutOfRangeException(nameof(vertexIndices), "A selected terrain vertex no longer exists.");
            Vector3 position = Vector3.Transform(Vertices[index], transform);
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                throw new InvalidOperationException("The terrain transform would produce a nonfinite vertex.");
            positions[i] = position;
            changed |= position != Vertices[index];
        }
        if (!changed) return false;
        for (int i = 0; i < indices.Length; i++)
            Vertices[indices[i]] = positions[i];
        return true;
    }

    public MapTerrain Clone()
    {
        var copy = new MapTerrain
        {
            Material = Material, Lightmap = Lightmap, Smoothing = Smoothing,
            Width = Width, Height = Height, LightmapSize = LightmapSize, Subdivision = Subdivision,
            Vertices = (Vector3[])Vertices.Clone(), TextureCoordinates = (Vector2[])TextureCoordinates.Clone(),
            LightmapCoordinates = (Vector2[])LightmapCoordinates.Clone(), Colors = (Vector4[])Colors.Clone(),
            EdgeFlags = (int[])EdgeFlags.Clone()
        };
        copy.Directives.AddRange(Directives);
        return copy;
    }

    public static MapTerrain Create(Vector3 origin, float spacing, int count, string material)
    {
        var terrain = new MapTerrain
        {
            Width = count, Height = count, Material = material,
            Vertices = new Vector3[count * count], TextureCoordinates = new Vector2[count * count],
            LightmapCoordinates = new Vector2[count * count], Colors = new Vector4[count * count],
            EdgeFlags = new int[count * count]
        };
        for (int x = 0; x < count; x++)
        for (int y = 0; y < count; y++)
        {
            int index = x * count + y;
            terrain.Vertices[index] = origin + new Vector3(x * spacing, y * spacing, 0);
            terrain.TextureCoordinates[index] = new Vector2(x * spacing, y * spacing) / 128;
            terrain.LightmapCoordinates[index] = new Vector2(x, y) / (count - 1);
            terrain.Colors[index] = Vector4.One;
        }
        return terrain;
    }
}
