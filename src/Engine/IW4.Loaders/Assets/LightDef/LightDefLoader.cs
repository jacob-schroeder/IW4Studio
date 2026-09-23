using IW4.Loaders.Database;
using IW4.Loaders.Assets.Image;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.LightDef;
using IW4.Game.Assets.Material;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.LightDef;

public sealed class LightDefLoader : XAssetLoader<LightDefAsset>
{
    private readonly GfxImageLoader _imageLoader = new();

    protected override bool ValidatePackedPointerRange => false;

    public LightDefLoader() : base(XAssetType.LightDef, LightDefAsset.SerializedSize, "LightDef")
    {
    }

    protected override LightDefAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, LightDefAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"LightDef pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);
        XPointer<GfxImageAsset> imagePointer = context.PointerReader.ReadPointer<GfxImageAsset>(rootCursor, XPointerResolutionMode.AliasCell);
        byte samplerState = rootCursor.ReadByte();
        byte[] pad09To0B = rootCursor.ReadBytes(3);
        uint lmapLookupStart = rootCursor.ReadUInt32();

        if (rootCursor.Offset != LightDefAsset.SerializedSize)
            throw new InvalidDataException($"LightDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{LightDefAsset.SerializedSize:X}.");

        string? name;
        GfxImageAsset? image;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            image = _imageLoader.LoadFromPointer(
                cursor,
                imagePointer.Untyped,
                context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new LightDefAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            ImagePointer = imagePointer,
            Image = image,
            SamplerState = (MaterialSamplerState)samplerState,
            Pad09To0B = pad09To0B,
            LmapLookupStart = lmapLookupStart
        };
    }

    }
