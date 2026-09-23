using IW4.Loaders.Database;
using IW4.Game.Assets.Sound;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.Sound;

public sealed class SndCurveLoader : XAssetLoader<SndCurve>
{
    public SndCurveLoader() : base(XAssetType.SndCurve, SndCurve.SerializedSize, "SndCurve")
    {
    }

    protected override SndCurve? HandleUnresolvedReference(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (!requireAsset)
            return null;

        return base.HandleUnresolvedReference(pointer, context, requireAsset);
    }

    protected override SndCurve RegisterAsset(
        SndCurve asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(asset, providerRegistration);
    }

    // The fixed 0x88-byte root is staged in TEMP, followed by its filename
    // XString in LARGE.
    protected override SndCurve ReadBody(
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
            knots[index] = new SndCurveKnot(rootCursor.ReadSingle(), rootCursor.ReadSingle());

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


}
