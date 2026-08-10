using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.GameMap;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.GameMap;

public sealed class VehicleTrackLoader
{
    private const int MaximumSegmentDepth = 128;

    // Embedded 0x08-byte VehicleTrack header.
    public VehicleTrack ReadHeader(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        XPointer<VehicleTrackSegment[]> segmentsPointer = ReadPointer<VehicleTrackSegment[]>(cursor, context);
        return new VehicleTrack
        {
            SegmentsPointer = segmentsPointer,
            SegmentCount = cursor.ReadInt32()
        };
    }

    public VehicleTrack LoadPayloads(
        FastFileCursor cursor,
        VehicleTrack header,
        DbLoadExecutionContext context)
    {
        var segmentsByAddress = new Dictionary<XBlockAddress, VehicleTrackSegment>();
        IReadOnlyList<VehicleTrackSegment> segments = ReadSegmentArray(
            cursor,
            header.SegmentsPointer,
            NonNegative(header.SegmentCount, "VehicleTrack.segmentCount"),
            context,
            segmentsByAddress,
            depth: 0,
            "VehicleTrack.segments");
        return new VehicleTrack
        {
            SegmentsPointer = header.SegmentsPointer,
            Segments = segments,
            SegmentCount = header.SegmentCount
        };
    }

    // Segment arrays may be null, inline (-1), or packed.
    private static IReadOnlyList<VehicleTrackSegment> ReadSegmentArray(
        FastFileCursor cursor,
        XPointer<VehicleTrackSegment[]> pointer,
        int count,
        DbLoadExecutionContext context,
        Dictionary<XBlockAddress, VehicleTrackSegment> segmentsByAddress,
        int depth,
        string memberName)
    {
        int byteCount = checked(count * VehicleTrackSegment.SerializedSize);
        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<VehicleTrackSegment[]>(
                pointer.Untyped,
                byteCount,
                memberName);
            return [];
        }

        if (pointer.Type != PointerType.Inline)
        {
            throw new InvalidDataException(
                $"{memberName} pointer 0x{pointer.Raw:X8} is not null, inline -1, or packed.");
        }

