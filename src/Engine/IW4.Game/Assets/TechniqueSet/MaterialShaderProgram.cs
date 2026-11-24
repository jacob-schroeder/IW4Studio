using System.Buffers.Binary;

namespace IW4.Game.Assets.TechniqueSet;

/// <summary>Views over the same big-endian PS3 program bytes at shader root +0x04.</summary>
public readonly ref struct MaterialShaderProgram
{
    private readonly ReadOnlySpan<byte> _bytes;

    public MaterialShaderProgram(ReadOnlySpan<byte> bytes, MaterialShaderKind kind)
    {
        int size = MaterialShaderAsset.GetSerializedSize(kind) - sizeof(uint);
        if (bytes.Length != size)
            throw new ArgumentException($"A {kind} shader program occupies {size} bytes.", nameof(bytes));
        _bytes = bytes;
    }

    public MaterialShaderLoadDef LoadDef => new(
        BinaryPrimitives.ReadInt32BigEndian(_bytes),
        BinaryPrimitives.ReadUInt32BigEndian(_bytes[4..]));

    // The serialized pixel definition uses the first eight bytes. Its remaining
    // twelve bytes must survive linking, including when they contain nonzero data.
    public ReadOnlySpan<byte> LoadDefTail => _bytes[MaterialShaderAsset.ShaderLoadDefSerializedSize..];

    /// <summary>Decode a pixel program after native creation (PS3 0x361F38).</summary>
    public MaterialPixelShaderRuntimeProgram ReadPixelRuntime()
    {
        if (_bytes.Length != MaterialShaderAsset.PixelShaderSerializedSize - sizeof(uint))
            throw new InvalidOperationException("A runtime pixel program requires the 20-byte pixel layout.");

        return new MaterialPixelShaderRuntimeProgram(
            BinaryPrimitives.ReadUInt32BigEndian(_bytes),
            BinaryPrimitives.ReadUInt32BigEndian(_bytes[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(_bytes[8..]),
            BinaryPrimitives.ReadUInt16BigEndian(_bytes[12..]),
            BinaryPrimitives.ReadUInt16BigEndian(_bytes[14..]),
            BinaryPrimitives.ReadUInt32BigEndian(_bytes[16..]));
    }
}

public readonly record struct MaterialShaderLoadDef(int EncodedBytecodePointer, uint ByteCount);

public readonly record struct MaterialPixelShaderRuntimeProgram(
    uint ConstantTableAddress,
    uint CommandBufferByteOffset,
    uint CommandWordCount,
    ushort ConstantTableHalfwordCount,
    ushort MicrocodeByteCountLow16,
    uint MicrocodeAddress);
