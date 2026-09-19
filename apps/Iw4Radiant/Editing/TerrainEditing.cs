using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class TerrainEditing
{
    internal static bool RaiseLower(MapTerrain terrain, Vector2 center, float radius, float deltaHeight)
    {
        if (!float.IsFinite(deltaHeight))
            throw new ArgumentOutOfRangeException(nameof(deltaHeight), "Terrain height changes must be finite.");
        return Sculpt(terrain, center, radius, 1, (index, source) => source[index].Z + (double)deltaHeight);
    }

    internal static bool Smooth(MapTerrain terrain, Vector2 center, float radius, float amount) =>
        Sculpt(terrain, center, radius, amount, (index, source) => NeighborhoodAverage(terrain, source, index));

    internal static bool SmoothVertices(MapTerrain terrain, IReadOnlyCollection<int> vertexIndices) =>
        EditVertexHeights(terrain, vertexIndices, (index, source) => (float)NeighborhoodAverage(terrain, source, index));

    internal static bool FlattenVertices(MapTerrain terrain, IReadOnlyCollection<int> vertexIndices, float height)
    {
        if (!float.IsFinite(height))
            throw new ArgumentOutOfRangeException(nameof(height), "The flatten height must be finite.");
        return EditVertexHeights(terrain, vertexIndices, (_, _) => height);
    }

    internal static bool Flatten(MapTerrain terrain, Vector2 center, float radius, float height, float amount)
    {
        if (!float.IsFinite(height))
            throw new ArgumentOutOfRangeException(nameof(height), "The flatten height must be finite.");
        return Sculpt(terrain, center, radius, amount, (_, _) => height);
    }

    private static double NeighborhoodAverage(MapTerrain terrain, Vector3[] source, int index)
    {
        int column = index / terrain.Height, row = index % terrain.Height, count = 0;
        double total = 0;
        for (int x = Math.Max(0, column - 1); x <= Math.Min(terrain.Width - 1, column + 1); x++)
        for (int y = Math.Max(0, row - 1); y <= Math.Min(terrain.Height - 1, row + 1); y++)
        {
            total += source[x * terrain.Height + y].Z;
            count++;
        }
        return total / count;
    }

    private static bool EditVertexHeights(MapTerrain terrain, IReadOnlyCollection<int> vertexIndices,
        Func<int, Vector3[], float> height)
    {
        ValidateVertices(terrain);
        ArgumentNullException.ThrowIfNull(vertexIndices);
        int[] indices = vertexIndices.Distinct().ToArray();
        if (indices.Any(index => (uint)index >= (uint)terrain.Vertices.Length))
            throw new ArgumentOutOfRangeException(nameof(vertexIndices), "A selected terrain vertex no longer exists.");
        Vector3[] source = (Vector3[])terrain.Vertices.Clone();
        var heights = new float[indices.Length];
        bool changed = false;
        for (int i = 0; i < indices.Length; i++)
        {
            heights[i] = height(indices[i], source);
            if (!float.IsFinite(heights[i]))
                throw new InvalidOperationException("The terrain edit would produce a nonfinite height.");
            changed |= heights[i] != source[indices[i]].Z;
        }
        if (!changed) return false;
        for (int i = 0; i < indices.Length; i++)
            terrain.Vertices[indices[i]].Z = heights[i];
        return true;
    }

    private static bool Sculpt(MapTerrain terrain, Vector2 center, float radius, float amount,
        Func<int, Vector3[], double> targetHeight)
    {
        ValidateVertices(terrain);
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
            throw new ArgumentException("The sculpt position must be finite.", nameof(center));
        if (!float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "The sculpt radius must be positive and finite.");
        if (!float.IsFinite(amount) || amount < 0 || amount > 1)
            throw new ArgumentOutOfRangeException(nameof(amount), "The sculpt amount must be between zero and one.");
        if (amount == 0) return false;

        // Every height in a dab comes from the same surface, including smoothing neighbors.
        Vector3[] source = (Vector3[])terrain.Vertices.Clone();
        var heights = new float[source.Length];
        bool changed = false;
        for (int i = 0; i < source.Length; i++)
        {
            Vector3 vertex = source[i];
            double dx = (double)vertex.X - center.X, dy = (double)vertex.Y - center.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            float height = vertex.Z;
            if (distance < radius)
            {
                double falloff = 1 - distance / radius;
                height = (float)(vertex.Z + (targetHeight(i, source) - vertex.Z) * amount * falloff * falloff);
                if (!float.IsFinite(height))
                    throw new InvalidOperationException("The sculpt operation would produce a nonfinite terrain height.");
            }
            heights[i] = height;
            changed |= height != vertex.Z;
        }
        if (!changed) return false;
        for (int i = 0; i < heights.Length; i++)
            terrain.Vertices[i].Z = heights[i];
        return true;
    }

    internal static int Stitch(MapTerrain first, MapTerrain second, float tolerance)
    {
        if (ReferenceEquals(first, second))
            throw new ArgumentException("Select two different terrain patches to stitch.", nameof(second));
        if (!float.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "The stitch tolerance must be finite and nonnegative.");
        ValidateSeamAttributes(first);
        ValidateSeamAttributes(second);

        int[]? firstEdge = null, secondEdge = null;
        foreach (int[] a in BoundaryEdges(first))
        foreach (int[] b in BoundaryEdges(second))
        {
            bool forward = Nearby(first.Vertices[a[0]], second.Vertices[b[0]], tolerance) &&
                Nearby(first.Vertices[a[^1]], second.Vertices[b[^1]], tolerance);
            bool reverse = Nearby(first.Vertices[a[0]], second.Vertices[b[^1]], tolerance) &&
                Nearby(first.Vertices[a[^1]], second.Vertices[b[0]], tolerance);
            if (!forward && !reverse) continue;
            if (firstEdge is not null || forward && reverse)
                throw new InvalidOperationException("The stitch tolerance matches multiple terrain edges. Use a smaller tolerance.");
            firstEdge = a;
            secondEdge = reverse ? b.Reverse().ToArray() : b;
        }
        if (firstEdge is null || secondEdge is null) return 0;

        MapTerrain coarse = first, fine = second;
        int[] coarseEdge = firstEdge, fineEdge = secondEdge;
        if (coarseEdge.Length > fineEdge.Length)
        {
            (coarse, fine) = (fine, coarse);
            (coarseEdge, fineEdge) = (fineEdge, coarseEdge);
        }
        int coarseSegments = coarseEdge.Length - 1, fineSegments = fineEdge.Length - 1;
        if (fineSegments % coarseSegments != 0)
            throw new InvalidOperationException("Terrain edge subdivisions must match or be whole multiples to stitch without changing topology.");
        int ratio = fineSegments / coarseSegments;
        for (int i = 0; i < fineEdge.Length; i++)
        {
            Vector3 corresponding = Sample(coarse.Vertices, coarseEdge, i, ratio);
            if (!Nearby(corresponding, fine.Vertices[fineEdge[i]], tolerance))
                throw new InvalidOperationException("The terrain edges diverge beyond the stitch tolerance.");
        }

        var positions = new Vector3[coarseEdge.Length];
        var texture = new Vector2[coarseEdge.Length];
        var colors = new Vector4[coarseEdge.Length];
        for (int i = 0; i < coarseEdge.Length; i++)
        {
            int a = coarseEdge[i], b = fineEdge[i * ratio];
            positions[i] = Mix(coarse.Vertices[a], fine.Vertices[b], 0.5);
            texture[i] = Mix(coarse.TextureCoordinates[a], fine.TextureCoordinates[b], 0.5);
            colors[i] = Mix(coarse.Colors[a], fine.Colors[b], 0.5);
        }

        // Fine-only samples follow the shared coarse polyline; neither grid loses vertices.
        var finePositions = new Vector3[fineEdge.Length];
        var fineTexture = new Vector2[fineEdge.Length];
        var fineColors = new Vector4[fineEdge.Length];
        for (int i = 0; i < fineEdge.Length; i++)
        {
            int left = Math.Min(i / ratio, coarseSegments - 1);
            double fraction = (double)(i - left * ratio) / ratio;
            finePositions[i] = Mix(positions[left], positions[left + 1], fraction);
            fineTexture[i] = Mix(texture[left], texture[left + 1], fraction);
            fineColors[i] = Mix(colors[left], colors[left + 1], fraction);
        }

        int changed = ApplyEdge(coarse, coarseEdge, positions, texture, colors);
        return changed + ApplyEdge(fine, fineEdge, finePositions, fineTexture, fineColors);
    }

    private static int ApplyEdge(MapTerrain terrain, int[] edge, Vector3[] positions, Vector2[] texture, Vector4[] colors)
    {
        int changed = 0;
        for (int i = 0; i < edge.Length; i++)
        {
            int index = edge[i];
            if (terrain.Vertices[index] == positions[i] && terrain.TextureCoordinates[index] == texture[i] &&
                terrain.Colors[index] == colors[i]) continue;
            terrain.Vertices[index] = positions[i];
            terrain.TextureCoordinates[index] = texture[i];
            terrain.Colors[index] = colors[i];
            changed++;
        }
        return changed;
    }

    private static IEnumerable<int[]> BoundaryEdges(MapTerrain terrain)
    {
        yield return Enumerable.Range(0, terrain.Height).ToArray();
        yield return Enumerable.Range(0, terrain.Height).Select(row => (terrain.Width - 1) * terrain.Height + row).ToArray();
        yield return Enumerable.Range(0, terrain.Width).Select(column => column * terrain.Height).ToArray();
        yield return Enumerable.Range(0, terrain.Width).Select(column => column * terrain.Height + terrain.Height - 1).ToArray();
    }

    private static Vector3 Sample(Vector3[] vertices, int[] edge, int index, int ratio)
    {
        int left = Math.Min(index / ratio, edge.Length - 2);
        return Mix(vertices[edge[left]], vertices[edge[left + 1]], (double)(index - left * ratio) / ratio);
    }

    private static bool Nearby(Vector3 a, Vector3 b, float tolerance)
    {
        double x = (double)a.X - b.X, y = (double)a.Y - b.Y, z = (double)a.Z - b.Z;
        return x * x + y * y + z * z <= (double)tolerance * tolerance;
    }

    private static float Mix(float a, float b, double amount) =>
        amount == 0 || a == b ? a : amount == 1 ? b : (float)(a + ((double)b - a) * amount);
    private static Vector2 Mix(Vector2 a, Vector2 b, double amount) =>
        new(Mix(a.X, b.X, amount), Mix(a.Y, b.Y, amount));
    private static Vector3 Mix(Vector3 a, Vector3 b, double amount) =>
        new(Mix(a.X, b.X, amount), Mix(a.Y, b.Y, amount), Mix(a.Z, b.Z, amount));
    private static Vector4 Mix(Vector4 a, Vector4 b, double amount) =>
        new(Mix(a.X, b.X, amount), Mix(a.Y, b.Y, amount), Mix(a.Z, b.Z, amount), Mix(a.W, b.W, amount));

    private static void ValidateVertices(MapTerrain terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (terrain.Width < 2 || terrain.Height < 2 || (long)terrain.Width * terrain.Height != terrain.Vertices.Length)
            throw new InvalidOperationException("Terrain dimensions do not match its vertices.");
        foreach (Vector3 vertex in terrain.Vertices)
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
                throw new InvalidOperationException("Terrain vertices must be finite before editing.");
    }

    private static void ValidateSeamAttributes(MapTerrain terrain)
    {
        ValidateVertices(terrain);
        int count = terrain.Vertices.Length;
        if (terrain.TextureCoordinates.Length != count || terrain.LightmapCoordinates.Length != count ||
            terrain.Colors.Length != count || terrain.EdgeFlags.Length != count)
            throw new InvalidOperationException("Terrain vertex attributes do not match its dimensions.");
        for (int i = 0; i < count; i++)
        {
            Vector2 texture = terrain.TextureCoordinates[i], lightmap = terrain.LightmapCoordinates[i];
            Vector4 color = terrain.Colors[i];
            if (!float.IsFinite(texture.X) || !float.IsFinite(texture.Y) ||
                !float.IsFinite(lightmap.X) || !float.IsFinite(lightmap.Y) ||
                !float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z) || !float.IsFinite(color.W) ||
                color.X < 0 || color.X > 1 || color.Y < 0 || color.Y > 1 ||
                color.Z < 0 || color.Z > 1 || color.W < 0 || color.W > 1)
                throw new InvalidOperationException("Terrain UVs must be finite and colors must be between zero and one before stitching.");
        }
    }
}
