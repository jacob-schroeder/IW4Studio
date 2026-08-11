using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen PhysCollmap provider. Captured destination coordinates are used only
/// while freezing direct-storage views and never survive in the recipe.
/// </summary>
internal sealed class PhysCollmapLinkRecipe : AssetLinkRecipe
{
    private PhysCollmapLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        PhysCollmapAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        Root = CreateOwnedRoot(definition, freeze);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        PhysCollmapAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.PhysCollmap,
                originalSerializedName,
                freeze);
        }

        return new PhysCollmapLinkRecipe(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private static void ValidateReferenceShape(PhysCollmapAsset definition)
    {
        if (definition.Count != 0 || definition.Geoms.Count != 0 ||
            !IsZero(definition.Mass.CenterOfMass) ||
            !IsZero(definition.Mass.MomentsOfInertia) ||
            !IsZero(definition.Mass.ProductsOfInertia) ||
            !IsZero(definition.Bounds.MidPoint) ||
            !IsZero(definition.Bounds.HalfSize))
        {
            throw new InvalidDataException(
                "A comma-prefixed PhysCollmap provider must have a zeroed reference body.");
        }
    }

    private LinkStorageSymbol CreateOwnedRoot(
        PhysCollmapAsset definition,
        LinkAssetFreezeScope freeze)
    {
        if (definition.Count < 0 || definition.Count != definition.Geoms.Count)
        {
            throw new InvalidDataException(
                "PhysCollmap.Count must equal its nonnegative detached geometry count.");
        }

        LinkStorageTarget? geoms = definition.Geoms.Count == 0
            ? null
            : CreateGeomTable(
                definition.GeomsPointer.Untyped,
                definition.Geoms,
                freeze);
        if (definition.Geoms.Count == 0 && definition.GeomsPointer.Type != PointerType.Null)
        {
            throw new NotSupportedException(
                "PhysCollmap cannot preserve a present-empty geometry allocation without retained storage identity.");
        }

        var writer = new LinkTemplateWriter(PhysCollmapAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.Count);
        writer.Skip(sizeof(int));
        WriteVec3(writer, definition.Mass.CenterOfMass, "PhysCollmap.Mass.CenterOfMass");
        WriteVec3(writer, definition.Mass.MomentsOfInertia, "PhysCollmap.Mass.MomentsOfInertia");
        WriteVec3(writer, definition.Mass.ProductsOfInertia, "PhysCollmap.Mass.ProductsOfInertia");
        WriteBounds(writer, definition.Bounds, "PhysCollmap.Bounds");

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => geoms is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    Direct(root, 0x08, geoms.Value, "PhysCollmap.Geoms")
                ]);
    }

    private static LinkStorageTarget CreateGeomTable(
        XPointerReference pointer,
        IReadOnlyList<PhysGeomInfo> geoms,
        LinkAssetFreezeScope freeze)
    {
        var wrappers = new LinkStorageTarget?[geoms.Count];
        var writer = new LinkTemplateWriter(
            checked(geoms.Count * PhysGeomInfo.SerializedSize));
        for (int index = 0; index < geoms.Count; index++)
        {
            PhysGeomInfo geom = geoms[index] ?? throw new InvalidDataException(
                $"PhysCollmap.Geoms[{index}] cannot be null.");
            if (geom.Orientation.Count != 3)
            {
                throw new InvalidDataException(
                    $"PhysCollmap.Geoms[{index}].Orientation requires exactly three vectors.");
            }

            wrappers[index] = ResolveBrushWrapper(
                geom,
                freeze,
                $"PhysCollmap.Geoms[{index}].BrushWrapper");
            writer.Skip(sizeof(int));
            writer.WriteInt32(geom.Type);
            for (int orientation = 0; orientation < geom.Orientation.Count; orientation++)
            {
                WriteVec3(
                    writer,
                    geom.Orientation[orientation],
                    $"PhysCollmap.Geoms[{index}].Orientation[{orientation}]");
            }
            WriteBounds(writer, geom.Bounds, $"PhysCollmap.Geoms[{index}].Bounds");
        }

        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (owner, addend) => wrappers
                .Select((target, index) => (target, index))
                .Where(item => item.target is not null)
                .Select(item => Direct(
                    owner,
                    checked(addend + item.index * PhysGeomInfo.SerializedSize),
                    item.target!.Value,
                    $"PhysCollmap.Geoms[{item.index}].BrushWrapper")),
            "PhysCollmap.Geoms");
    }

    private static LinkStorageTarget? ResolveBrushWrapper(
        PhysGeomInfo geom,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        XPointerReference pointer = geom.BrushWrapperPointer.Untyped;
        if (pointer.Type == PointerType.Offset && geom.BrushWrapper is null)
        {
            return freeze.ResolveStorage(
                pointer,
                BrushWrapper.SerializedSize,
                XFileBlockType.LARGE,
                fieldPath);
        }
        if (geom.BrushWrapper is null)
        {
            if (pointer.Type != PointerType.Null)
                throw new InvalidDataException($"{fieldPath} has no retained semantic body.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        return CreateBrushWrapper(
            pointer,
            geom.BrushWrapper,
            freeze,
            fieldPath);
    }

    private static LinkStorageTarget CreateBrushWrapper(
        XPointerReference pointer,
        BrushWrapper wrapper,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        CBrush brush = wrapper.Brush ?? throw new InvalidDataException(
            $"{fieldPath}.Brush cannot be null.");
        if (brush.AxialMaterialNum.Count != 6 ||
            brush.FirstAdjacentSideOffsets.Count != 6 ||
            brush.EdgeCount.Count != 6)
        {
            throw new InvalidDataException(
                $"{fieldPath}.Brush fixed axial and adjacency arrays require six entries each.");
        }
        bool packedAdjacent =
            brush.BaseAdjacentSidePointer.Type == PointerType.Offset;
        if (wrapper.TotalEdgeCount < 0 ||
            (!packedAdjacent &&
             brush.BaseAdjacentSide.Count != wrapper.TotalEdgeCount) ||
            (packedAdjacent && brush.BaseAdjacentSide.Count != 0))
        {
            throw new InvalidDataException(
                $"{fieldPath}.BaseAdjacentSide must equal nonnegative TotalEdgeCount.");
        }

        LinkStorageTarget? sides = ResolveSides(brush, freeze, $"{fieldPath}.Brush.Sides");
        LinkStorageTarget? adjacent = ResolveBytes(
            brush.BaseAdjacentSidePointer.Untyped,
            brush.BaseAdjacentSide,
            wrapper.TotalEdgeCount,
            alignment: 1,
            freeze,
            $"{fieldPath}.Brush.BaseAdjacentSide");
        LinkStorageTarget? planes = ResolvePlanes(wrapper, brush.NumSides, freeze, $"{fieldPath}.Planes");

        var writer = new LinkTemplateWriter(BrushWrapper.SerializedSize);
        WriteBounds(writer, wrapper.Bounds, $"{fieldPath}.Bounds");
        writer.WriteUInt16(brush.NumSides);
        writer.WriteUInt16(brush.GlassPieceIndex);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        foreach (short value in brush.AxialMaterialNum)
            writer.WriteUInt16(unchecked((ushort)value));
        writer.WriteBytes(brush.FirstAdjacentSideOffsets.ToArray());
        writer.WriteBytes(brush.EdgeCount.ToArray());
        writer.WriteInt32(wrapper.TotalEdgeCount);
        writer.Skip(sizeof(int));

        byte[] bytes = writer.Complete();
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>> operations =
            (owner, addend) => OrderedDirectOperations(
                owner,
                (addend + 0x1c, sides, $"{fieldPath}.Brush.Sides"),
                (addend + 0x20, adjacent, $"{fieldPath}.Brush.BaseAdjacentSide"),
                (addend + 0x40, planes, $"{fieldPath}.Planes"));
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                fieldPath);
    }

    private static LinkStorageTarget? ResolveSides(
        CBrush brush,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        XPointerReference pointer = brush.SidesPointer.Untyped;
        int byteCount = checked(brush.NumSides * CBrushSide.SerializedSize);
        if (pointer.Type == PointerType.Offset && brush.Sides.Count != brush.NumSides)
        {
            return freeze.ResolveStorage(
                pointer,
                byteCount,
                XFileBlockType.LARGE,
                fieldPath);
        }
        if (brush.Sides.Count == 0)
        {
            if (brush.NumSides != 0 || pointer.Type != PointerType.Null)
                throw new InvalidDataException($"{fieldPath} has no retained semantic rows.");
            return null;
        }
        if (brush.Sides.Count != brush.NumSides)
            throw new InvalidDataException($"{fieldPath} count must equal NumSides.");
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);

        var planeTargets = new LinkStorageTarget?[brush.Sides.Count];
        var writer = new LinkTemplateWriter(byteCount);
        for (int index = 0; index < brush.Sides.Count; index++)
        {
            CBrushSide side = brush.Sides[index] ?? throw new InvalidDataException(
                $"{fieldPath}[{index}] cannot be null.");
            planeTargets[index] = ResolvePlane(
                side,
                freeze,
                $"{fieldPath}[{index}].Plane");
            writer.Skip(sizeof(int));
            writer.WriteUInt16(side.MaterialNum);
            writer.WriteByte(side.FirstAdjacentSideOffset);
            writer.WriteByte(side.EdgeCount);
        }

        byte[] bytes = writer.Complete();
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>> operations =
            (owner, addend) => planeTargets
                .Select((target, index) => (target, index))
                .Where(item => item.target is not null)
                .Select(item => Direct(
                    owner,
                    checked(addend + item.index * CBrushSide.SerializedSize),
                    item.target!.Value,
                    $"{fieldPath}[{item.index}].Plane"));
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                fieldPath);
    }

    private static LinkStorageTarget? ResolvePlane(
        CBrushSide side,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        XPointerReference pointer = side.PlanePointer.Untyped;
        if (pointer.Type == PointerType.Offset && side.Plane is null)
        {
            return freeze.ResolveStorage(
                pointer,
                CPlane.SerializedSize,
                XFileBlockType.LARGE,
                fieldPath);
        }
        if (side.Plane is null)
        {
            if (pointer.Type != PointerType.Null)
                throw new InvalidDataException($"{fieldPath} has no retained semantic plane.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        return FreezePlaneStorage(
            pointer,
            [side.Plane],
            freeze,
            fieldPath);
    }

    private static LinkStorageTarget? ResolvePlanes(
        BrushWrapper wrapper,
        int count,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        XPointerReference pointer = wrapper.PlanesPointer.Untyped;
        int byteCount = checked(count * CPlane.SerializedSize);
        if (pointer.Type == PointerType.Offset && wrapper.Planes.Count != count)
        {
            return freeze.ResolveStorage(
                pointer,
                byteCount,
                XFileBlockType.LARGE,
                fieldPath);
        }
        if (wrapper.Planes.Count == 0)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException(
                    $"{fieldPath} cannot preserve a present-empty plane allocation.");
            return null;
        }
        if (wrapper.Planes.Count != count)
            throw new InvalidDataException($"{fieldPath} must contain one plane per brush side.");
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        return FreezePlaneStorage(pointer, wrapper.Planes, freeze, fieldPath);
    }

    private static LinkStorageTarget FreezePlaneStorage(
        XPointerReference pointer,
        IReadOnlyList<CPlane> planes,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        var writer = new LinkTemplateWriter(checked(planes.Count * CPlane.SerializedSize));
        for (int index = 0; index < planes.Count; index++)
            WritePlane(writer, planes[index], $"CPlane[{index}]");
        byte[] bytes = writer.Complete();
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations: null,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations: null,
                fieldPath);
    }

    private static LinkStorageTarget? ResolveBytes(
        XPointerReference pointer,
        IReadOnlyList<byte> values,
        int expectedCount,
        int alignment,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (pointer.Type == PointerType.Offset && values.Count != expectedCount)
        {
            return freeze.ResolveStorage(
                pointer,
                expectedCount,
                XFileBlockType.LARGE,
                fieldPath);
        }
        if (values.Count != expectedCount)
            throw new InvalidDataException($"{fieldPath} requires exactly {expectedCount} bytes.");
        if (values.Count == 0)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException($"{fieldPath} cannot preserve present-empty storage.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        byte[] bytes = values.ToArray();
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment,
                operations: null,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment,
                operations: null,
                fieldPath);
    }

    private static IEnumerable<LinkOperation> OrderedDirectOperations(
        LinkStorageSymbol owner,
        params (int Offset, LinkStorageTarget? Target, string Path)[] values)
    {
        foreach ((int offset, LinkStorageTarget? target, string path) in values)
        {
            if (target is { } value)
                yield return Direct(owner, offset, value, path);
        }
    }

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int offset,
        LinkStorageTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, offset),
            target.View,
            target.CanMaterializeRoot,
            fieldPath);

    private static void WritePlane(
        LinkTemplateWriter writer,
        CPlane plane,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(plane);
        if (plane.Pad12.Count != 2)
            throw new InvalidDataException($"{fieldPath}.Pad12 requires exactly two bytes.");
        WriteVec3(writer, plane.Normal, $"{fieldPath}.Normal");
        WriteSingle(writer, plane.Dist, $"{fieldPath}.Dist");
        writer.WriteByte(plane.Type);
        writer.WriteByte(plane.SignBits);
        writer.WriteBytes(plane.Pad12.ToArray());
    }

    private static void WriteBounds(
        LinkTemplateWriter writer,
        Bounds bounds,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        WriteVec3(writer, bounds.MidPoint, $"{fieldPath}.MidPoint");
        WriteVec3(writer, bounds.HalfSize, $"{fieldPath}.HalfSize");
    }

    private static void WriteVec3(
        LinkTemplateWriter writer,
        Vec3 value,
        string fieldPath)
    {
        WriteSingle(writer, value.X, $"{fieldPath}.X");
        WriteSingle(writer, value.Y, $"{fieldPath}.Y");
        WriteSingle(writer, value.Z, $"{fieldPath}.Z");
    }

    private static void WriteSingle(
        LinkTemplateWriter writer,
        float value,
        string fieldPath)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"{fieldPath} must be finite.");
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value));
    }

    private static bool IsZero(Vec3 value) =>
        BitConverter.SingleToInt32Bits(value.X) == 0 &&
        BitConverter.SingleToInt32Bits(value.Y) == 0 &&
        BitConverter.SingleToInt32Bits(value.Z) == 0;

    private static void RequireAuthoredOrInline(
        XPointerReference pointer,
        string fieldPath)
    {
        if (pointer.Type is not (PointerType.Null or PointerType.Inline))
        {
            throw new NotSupportedException(
                $"{fieldPath} uses unsupported direct source form {pointer.Type}.");
        }
    }

}