        EnsureDepth(depth, memberName);
        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} patched to {targetAddress}, but loaded at {loadedAddress}.");

        var segments = new VehicleTrackSegment[count];
        for (int index = 0; index < segments.Length; index++)
        {
            XBlockAddress rowAddress = targetAddress.Add(checked(index * VehicleTrackSegment.SerializedSize));
            var rowCursor = RowCursor(bytes, targetAddress, index, VehicleTrackSegment.SerializedSize);
            VehicleTrackSegment segment = ReadSegmentBody(
                cursor,
                rowCursor,
                rowAddress,
                context,
                segmentsByAddress,
                depth,
                $"{memberName}[{index}]");
            segments[index] = segment;
            segmentsByAddress[rowAddress] = segment;
        }

        return segments;
    }

    // Fixed 0x2C-byte segment row.
    private static VehicleTrackSegment ReadSegmentBody(
        FastFileCursor cursor,
        FastFileCursor rowCursor,
        XBlockAddress rowAddress,
        DbLoadExecutionContext context,
        Dictionary<XBlockAddress, VehicleTrackSegment> segmentsByAddress,
        int depth,
        string memberName)
    {
        EnsureDepth(depth, memberName);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(
            rowCursor,
            XPointerResolutionMode.Direct);
        XPointer<VehicleTrackSector[]> sectorsPointer = ReadPresencePointer<VehicleTrackSector[]>(rowCursor, context);
        int sectorCount = rowCursor.ReadInt32();
        XPointer<XPointer<VehicleTrackSegment>[]> nextBranchesPointer =
            ReadPresencePointer<XPointer<VehicleTrackSegment>[]>(rowCursor, context);
        int nextBranchCount = rowCursor.ReadInt32();
        XPointer<XPointer<VehicleTrackSegment>[]> previousBranchesPointer =
            ReadPresencePointer<XPointer<VehicleTrackSegment>[]>(rowCursor, context);
        int previousBranchCount = rowCursor.ReadInt32();
        IReadOnlyList<float> endEdgeDirection = ReadFloatValues(rowCursor, 2);
        float endEdgeDistance = ReadSingle(rowCursor);
        float totalLength = ReadSingle(rowCursor);

        string? name = context.PointerReader.LoadXString(cursor, namePointer);
        IReadOnlyList<VehicleTrackSector> sectors = ReadSectorArray(
            cursor,
            sectorsPointer,
            NonNegative(sectorCount, $"{memberName}.sectorCount"),
            context,
            $"{memberName}.sectors");
        (IReadOnlyList<XPointer<VehicleTrackSegment>> nextBranchPointers,
            IReadOnlyList<VehicleTrackSegment?> nextBranches) = ReadSegmentPointerArray(
                cursor,
                nextBranchesPointer,
                NonNegative(nextBranchCount, $"{memberName}.nextBranchCount"),
                context,
                segmentsByAddress,
                depth + 1,
                $"{memberName}.nextBranches");
        (IReadOnlyList<XPointer<VehicleTrackSegment>> previousBranchPointers,
            IReadOnlyList<VehicleTrackSegment?> previousBranches) = ReadSegmentPointerArray(
                cursor,
                previousBranchesPointer,
                NonNegative(previousBranchCount, $"{memberName}.previousBranchCount"),
                context,
                segmentsByAddress,
                depth + 1,
                $"{memberName}.previousBranches");

        return new VehicleTrackSegment
        {
            Offset = rowAddress.Offset,
            NamePointer = namePointer,
            Name = name,
            SectorsPointer = sectorsPointer,
            Sectors = sectors,
            SectorCount = sectorCount,
            NextBranchesPointer = nextBranchesPointer,
            NextBranchPointers = nextBranchPointers,
            NextBranches = nextBranches,
            NextBranchCount = nextBranchCount,
            PreviousBranchesPointer = previousBranchesPointer,
            PreviousBranchPointers = previousBranchPointers,
            PreviousBranches = previousBranches,
            PreviousBranchCount = previousBranchCount,
            EndEdgeDirection = endEdgeDirection,
            EndEdgeDistance = endEdgeDistance,
            TotalLength = totalLength
        };
    }

    private static IReadOnlyList<VehicleTrackSector> ReadSectorArray(
        FastFileCursor cursor,
        XPointer<VehicleTrackSector[]> pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            VehicleTrackSector.SerializedSize,
            alignment: 4,
            context,
            memberName);
        if (!hasPayload)
            return [];

        var sectors = new VehicleTrackSector[count];
        for (int index = 0; index < sectors.Length; index++)
        {
            var rowCursor = RowCursor(bytes, address, index, VehicleTrackSector.SerializedSize);
            XBlockAddress rowAddress = address.Add(checked(index * VehicleTrackSector.SerializedSize));
            IReadOnlyList<float> startEdgeDirection = ReadFloatValues(rowCursor, 2);
            float startEdgeDistance = ReadSingle(rowCursor);
            IReadOnlyList<float> leftEdgeDirection = ReadFloatValues(rowCursor, 2);
            float leftEdgeDistance = ReadSingle(rowCursor);
            IReadOnlyList<float> rightEdgeDirection = ReadFloatValues(rowCursor, 2);
            float rightEdgeDistance = ReadSingle(rowCursor);
            float sectorLength = ReadSingle(rowCursor);
            float sectorWidth = ReadSingle(rowCursor);
            float totalPriorLength = ReadSingle(rowCursor);
            float totalFollowingLength = ReadSingle(rowCursor);
            XPointer<VehicleTrackObstacle[]> obstaclesPointer =
                ReadPresencePointer<VehicleTrackObstacle[]>(rowCursor, context);
            int obstacleCount = rowCursor.ReadInt32();
            IReadOnlyList<VehicleTrackObstacle> obstacles = ReadObstacleArray(
                cursor,
                obstaclesPointer,
                NonNegative(obstacleCount, $"{memberName}[{index}].obstacleCount"),
                context,
                $"{memberName}[{index}].obstacles");

            sectors[index] = new VehicleTrackSector
            {
                Offset = rowAddress.Offset,
                StartEdgeDirection = startEdgeDirection,
                StartEdgeDistance = startEdgeDistance,
                LeftEdgeDirection = leftEdgeDirection,
                LeftEdgeDistance = leftEdgeDistance,
                RightEdgeDirection = rightEdgeDirection,
                RightEdgeDistance = rightEdgeDistance,
                SectorLength = sectorLength,
                SectorWidth = sectorWidth,
                TotalPriorLength = totalPriorLength,
                TotalFollowingLength = totalFollowingLength,
                ObstaclesPointer = obstaclesPointer,
                Obstacles = obstacles,
                ObstacleCount = obstacleCount
            };
        }

        return sectors;
    }

    private static IReadOnlyList<VehicleTrackObstacle> ReadObstacleArray(
        FastFileCursor cursor,
        XPointer<VehicleTrackObstacle[]> pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            VehicleTrackObstacle.SerializedSize,
            alignment: 4,
            context,
            memberName);
        if (!hasPayload)
            return [];

        var obstacles = new VehicleTrackObstacle[count];
        var rowCursor = new FastFileCursor(bytes, address);
        for (int index = 0; index < obstacles.Length; index++)
        {
            obstacles[index] = new VehicleTrackObstacle
            {
                Origin = ReadFloatValues(rowCursor, 2),
                Radius = ReadSingle(rowCursor)
            };
        }

        return obstacles;
    }

    private static (IReadOnlyList<XPointer<VehicleTrackSegment>> Pointers,
        IReadOnlyList<VehicleTrackSegment?> Segments) ReadSegmentPointerArray(
        FastFileCursor cursor,
        XPointer<XPointer<VehicleTrackSegment>[]> pointer,
        int count,
        DbLoadExecutionContext context,
        Dictionary<XBlockAddress, VehicleTrackSegment> segmentsByAddress,
        int depth,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            sizeof(int),
            alignment: 4,
            context,
            memberName);
        if (!hasPayload)
            return ([], []);

        var pointerCursor = new FastFileCursor(bytes, address);
        var pointers = new XPointer<VehicleTrackSegment>[count];
        for (int index = 0; index < pointers.Length; index++)
            pointers[index] = ReadPointer<VehicleTrackSegment>(pointerCursor, context);

        var segments = new VehicleTrackSegment?[count];
        for (int index = 0; index < pointers.Length; index++)
        {
            segments[index] = ReadSegmentPointer(
                cursor,
                pointers[index],
                context,
                segmentsByAddress,
                depth,
                $"{memberName}[{index}]");
        }

        return (pointers, segments);
    }

    // Segment references may be null, inline (-1), or packed.
    private static VehicleTrackSegment? ReadSegmentPointer(
        FastFileCursor cursor,
        XPointer<VehicleTrackSegment> pointer,
        DbLoadExecutionContext context,
        Dictionary<XBlockAddress, VehicleTrackSegment> segmentsByAddress,
        int depth,
        string memberName)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<VehicleTrackSegment>(
                pointer.Untyped,
                VehicleTrackSegment.SerializedSize,
                memberName);
            return pointer.PackedAddress is { } address && segmentsByAddress.TryGetValue(address, out VehicleTrackSegment? segment)
                ? segment
                : null;
        }

        if (pointer.Type != PointerType.Inline)
        {
            throw new InvalidDataException(
                $"{memberName} pointer 0x{pointer.Raw:X8} is not null, inline -1, or packed.");
        }

        EnsureDepth(depth, memberName);
        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(
            cursor,
            VehicleTrackSegment.SerializedSize,
            out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} patched to {targetAddress}, but loaded at {loadedAddress}.");

        VehicleTrackSegment loaded = ReadSegmentBody(
            cursor,
            new FastFileCursor(bytes, targetAddress),
            targetAddress,
            context,
            segmentsByAddress,
            depth,
            memberName);
        segmentsByAddress[targetAddress] = loaded;
        return loaded;
    }

    private static (byte[] Bytes, XBlockAddress Address, bool HasPayload) LoadPresenceArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        int byteCount = checked(NonNegative(count, memberName) * stride);
        if (pointer.Raw == 0)
            return ([], context.Blocks.CurrentAddress, HasPayload: false);

        XBlockAddress targetAddress = PatchPresenceCell(pointer, alignment, context, memberName);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} patched to {targetAddress}, but loaded at {loadedAddress}.");
        return (bytes, targetAddress, HasPayload: true);
    }

    private static XBlockAddress PatchPresenceCell(
        XPointerReference pointer,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        if (pointer.Raw == 0)
            throw new InvalidDataException($"{memberName} is null and cannot be materialized.");
        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"{memberName} has no destination cell.");

        context.Blocks.AlignCurrent(alignment);
        XBlockAddress targetAddress = context.Blocks.CurrentAddress;
        context.Blocks.WriteInt32(cellAddress, XPointerCodec.Encode(targetAddress));
        return targetAddress;
    }

    private static XPointer<T> ReadPointer<T>(FastFileCursor cursor, DbLoadExecutionContext context) =>
        context.PointerReader.ReadPointer<T>(cursor, XPointerResolutionMode.Direct);

    private static XPointer<T> ReadPresencePointer<T>(FastFileCursor cursor, DbLoadExecutionContext context) =>
        ReadPointer<T>(cursor, context);

    private static FastFileCursor RowCursor(
        byte[] bytes,
        XBlockAddress address,
        int index,
        int stride)
    {
        int offset = checked(index * stride);
        return new FastFileCursor(bytes.AsSpan(offset, stride).ToArray(), address.Add(offset));
    }

    private static void EnsureDepth(int depth, string memberName)
    {
        if (depth > MaximumSegmentDepth)
        {
            throw new InvalidDataException(
                $"{memberName} exceeds the managed VehicleTrack recursion limit of {MaximumSegmentDepth}.");
        }
    }

    private static int NonNegative(int count, string name)
    {
        if (count < 0)
            throw new InvalidDataException($"{name} is negative: {count}.");
        return count;
    }

    private static IReadOnlyList<float> ReadFloatValues(FastFileCursor cursor, int count)
    {
        var values = new float[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = ReadSingle(cursor);
        return values;
    }

    private static float ReadSingle(FastFileCursor cursor) =>
        BitConverter.Int32BitsToSingle(cursor.ReadInt32());
}
