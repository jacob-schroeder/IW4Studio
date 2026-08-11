using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.Sound;

public sealed class SndCurveLoader
{
    public SndCurve LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(
                cursor,
                pointer,
                context,
                requireAsset: true)
            ?? throw new InvalidDataException("Top-level SndCurve pointer resolved to null.");
    }

    public SndCurve? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(
            cursor,
            pointer,
            context,
            requireAsset: false);
    }

    private static SndCurve? LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (requireAsset)
                throw new InvalidDataException("Top-level SndCurve pointer is null.");

            return null;
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<SndCurve>(
                pointer,
                SndCurve.SerializedSize,
                "SndCurve");
            SndCurve? canonical = context.ResolveSndCurve(pointer);
            if (canonical is null)
            {
                if (!requireAsset)
                    return null;

                throw new InvalidDataException(
                    $"Top-level SndCurve pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical SndCurve asset.");
            }

            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"SndCurve pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            SndCurve curve = ReadSndCurve(cursor, rootAddress, context);
            SndCurve canonical = context.DB_AddXAsset(curve, providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The fixed 0x88-byte root is staged in TEMP, followed by its filename
    // XString in LARGE.
    private static SndCurve ReadSndCurve(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            SndCurve.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"SndCurve pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> filenamePointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);
        ushort knotCount = rootCursor.ReadUInt16();
        ushort padding = rootCursor.ReadUInt16();
        var knots = new SndCurveKnot[SndCurve.MaxKnotCount];
        for (int index = 0; index < knots.Length; index++)
            knots[index] = new SndCurveKnot(ReadSingle(rootCursor), ReadSingle(rootCursor));

        if (rootCursor.Offset != SndCurve.SerializedSize)
        {
            throw new InvalidDataException(
                $"SndCurve consumed 0x{rootCursor.Offset:X} bytes instead of 0x{SndCurve.SerializedSize:X}.");
        }

        string? filename;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            filename = context.PointerReader.LoadXString(cursor, filenamePointer);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new SndCurve
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            FilenamePointer = filenamePointer,
            Filename = filename,
            KnotCount = knotCount,
            Padding = padding,
            Knots = knots
        };
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        SndCurve canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed SndCurve pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical SndCurve has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }
}
