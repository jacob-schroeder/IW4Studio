using IW4.Loaders.Database;
using IW4.Loaders.Assets.Material;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Tracer;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.Tracer;

public sealed class TracerDefLoader : XAssetLoader<TracerDefAsset>
{
    private readonly MaterialLoader _materialLoader = new();

    // Top-level assets and nested WeaponDef.tracer references share the
    // inherited pointer path and canonicalize through type 0x27.
    public TracerDefLoader() : base(XAssetType.Tracer, TracerDefAsset.SerializedSize, "TracerDef")
    {
    }

    protected override TracerDefAsset RegisterAsset(
        TracerDefAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context) =>
        context.DB_AddXAsset(asset, providerRegistration);

    // The root is staged in TEMP; its name and MaterialPtr resolve in LARGE,
    // in that order.
    protected override TracerDefAsset ReadBody(
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
        float speed = rootCursor.ReadSingle();
        float beamLength = rootCursor.ReadSingle();
        float beamWidth = rootCursor.ReadSingle();
        float screwRadius = rootCursor.ReadSingle();
        float screwDistance = rootCursor.ReadSingle();
        var colors = new TracerColor[TracerDefAsset.ColorCount];
        for (int index = 0; index < colors.Length; index++)
        {
            colors[index] = new TracerColor(
                rootCursor.ReadSingle(),
                rootCursor.ReadSingle(),
                rootCursor.ReadSingle(),
                rootCursor.ReadSingle());
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

}
