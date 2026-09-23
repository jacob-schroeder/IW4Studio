using IW4.Loaders.Database;
using IW4.Game.Assets.Physics;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.Physics;

public sealed class PhysPresetLoader : XAssetLoader<PhysPresetAsset>
{
    public PhysPresetLoader() : base(XAssetType.PhysPreset, PhysPresetAsset.SerializedSize, "PhysPreset")
    {
    }

    protected override PhysPresetAsset? HandleUnresolvedReference(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (!requireAsset)
            return null;

        return base.HandleUnresolvedReference(pointer, context, requireAsset);
    }

    protected override PhysPresetAsset RegisterAsset(
        PhysPresetAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(asset, providerRegistration);
    }

    // The fixed 0x2C-byte root is staged in TEMP, followed by its two XStrings
    // in LARGE.
    protected override PhysPresetAsset ReadBody(
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
        float mass = rootCursor.ReadSingle();
        float bounce = rootCursor.ReadSingle();
        float friction = rootCursor.ReadSingle();
        float bulletForceScale = rootCursor.ReadSingle();
        float explosiveForceScale = rootCursor.ReadSingle();
        XPointer<string> sndAliasPrefixPointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);
        float piecesSpreadFraction = rootCursor.ReadSingle();
        float piecesUpwardVelocity = rootCursor.ReadSingle();
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


}
