using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Physics;

public sealed record PhysCollmapPointerLoadResult(
    PhysCollmapAsset? Canonical,
    PhysCollmapAsset? IncomingDefinition);

public sealed class PhysCollmapLoader
{
    public PhysCollmapAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: true).Canonical
            ?? throw new InvalidDataException("Top-level PhysCollmap pointer resolved to null.");
    }

    public PhysCollmapAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: false).Canonical;
    }

    public PhysCollmapPointerLoadResult LoadFromPointerWithMaterialization(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context) =>
        LoadFromPointerCore(cursor, pointer, context, requireAsset: false);

    private static PhysCollmapPointerLoadResult LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (requireAsset)
                throw new InvalidDataException("Top-level PhysCollmap pointer is null.");

            return new PhysCollmapPointerLoadResult(null, null);
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<PhysCollmapAsset>(
                pointer,
                PhysCollmapAsset.SerializedSize,
                "PhysCollmap");
            PhysCollmapAsset? canonical = context.ResolveCanonicalAsset<PhysCollmapAsset>(
                pointer,
                XAssetType.PhysCollmap);
            if (canonical is null)
            {
                if (!requireAsset)
                    return new PhysCollmapPointerLoadResult(null, null);

                throw new InvalidDataException(
                    $"Top-level PhysCollmap pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical PhysCollmap asset.");
            }

            PatchCanonicalPointerCell(pointer, canonical, context);
            return new PhysCollmapPointerLoadResult(canonical, null);
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"PhysCollmap pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        return LoadInlineOrInsert(cursor, pointer, context);
    }

    private static PhysCollmapPointerLoadResult LoadInlineOrInsert(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            PhysCollmapAsset asset = ReadPhysCollmap(cursor, rootAddress, context);
            PhysCollmapAsset canonical = context.DB_AddXAsset(
                XAssetType.PhysCollmap,
                asset.Name,
                asset,
                providerRegistration);

            return new PhysCollmapPointerLoadResult(canonical, asset);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static PhysCollmapAsset ReadPhysCollmap(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, PhysCollmapAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"PhysCollmap pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XString namePointer = ReadXStringPointer(rootCursor, context);
        int count = rootCursor.ReadInt32();
        XPointer<PhysGeomInfo[]> geomsPointer = ReadPointer<PhysGeomInfo[]>(rootCursor, context, XPointerResolutionMode.Direct);
        PhysMass mass = ReadPhysMass(rootCursor);
        Bounds bounds = ReadBounds(rootCursor);

        if (rootCursor.Offset != PhysCollmapAsset.SerializedSize)
            throw new InvalidDataException($"PhysCollmap consumed 0x{rootCursor.Offset:X} bytes instead of 0x{PhysCollmapAsset.SerializedSize:X}.");

        string? name;
        IReadOnlyList<PhysGeomInfo> geoms;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            geoms = ReadPhysGeomInfoArray(cursor, geomsPointer.Untyped, count, context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new PhysCollmapAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            Count = count,
            GeomsPointer = geomsPointer,
            Geoms = geoms,
            Mass = mass,
            Bounds = bounds
        };
    }

    private static IReadOnlyList<PhysGeomInfo> ReadPhysGeomInfoArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative PhysGeomInfo count {count}.");

        if (pointer.Type == PointerType.Null)
            return [];

        XBlockAddress address = PatchCurrentPointerCell<PhysGeomInfo[]>(pointer, alignment: 4, checked(count * PhysGeomInfo.SerializedSize), "PhysGeomInfo[]", context);
        if (pointer.Type == PointerType.Offset || count == 0)
            return [];

        byte[] bytes = context.Blocks.Load(cursor, checked(count * PhysGeomInfo.SerializedSize));
        var geoms = new PhysGeomInfo[count];
        var brushPointers = new XPointer<BrushWrapper>[count];

        for (int i = 0; i < count; i++)
        {
            int entryOffset = i * PhysGeomInfo.SerializedSize;
            var entryCursor = new FastFileCursor(
                bytes.AsSpan(entryOffset, PhysGeomInfo.SerializedSize).ToArray(),
                address with { Offset = address.Offset + entryOffset });

            XPointer<BrushWrapper> brushPointer = ReadPointer<BrushWrapper>(entryCursor, context, XPointerResolutionMode.Direct);
            brushPointers[i] = brushPointer;
            int type = entryCursor.ReadInt32();
            Vec3[] orientation = [ReadVec3(entryCursor), ReadVec3(entryCursor), ReadVec3(entryCursor)];
            Bounds bounds = ReadBounds(entryCursor);

            geoms[i] = new PhysGeomInfo
            {
                BrushWrapperPointer = brushPointer,
                Type = type,
                Orientation = orientation,
                Bounds = bounds
            };
        }

        for (int i = 0; i < geoms.Length; i++)
        {
            geoms[i] = new PhysGeomInfo
            {
                BrushWrapperPointer = geoms[i].BrushWrapperPointer,
                BrushWrapper = ReadBrushWrapper(cursor, brushPointers[i].Untyped, context),
                Type = geoms[i].Type,
                Orientation = geoms[i].Orientation,
                Bounds = geoms[i].Bounds
            };
        }

        return geoms;
    }

    private static BrushWrapper? ReadBrushWrapper(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        XBlockAddress address = PatchCurrentPointerCell<BrushWrapper>(pointer, alignment: 4, BrushWrapper.SerializedSize, "BrushWrapper", context);
        if (pointer.Type == PointerType.Offset)
            return null;

        byte[] bytes = context.Blocks.Load(cursor, BrushWrapper.SerializedSize);
        var wrapperCursor = new FastFileCursor(bytes, address);
        Bounds bounds = ReadBounds(wrapperCursor);

        var brushCursor = new FastFileCursor(
            bytes.AsSpan(0x18, CBrush.SerializedSize).ToArray(),
            address with { Offset = address.Offset + 0x18 });
        CBrush brushRoot = ReadCBrushRoot(brushCursor, context);
        wrapperCursor.Skip(0x3c - wrapperCursor.Offset);
        int totalEdgeCount = wrapperCursor.ReadInt32();
        // The brush-side payload can materialize the shared plane range after
        // this header is read. Defer target validation until that payload has
        // been processed below, immediately before the plane view is used.
        XPointer<CPlane[]> planesPointer = context.PointerReader.ReadDeferredPointer<CPlane[]>(
            wrapperCursor,
            XPointerResolutionMode.Direct);

        IReadOnlyList<CBrushSide> sides = ReadCBrushSideArray(cursor, brushRoot.SidesPointer.Untyped, brushRoot.NumSides, context);
        IReadOnlyList<byte> baseAdjacentSide = ReadByteArray(cursor, brushRoot.BaseAdjacentSidePointer.Untyped, totalEdgeCount, context);
        CBrush brush = new()
        {
            NumSides = brushRoot.NumSides,
            GlassPieceIndex = brushRoot.GlassPieceIndex,
            SidesPointer = brushRoot.SidesPointer,
            Sides = sides,
            BaseAdjacentSidePointer = brushRoot.BaseAdjacentSidePointer,
            BaseAdjacentSide = baseAdjacentSide,
            AxialMaterialNum = brushRoot.AxialMaterialNum,
            FirstAdjacentSideOffsets = brushRoot.FirstAdjacentSideOffsets,
            EdgeCount = brushRoot.EdgeCount
        };

        IReadOnlyList<CPlane> planes = ReadCPlaneArray(cursor, planesPointer.Untyped, brush.NumSides, context);
        return new BrushWrapper
        {
            Bounds = bounds,
            Brush = brush,
            TotalEdgeCount = totalEdgeCount,
            PlanesPointer = planesPointer,
            Planes = planes
        };
    }

    private static CBrush ReadCBrushRoot(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        ushort numSides = cursor.ReadUInt16();
        ushort glassPieceIndex = cursor.ReadUInt16();
        XPointer<CBrushSide[]> sidesPointer = ReadPointer<CBrushSide[]>(cursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> baseAdjacentSidePointer = ReadPointer<byte[]>(cursor, context, XPointerResolutionMode.Direct);
        var axialMaterialNum = new short[6];
        for (int i = 0; i < axialMaterialNum.Length; i++)
            axialMaterialNum[i] = unchecked((short)cursor.ReadUInt16());

        byte[] firstAdjacentSideOffsets = cursor.ReadBytes(6);
        byte[] edgeCount = cursor.ReadBytes(6);
        return new CBrush
        {
            NumSides = numSides,
            GlassPieceIndex = glassPieceIndex,
            SidesPointer = sidesPointer,
            BaseAdjacentSidePointer = baseAdjacentSidePointer,
            AxialMaterialNum = axialMaterialNum,
            FirstAdjacentSideOffsets = firstAdjacentSideOffsets,
            EdgeCount = edgeCount
        };
    }

    private static IReadOnlyList<CBrushSide> ReadCBrushSideArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return [];

        XBlockAddress address = PatchCurrentPointerCell<CBrushSide[]>(pointer, alignment: 4, checked(count * CBrushSide.SerializedSize), "CBrushSide[]", context);
        if (pointer.Type == PointerType.Offset || count == 0)
            return [];

        byte[] bytes = context.Blocks.Load(cursor, checked(count * CBrushSide.SerializedSize));
        var sides = new CBrushSide[count];
        for (int i = 0; i < sides.Length; i++)
        {
            int entryOffset = i * CBrushSide.SerializedSize;
            var entryCursor = new FastFileCursor(
                bytes.AsSpan(entryOffset, CBrushSide.SerializedSize).ToArray(),
                address with { Offset = address.Offset + entryOffset });

            XPointer<CPlane> planePointer = ReadPointer<CPlane>(entryCursor, context, XPointerResolutionMode.Direct);
            sides[i] = new CBrushSide
            {
                PlanePointer = planePointer,
                Plane = ReadCPlanePointer(cursor, planePointer.Untyped, context),
                MaterialNum = entryCursor.ReadUInt16(),
                FirstAdjacentSideOffset = entryCursor.ReadByte(),
                EdgeCount = entryCursor.ReadByte()
            };
        }

        return sides;
    }

    private static IReadOnlyList<CPlane> ReadCPlaneArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return [];

        XBlockAddress address = PatchCurrentPointerCell<CPlane[]>(pointer, alignment: 4, checked(count * CPlane.SerializedSize), "CPlane[]", context);
        if (pointer.Type == PointerType.Offset || count == 0)
            return [];

        byte[] bytes = context.Blocks.Load(cursor, checked(count * CPlane.SerializedSize));
        var planes = new CPlane[count];
        for (int i = 0; i < planes.Length; i++)
        {
            int entryOffset = i * CPlane.SerializedSize;
            var entryCursor = new FastFileCursor(
                bytes.AsSpan(entryOffset, CPlane.SerializedSize).ToArray(),
                address with { Offset = address.Offset + entryOffset });
            planes[i] = ReadCPlane(entryCursor);
        }

        return planes;
    }

    private static CPlane? ReadCPlanePointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        XBlockAddress address = PatchCurrentPointerCell<CPlane>(pointer, alignment: 4, CPlane.SerializedSize, "CPlane", context);
        if (pointer.Type == PointerType.Offset)
            return null;

        byte[] bytes = context.Blocks.Load(cursor, CPlane.SerializedSize);
        return ReadCPlane(new FastFileCursor(bytes, address));
    }

    private static CPlane ReadCPlane(FastFileCursor cursor)
    {
        Vec3 normal = ReadVec3(cursor);
        float dist = ReadSingle(cursor);
        byte type = cursor.ReadByte();
        byte signBits = cursor.ReadByte();
        byte[] pad12 = cursor.ReadBytes(2);
        return new CPlane
        {
            Normal = normal,
            Dist = dist,
            Type = type,
            SignBits = signBits,
            Pad12 = pad12
        };
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative byte count {count}.");

        if (pointer.Type == PointerType.Null)
            return [];

        PatchCurrentPointerCell<byte[]>(pointer, alignment: 1, count, "byte[]", context);
        if (pointer.Type == PointerType.Offset || count == 0)
            return [];

        return context.Blocks.Load(cursor, count);
    }

    private static XBlockAddress PatchCurrentPointerCell<T>(
        XPointerReference pointer,
        int alignment,
        int byteCount,
        string targetName,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<T>(pointer, byteCount, targetName);
            return pointer.PackedAddress ?? throw new InvalidDataException($"Offset pointer 0x{pointer.Raw:X8} has no packed address for {targetName}.");
        }

        if (pointer.Type != PointerType.Inline)
            throw new NotSupportedException($"PhysCollmap {targetName} pointer 0x{pointer.Raw:X8} uses unsupported source sentinel {pointer.Type}.");

        return context.PointerReader.PatchInlinePointerCell(pointer, alignment);
    }

    private static PhysMass ReadPhysMass(FastFileCursor cursor)
    {
        return new PhysMass
        {
            CenterOfMass = ReadVec3(cursor),
            MomentsOfInertia = ReadVec3(cursor),
            ProductsOfInertia = ReadVec3(cursor)
        };
    }

    private static Bounds ReadBounds(FastFileCursor cursor)
    {
        return new Bounds
        {
            MidPoint = ReadVec3(cursor),
            HalfSize = ReadVec3(cursor)
        };
    }

    private static Vec3 ReadVec3(FastFileCursor cursor)
    {
        return new Vec3
        {
            X = ReadSingle(cursor),
            Y = ReadSingle(cursor),
            Z = ReadSingle(cursor)
        };
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode) => context.PointerReader.ReadPointer<T>(cursor, mode);

    private static XString ReadXStringPointer(FastFileCursor cursor, DbLoadExecutionContext context) =>
        ReadPointer<string>(cursor, context, XPointerResolutionMode.Direct);

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        PhysCollmapAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed PhysCollmap pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical PhysCollmap has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }
}
