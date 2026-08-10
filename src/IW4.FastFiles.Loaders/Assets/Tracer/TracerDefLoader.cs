using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Tracer;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.Tracer;

public sealed class TracerDefLoader
{
    private readonly MaterialLoader _materialLoader = new();

    public TracerDefAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointer(cursor, pointer, context)
            ?? throw new InvalidDataException("A top-level TracerDef XAsset cannot have a null body.");
    }

    // Top-level assets and nested WeaponDef.tracer references share this path.
    // Both canonicalize through type 0x27.
    public TracerDefAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<TracerDefAsset>(
                pointer,
                TracerDefAsset.SerializedSize,
                "TracerDef");
            TracerDefAsset canonical = context.ResolveTracerDef(pointer)
                ?? throw new InvalidDataException(
                    $"TracerDef pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical TracerDef asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed TracerDef pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical TracerDef has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"TracerDef pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            TracerDefAsset tracer = ReadTracerDef(cursor, rootAddress, context);
            TracerDefAsset canonical = context.DB_AddXAsset(tracer, providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The root is staged in TEMP; its name and MaterialPtr resolve in LARGE,
    // in that order.
    private TracerDefAsset ReadTracerDef(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            TracerDefAsset.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"TracerDef pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);
        XPointer<MaterialAsset> materialPointer = context.PointerReader.ReadPointer<MaterialAsset>(
            rootCursor,
            XPointerResolutionMode.AliasCell,
            XPointerNullability.Nullable);
        uint drawInterval = rootCursor.ReadUInt32();
        float speed = ReadSingle(rootCursor);
        float beamLength = ReadSingle(rootCursor);
        float beamWidth = ReadSingle(rootCursor);
        float screwRadius = ReadSingle(rootCursor);
        float screwDistance = ReadSingle(rootCursor);
        var colors = new TracerColor[TracerDefAsset.ColorCount];
        for (int index = 0; index < colors.Length; index++)
        {
            colors[index] = new TracerColor(
                ReadSingle(rootCursor),
                ReadSingle(rootCursor),
                ReadSingle(rootCursor),
                ReadSingle(rootCursor));
        }

        if (rootCursor.Offset != TracerDefAsset.SerializedSize)
        {
            throw new InvalidDataException(
                $"TracerDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{TracerDefAsset.SerializedSize:X}.");
        }

        string? name;
        MaterialAsset? material;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            material = _materialLoader.LoadFromPointer(cursor, materialPointer.Untyped, context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new TracerDefAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            MaterialPointer = materialPointer,
            Material = material,
            DrawInterval = drawInterval,
            Speed = speed,
            BeamLength = beamLength,
            BeamWidth = beamWidth,
            ScrewRadius = screwRadius,
            ScrewDistance = screwDistance,
            Colors = colors
        };
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }
}
