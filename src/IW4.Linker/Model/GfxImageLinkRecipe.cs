using System.Buffers.Binary;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen GfxImage wire recipe. Reference definitions synthesize the canonical
/// zeroed body; owned definitions currently require an inline PHYSICAL payload.
/// </summary>
internal sealed class GfxImageLinkRecipe : AssetLinkRecipe
{
    private readonly byte[] _rootBytes;
    private readonly byte[]? _payload;

    private GfxImageLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        byte[] rootBytes,
        byte[]? payload,
        bool requireReferencePlaceholder)
        : base(
            key,
            originalSerializedName,
            requireReferencePlaceholder)
    {
        _rootBytes = rootBytes;
        _payload = payload;
    }

    public static GfxImageLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        GfxImageAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return originalSerializedName.StartsWith(',')
            ? CreateReference(key, originalSerializedName)
            : CreateOwned(key, originalSerializedName, definition);
    }

    public static GfxImageLinkRecipe CreateExternal(
        AssetKey key,
        string originalSerializedName) =>
        CreateReference(key, originalSerializedName);

    public override void Emit(
        ZoneEmissionWriter output,
        Action<AssetDependency, XBlockAddress, int> emitDependency)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(emitDependency);

        output.PushTempScope();
        try
        {
            output.Allocate(
                XFileBlockType.TEMP,
                GfxImageAsset.SerializedSize,
                alignment: 4);
            output.WriteBytes(_rootBytes);
            EmitName(output);

            if (_payload is not null)
            {
                output.Allocate(
                    XFileBlockType.PHYSICAL,
                    _payload.Length,
                    alignment: 128);
                output.WriteBytes(_payload);
            }
        }
        finally
        {
            output.PopTempScope();
        }
    }

    private static GfxImageLinkRecipe CreateReference(
        AssetKey key,
        string originalSerializedName)
    {
        byte[] rootBytes = new byte[GfxImageAsset.SerializedSize];
        BinaryPrimitives.WriteInt32BigEndian(
            rootBytes.AsSpan(0x4c, sizeof(int)),
            -1);
        return new GfxImageLinkRecipe(
            key,
            originalSerializedName,
            rootBytes,
            payload: null,
            requireReferencePlaceholder: true);
    }

    private static GfxImageLinkRecipe CreateOwned(
        AssetKey key,
        string originalSerializedName,
        GfxImageAsset definition)
    {
        if (definition.TextureSemantic == 0x0b)
        {
            throw new NotSupportedException(
                "Canonical linking does not yet support source-free RUNTIME GfxImage payloads.");
        }

        GfxImageStreamData[] streamData = FreezeStreamData(definition.StreamData);
        if (streamData.Any(entry => entry.HasStreamingData))
        {
            throw new NotSupportedException(
                "Canonical linking does not yet support streamed GfxImages because their " +
                "DB-header and external imagefile contributions must be rebuilt together.");
        }

        // Pixel bytes are the authored presence signal. PayloadPointer is
        // loader state and is deliberately not retained by the link request.
        byte[] payload = definition.PayloadBytes?.ToArray()
            ?? throw new InvalidDataException("GfxImage payload bytes cannot be null.");
        int expectedByteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            definition.Format,
            definition.LevelCount,
            definition.MultiFaceControl,
            definition.TextureFlags,
            definition.Width,
            definition.Height,
            definition.Depth);
        if (expectedByteCount <= 0)
        {
            throw new NotSupportedException(
                "Canonical linking requires an owned GfxImage layout with a positive, proven payload size.");
        }
        if (payload.Length != expectedByteCount)
        {
            throw new InvalidDataException(
                $"GfxImage inline payload is {payload.Length} byte(s); " +
                $"its serialized layout requires {expectedByteCount} byte(s).");
        }

        return new GfxImageLinkRecipe(
            key,
            originalSerializedName,
            BuildOwnedRoot(definition, streamData),
            payload,
            requireReferencePlaceholder: false);
    }

    private static GfxImageStreamData[] FreezeStreamData(
        IReadOnlyList<GfxImageStreamData> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0)
        {
            return Enumerable.Range(0, GfxImageStreamData.EntryCount)
                .Select(_ => new GfxImageStreamData(0, 0, 0))
                .ToArray();
        }
        if (source.Count != GfxImageStreamData.EntryCount)
        {
            throw new InvalidDataException(
                $"GfxImage requires exactly {GfxImageStreamData.EntryCount} stream records.");
        }

        return source
            .Select(entry => entry ?? throw new InvalidDataException(
                "GfxImage stream records cannot contain null."))
            .ToArray();
    }

    private static byte[] BuildOwnedRoot(
        GfxImageAsset definition,
        IReadOnlyList<GfxImageStreamData> streamData)
    {
        byte[] root = new byte[GfxImageAsset.SerializedSize];
        int offset = 0;
        root[offset++] = definition.Format;
        root[offset++] = definition.LevelCount;
        root[offset++] = definition.DimensionCount;
        root[offset++] = definition.MultiFaceControl;
        WriteUInt32(root, ref offset, definition.TextureFlags);
        WriteUInt16(root, ref offset, definition.Width);
        WriteUInt16(root, ref offset, definition.Height);
        WriteUInt16(root, ref offset, definition.Depth);
        root[offset++] = definition.PixelDataBlock;
        root[offset++] = definition.Pad0F;
        WriteUInt32(root, ref offset, definition.RenderTargetPitch);
        WriteUInt32(root, ref offset, definition.PixelsOffset);
        root[offset++] = definition.MapType;
        root[offset++] = definition.TextureSemantic;
        root[offset++] = definition.Category;
        root[offset++] = definition.Pad1B;
        WriteUInt32(root, ref offset, definition.CardMemory);
        WriteUInt16(root, ref offset, definition.BaseWidth);
        WriteUInt16(root, ref offset, definition.BaseHeight);
        WriteUInt16(root, ref offset, definition.BaseDepth);
        root[offset++] = definition.BaseLevelCount;
        root[offset++] = definition.Cached;
        WriteInt32(root, ref offset, -1);
        foreach (GfxImageStreamData entry in streamData)
        {
            WriteUInt16(root, ref offset, entry.Width);
            WriteUInt16(root, ref offset, entry.Height);
            WriteUInt32(root, ref offset, entry.LevelSizeAndOffset);
        }
        WriteInt32(root, ref offset, -1);
        if (offset != root.Length)
            throw new InvalidOperationException("GfxImage root serialization did not produce 0x50 bytes.");
        return root;
    }

    private static void WriteInt32(byte[] destination, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(
            destination.AsSpan(offset, sizeof(int)),
            value);
        offset += sizeof(int);
    }

    private static void WriteUInt16(byte[] destination, ref int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(
            destination.AsSpan(offset, sizeof(ushort)),
            value);
        offset += sizeof(ushort);
    }

    private static void WriteUInt32(byte[] destination, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.AsSpan(offset, sizeof(uint)),
            value);
        offset += sizeof(uint);
    }
}
