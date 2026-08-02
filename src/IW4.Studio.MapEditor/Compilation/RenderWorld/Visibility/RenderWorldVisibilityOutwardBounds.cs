using System.Buffers.Binary;
using IW4.Assets.Assets.ColMap;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

/// <summary>
/// Compiles midpoint/half-size bounds without contracting source endpoints
/// when IW4 consumers reconstruct them with float arithmetic.
/// </summary>
internal static class RenderWorldVisibilityOutwardBounds
{
    public static MapBounds FromSurface(
        RenderWorldCompiledGeometry geometry,
        RenderWorldCompiledSurface surface)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.VertexRange.IsEmpty ||
            surface.VertexRange.EndExclusive > geometry.VertexCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surface),
                "A visibility surface requires a valid packed vertex range.");
        }

        int firstOffset = checked(
            surface.VertexRange.Start *
            RenderWorldStructuralProfile.PositionStride);
        (float firstX, float firstY, float firstZ) =
            ReadPosition(geometry.PackedPositionData, firstOffset);
        double minimumX = firstX;
        double minimumY = firstY;
        double minimumZ = firstZ;
        double maximumX = firstX;
        double maximumY = firstY;
        double maximumZ = firstZ;

        for (int localVertex = 1;
             localVertex < surface.VertexRange.Count;
             localVertex++)
        {
            int offset = checked(
                (surface.VertexRange.Start + localVertex) *
                RenderWorldStructuralProfile.PositionStride);
            (float x, float y, float z) =
                ReadPosition(geometry.PackedPositionData, offset);
            minimumX = Math.Min(minimumX, x);
            minimumY = Math.Min(minimumY, y);
            minimumZ = Math.Min(minimumZ, z);
            maximumX = Math.Max(maximumX, x);
            maximumY = Math.Max(maximumY, y);
            maximumZ = Math.Max(maximumZ, z);
        }

        return FromExtents(
            minimumX,
            minimumY,
            minimumZ,
            maximumX,
            maximumY,
            maximumZ);
    }

    public static MapBounds FromCollisionWorld(
        CollisionStructuralCandidate collisionCandidate)
    {
        ArgumentNullException.ThrowIfNull(collisionCandidate);
        ClipMapAsset definition = collisionCandidate.Definition;
        if (definition.NumSubModels != 1 ||
            definition.CModels.Count != definition.NumSubModels)
        {
            throw new InvalidDataException(
                "The single-cell visibility profile requires exactly the " +
                "collision world model and no inline collision models.");
        }

        CModel worldModel = definition.CModels[0];
        return FromExtents(
            worldModel.Mins.X,
            worldModel.Mins.Y,
            worldModel.Mins.Z,
            worldModel.Maxs.X,
            worldModel.Maxs.Y,
            worldModel.Maxs.Z);
    }

    public static bool Contains(
        MapBounds outer,
        MapBounds inner)
    {
        RequireValid(outer, nameof(outer));
        RequireValid(inner, nameof(inner));
        (double outerMinX, double outerMaxX) = Endpoints(
            outer.MidPoint.X,
            outer.HalfSize.X);
        (double outerMinY, double outerMaxY) = Endpoints(
            outer.MidPoint.Y,
            outer.HalfSize.Y);
        (double outerMinZ, double outerMaxZ) = Endpoints(
            outer.MidPoint.Z,
            outer.HalfSize.Z);
        (double innerMinX, double innerMaxX) = Endpoints(
            inner.MidPoint.X,
            inner.HalfSize.X);
        (double innerMinY, double innerMaxY) = Endpoints(
            inner.MidPoint.Y,
            inner.HalfSize.Y);
        (double innerMinZ, double innerMaxZ) = Endpoints(
            inner.MidPoint.Z,
            inner.HalfSize.Z);

        return outerMinX <= innerMinX &&
               outerMinY <= innerMinY &&
               outerMinZ <= innerMinZ &&
               outerMaxX >= innerMaxX &&
               outerMaxY >= innerMaxY &&
               outerMaxZ >= innerMaxZ;
    }

    public static MapBounds Include(
        IEnumerable<MapBounds> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        MapBounds[] copy = bounds.ToArray();
        if (copy.Length == 0)
        {
            throw new ArgumentException(
                "Visibility bounds collection cannot be empty.",
                nameof(bounds));
        }
        foreach (MapBounds value in copy)
            RequireValid(value, nameof(bounds));

        return FromExtents(
            copy.Min(value =>
                (double)value.MidPoint.X - value.HalfSize.X),
            copy.Min(value =>
                (double)value.MidPoint.Y - value.HalfSize.Y),
            copy.Min(value =>
                (double)value.MidPoint.Z - value.HalfSize.Z),
            copy.Max(value =>
                (double)value.MidPoint.X + value.HalfSize.X),
            copy.Max(value =>
                (double)value.MidPoint.Y + value.HalfSize.Y),
            copy.Max(value =>
                (double)value.MidPoint.Z + value.HalfSize.Z));
    }

    public static bool ContainsSurfaceVertices(
        MapBounds bounds,
        RenderWorldCompiledGeometry geometry,
        RenderWorldCompiledSurface surface)
    {
        RequireValid(bounds, nameof(bounds));
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(surface);
        (double minimumX, double maximumX) = Endpoints(
            bounds.MidPoint.X,
            bounds.HalfSize.X);
        (double minimumY, double maximumY) = Endpoints(
            bounds.MidPoint.Y,
            bounds.HalfSize.Y);
        (double minimumZ, double maximumZ) = Endpoints(
            bounds.MidPoint.Z,
            bounds.HalfSize.Z);

        for (int localVertex = 0;
             localVertex < surface.VertexRange.Count;
             localVertex++)
        {
            int offset = checked(
                (surface.VertexRange.Start + localVertex) *
                RenderWorldStructuralProfile.PositionStride);
            (float x, float y, float z) =
                ReadPosition(geometry.PackedPositionData, offset);
            if (x < minimumX || x > maximumX ||
                y < minimumY || y > maximumY ||
                z < minimumZ || z > maximumZ)
            {
                return false;
            }
        }

        return true;
    }

    public static void RequireValid(
        MapBounds bounds,
        string parameterName)
    {
        if (!bounds.IsFinite ||
            bounds.HalfSize.X < 0 ||
            bounds.HalfSize.Y < 0 ||
            bounds.HalfSize.Z < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Visibility bounds must be finite and non-negative.");
        }
    }

    private static MapBounds FromExtents(
        double minimumX,
        double minimumY,
        double minimumZ,
        double maximumX,
        double maximumY,
        double maximumZ)
    {
        (float midpointX, float halfSizeX) =
            CompileAxis(minimumX, maximumX);
        (float midpointY, float halfSizeY) =
            CompileAxis(minimumY, maximumY);
        (float midpointZ, float halfSizeZ) =
            CompileAxis(minimumZ, maximumZ);
        return new MapBounds(
            new MapVector3(midpointX, midpointY, midpointZ),
            new MapVector3(halfSizeX, halfSizeY, halfSizeZ));
    }

    private static (float Midpoint, float HalfSize) CompileAxis(
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "Visibility bound endpoints must be finite and ordered.");
        }

        float midpoint = CanonicalizeZero(
            checked((float)(minimum + (maximum - minimum) * 0.5d)));
        double requiredHalfSize = Math.Max(
            (double)midpoint - minimum,
            maximum - midpoint);
        float halfSize = CanonicalizeZero(
            checked((float)requiredHalfSize));
        if (!float.IsFinite(midpoint) || !float.IsFinite(halfSize))
        {
            throw new OverflowException(
                "Visibility bounds cannot represent finite midpoint and " +
                "half-size values.");
        }

        while (midpoint - halfSize > minimum ||
               midpoint + halfSize < maximum)
        {
            halfSize = MathF.BitIncrement(halfSize);
            if (!float.IsFinite(halfSize))
            {
                throw new OverflowException(
                    "Visibility bounds cannot represent an outward-rounded " +
                    "finite half-size.");
            }
        }

        return (midpoint, halfSize);
    }

    private static (double Minimum, double Maximum) Endpoints(
        float midpoint,
        float halfSize) =>
        (midpoint - halfSize, midpoint + halfSize);

    private static (float X, float Y, float Z) ReadPosition(
        IReadOnlyList<byte> packedPositions,
        int offset)
    {
        if (offset < 0 ||
            offset + RenderWorldStructuralProfile.PositionStride >
                packedPositions.Count)
        {
            throw new InvalidDataException(
                "A packed visibility position escapes the M3 position " +
                "stream.");
        }

        Span<byte> row =
            stackalloc byte[
                RenderWorldStructuralProfile.PositionStride];
        for (int index = 0; index < row.Length; index++)
            row[index] = packedPositions[offset + index];

        float x = BinaryPrimitives.ReadSingleBigEndian(
            row.Slice(0x00, sizeof(float)));
        float y = BinaryPrimitives.ReadSingleBigEndian(
            row.Slice(0x04, sizeof(float)));
        float z = BinaryPrimitives.ReadSingleBigEndian(
            row.Slice(0x08, sizeof(float)));
        if (!float.IsFinite(x) ||
            !float.IsFinite(y) ||
            !float.IsFinite(z))
        {
            throw new InvalidDataException(
                "A packed visibility position contains a non-finite " +
                "coordinate.");
        }

        return (x, y, z);
    }

    private static float CanonicalizeZero(float value) =>
        value == 0f ? 0f : value;
}
