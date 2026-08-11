using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.Physics;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.Physics;

public sealed class PhysPresetLoader
{
    public PhysPresetAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: true)
            ?? throw new InvalidDataException("Top-level PhysPreset pointer resolved to null.");
    }

    public PhysPresetAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: false);
    }

    private static PhysPresetAsset? LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (requireAsset)
                throw new InvalidDataException("Top-level PhysPreset pointer is null.");

            return null;
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<PhysPresetAsset>(
                pointer,
                PhysPresetAsset.SerializedSize,
                "PhysPreset");
            PhysPresetAsset? canonical = context.ResolvePhysPreset(pointer);
            if (canonical is null)
            {
                if (!requireAsset)
                    return null;

                throw new InvalidDataException(
                    $"Top-level PhysPreset pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical PhysPreset asset.");
            }

            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"PhysPreset pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            PhysPresetAsset physPreset = ReadPhysPreset(cursor, rootAddress, context);
            PhysPresetAsset canonical = context.DB_AddXAsset(physPreset, providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The fixed 0x2C-byte root is staged in TEMP, followed by its two XStrings
    // in LARGE.
    private static PhysPresetAsset ReadPhysPreset(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            PhysPresetAsset.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"PhysPreset pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);
        int type = rootCursor.ReadInt32();
        float mass = ReadSingle(rootCursor);
        float bounce = ReadSingle(rootCursor);
        float friction = ReadSingle(rootCursor);
        float bulletForceScale = ReadSingle(rootCursor);
        float explosiveForceScale = ReadSingle(rootCursor);
        XPointer<string> sndAliasPrefixPointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);
        float piecesSpreadFraction = ReadSingle(rootCursor);
        float piecesUpwardVelocity = ReadSingle(rootCursor);
        byte tempDefaultToCylinder = rootCursor.ReadByte();
        byte perSurfaceSndAlias = rootCursor.ReadByte();
        ushort pad2A = rootCursor.ReadUInt16();

        if (rootCursor.Offset != PhysPresetAsset.SerializedSize)
        {
            throw new InvalidDataException(
                $"PhysPreset consumed 0x{rootCursor.Offset:X} bytes instead of " +
                $"0x{PhysPresetAsset.SerializedSize:X}.");
        }

        string? name;
        string? sndAliasPrefix;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            sndAliasPrefix = context.PointerReader.LoadXString(cursor, sndAliasPrefixPointer);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new PhysPresetAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            Type = type,
            Mass = mass,
            Bounce = bounce,
            Friction = friction,
            BulletForceScale = bulletForceScale,
            ExplosiveForceScale = explosiveForceScale,
            SndAliasPrefixPointer = sndAliasPrefixPointer,
            SndAliasPrefix = sndAliasPrefix,
            PiecesSpreadFraction = piecesSpreadFraction,
            PiecesUpwardVelocity = piecesUpwardVelocity,
            TempDefaultToCylinder = tempDefaultToCylinder,
            PerSurfaceSndAlias = perSurfaceSndAlias,
            Pad2A = pad2A
        };
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        PhysPresetAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed PhysPreset pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical PhysPreset has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }
}
