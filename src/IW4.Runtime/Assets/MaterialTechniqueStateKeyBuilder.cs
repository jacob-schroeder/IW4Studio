using System.Buffers.Binary;
using System.Text;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Runtime.Assets;

internal static class MaterialTechniqueStateKeyBuilder
{
    private const uint Crc32Polynomial = 0xedb88320;

    public static MaterialTechniqueStateDescription Build(
        MaterialAsset material,
        MaterialPassAsset pass,
        int techniqueSlot,
        int passIndex)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(pass);

        string pixelShaderName = pass.PixelShader?.Name
            ?? throw new InvalidDataException(
                $"Material '{material.Info.Name}' technique slot {techniqueSlot} pass {passIndex} " +
                "has no materialized pixel-shader name for state-key generation.");
        MaterialShaderArgumentAsset[] stableArguments =
            MaterialStableArgumentResolver.GetStableArguments(pass, requireComplete: true);
        ushort[] codeConstants =
            MaterialStableArgumentResolver.GetCodePixelConstantIndices(stableArguments);
        ResolvedConstant[] pixelConstants = MaterialStableArgumentResolver.ResolvePixelConstants(
            material,
            stableArguments,
            requireResolved: true);

        byte[] key = BuildKey(pixelShaderName, codeConstants, pixelConstants);
        return new MaterialTechniqueStateDescription(
            techniqueSlot,
            passIndex,
            ComputeCrc32(key),
            Array.AsReadOnly(codeConstants),
            Array.AsReadOnly(pixelConstants));
    }

    internal static byte[] BuildKey(
        string pixelShaderName,
        IReadOnlyList<ushort> codeConstants,
        IReadOnlyList<ResolvedConstant> pixelConstants)
    {
        ArgumentNullException.ThrowIfNull(pixelShaderName);
        ArgumentNullException.ThrowIfNull(codeConstants);
        ArgumentNullException.ThrowIfNull(pixelConstants);
        if (codeConstants.Count > ushort.MaxValue)
            throw new InvalidDataException("Material pass has too many stable code pixel constants.");
        if (pixelConstants.Count > ushort.MaxValue)
            throw new InvalidDataException("Material pass has too many stable material/literal pixel constants.");

        byte[] shaderName = Encoding.Latin1.GetBytes(pixelShaderName);
        int byteCount = checked(
            shaderName.Length + 1 +
            sizeof(ushort) + codeConstants.Count * sizeof(ushort) +
            sizeof(ushort) + pixelConstants.Count * (sizeof(ushort) + 4 * sizeof(float)));
        byte[] key = new byte[byteCount];
        int offset = 0;

        shaderName.CopyTo(key, offset);
        offset += shaderName.Length;
        key[offset++] = 0;
        WriteUInt16(key, ref offset, checked((ushort)codeConstants.Count));
        foreach (ushort codeConstant in codeConstants)
            WriteUInt16(key, ref offset, codeConstant);

        WriteUInt16(key, ref offset, checked((ushort)pixelConstants.Count));
        foreach (ResolvedConstant constant in pixelConstants)
        {
            WriteUInt16(key, ref offset, constant.Destination);
            WriteSingle(key, ref offset, constant.Value.X);
            WriteSingle(key, ref offset, constant.Value.Y);
            WriteSingle(key, ref offset, constant.Value.Z);
            WriteSingle(key, ref offset, constant.Value.W);
        }

        if (offset != key.Length)
            throw new InvalidOperationException("Material technique-state key length drifted during serialization.");
        return key;
    }

    internal static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : Crc32Polynomial ^ (crc >> 1);
        }

        return ~crc;
    }

    private static void WriteUInt16(byte[] destination, ref int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(offset, sizeof(ushort)), value);
        offset += sizeof(ushort);
    }

    private static void WriteSingle(byte[] destination, ref int offset, float value)
    {
        BinaryPrimitives.WriteInt32BigEndian(
            destination.AsSpan(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));
        offset += sizeof(float);
    }
}
