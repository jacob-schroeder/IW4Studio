using System.Buffers.Binary;
using IW4.Game.Assets.GfxMap;

namespace IW4.Game.Codecs.GfxMap;

/// <summary>Computes one PS3 world-surface streaming weight from its final packed geometry.</summary>
internal static class WorldSurfaceStreamingWeight
{
    // The binary32 value recovered for the widened world-triangle gradient expression.
    private const double GradientScale = 208.7565765380859375;

    internal static byte Calculate(
        ReadOnlySpan<byte> packedPositions,
        ReadOnlySpan<byte> packedLayers,
        int layerStride,
        int uvOffset,
        SrfTriangles triangles,
        IReadOnlyList<ushort> indices,
        IReadOnlyList<(int Width, int Height)> mipDimensions)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(mipDimensions);
        if (layerStride <= 0 || uvOffset < 0 || uvOffset > layerStride - 8)
            throw new ArgumentOutOfRangeException(nameof(uvOffset), "The UV pair must fit in one layer row.");

        int indexCount = triangles.TriCount * 3;
        if (triangles.BaseIndex < 0 || triangles.BaseIndex > indices.Count - indexCount)
            throw new InvalidDataException("The world surface index range exceeds the index buffer.");
        if (triangles.BaseVertex < 0 || triangles.VertexLayerData < 0 ||
            triangles.MinVertexIndex > int.MaxValue ||
            (long)triangles.MinVertexIndex + triangles.VertexCount > int.MaxValue)
            throw new InvalidDataException("The world surface has an invalid vertex range.");

        if (mipDimensions.Count == 0 || indexCount == 0)
            return byte.MaxValue;
        var aspects = new HashSet<(int Width, int Height)>();
        double minimumDiagonal = double.PositiveInfinity;
        foreach ((int width, int height) in mipDimensions)
        {
            if (width <= 0 || height <= 0)
                continue;
            minimumDiagonal = System.Math.Min(minimumDiagonal, double.Hypot(width, height));
            int divisor = Gcd(width, height);
            aspects.Add((width / divisor, height / divisor));
        }
        if (aspects.Count == 0)
            return byte.MaxValue;

        var gradients = new List<(double U, double V)>(triangles.TriCount);
        for (int triangle = 0; triangle < triangles.TriCount; triangle++)
        {
            int firstIndex = triangles.BaseIndex + triangle * 3;
            Vertex a = ReadVertex(packedPositions, packedLayers, layerStride, uvOffset, triangles, indices[firstIndex]);
            Vertex b = ReadVertex(packedPositions, packedLayers, layerStride, uvOffset, triangles, indices[firstIndex + 1]);
            Vertex c = ReadVertex(packedPositions, packedLayers, layerStride, uvOffset, triangles, indices[firstIndex + 2]);

            // Subtract the binary32 vertex values before widening the differences.
            double ax = b.X - a.X, ay = b.Y - a.Y, az = b.Z - a.Z;
            double bx = c.X - a.X, by = c.Y - a.Y, bz = c.Z - a.Z;
            double au = b.U - a.U, av = b.V - a.V;
            double bu = c.U - a.U, bv = c.V - a.V;
            double nx = ay * bz - az * by;
            double ny = az * bx - ax * bz;
            double nz = ax * by - ay * bx;
            double area = System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (!(area > 0) || !double.IsFinite(area))
                continue;

            double areaSquared = area * area;
            // The two spatial vectors are the gradients of barycentric coordinates b and c.
            double oneX = (by * nz - bz * ny) / areaSquared;
            double oneY = (bz * nx - bx * nz) / areaSquared;
            double oneZ = (bx * ny - by * nx) / areaSquared;
            double twoX = (ny * az - nz * ay) / areaSquared;
            double twoY = (nz * ax - nx * az) / areaSquared;
            double twoZ = (nx * ay - ny * ax) / areaSquared;
            double ux = au * oneX + bu * twoX;
            double uy = au * oneY + bu * twoY;
            double uz = au * oneZ + bu * twoZ;
            double vx = av * oneX + bv * twoX;
            double vy = av * oneY + bv * twoY;
            double vz = av * oneZ + bv * twoZ;
            double u = System.Math.Sqrt(ux * ux + uy * uy + uz * uz);
            double v = System.Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (double.IsFinite(u + v) && (u > 0 || v > 0))
                gradients.Add((u, v));
        }

        if (gradients.Count == 0)
            return byte.MaxValue;

        double density = double.PositiveInfinity;
        int fifthRank = (gradients.Count - 1) / 5;
        int medianRank = (gradients.Count - 1) / 2;
        var values = new double[gradients.Count];
        foreach ((int width, int height) in aspects)
        {
            double aspectDiagonal = double.Hypot(width, height);
            for (int i = 0; i < gradients.Count; i++)
                values[i] = System.Math.Max(width * gradients[i].U, height * gradients[i].V) / aspectDiagonal;
            Array.Sort(values);
            double cutoff = values[medianRank] / 16.0;
            int rank = fifthRank;
            while (values[rank] < cutoff)
                rank++;
            density = System.Math.Min(density, values[rank] * minimumDiagonal);
        }

        double scaled = System.Math.Floor(GradientScale * density);
        return scaled >= byte.MaxValue ? byte.MaxValue : (byte)scaled;
    }

    private static Vertex ReadVertex(
        ReadOnlySpan<byte> packedPositions,
        ReadOnlySpan<byte> packedLayers,
        int layerStride,
        int uvOffset,
        SrfTriangles triangles,
        ushort localIndex)
    {
        long firstLocal = triangles.MinVertexIndex;
        if (localIndex < firstLocal || localIndex >= firstLocal + triangles.VertexCount)
            throw new InvalidDataException("A world surface index exceeds its vertex range.");
        long positionOffset = ((long)triangles.BaseVertex + localIndex) * WorldVertexCodec.PositionStride;
        long layerOffset = triangles.VertexLayerData + (long)localIndex * layerStride + uvOffset;
        if (positionOffset < 0 || positionOffset > packedPositions.Length - WorldVertexCodec.PositionStride ||
            layerOffset < 0 || layerOffset > packedLayers.Length - 8)
            throw new InvalidDataException("A world surface vertex exceeds its packed stream.");
        ReadOnlySpan<byte> position = packedPositions.Slice((int)positionOffset);
        ReadOnlySpan<byte> layer = packedLayers.Slice((int)layerOffset);
        return new Vertex(
            BinaryPrimitives.ReadSingleBigEndian(position),
            BinaryPrimitives.ReadSingleBigEndian(position[4..]),
            BinaryPrimitives.ReadSingleBigEndian(position[8..]),
            BinaryPrimitives.ReadSingleBigEndian(layer),
            BinaryPrimitives.ReadSingleBigEndian(layer[4..]));
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }

    private readonly record struct Vertex(float X, float Y, float Z, float U, float V);
}
