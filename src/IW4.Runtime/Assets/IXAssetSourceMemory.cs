using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

/// <summary>
/// Live view of the materialized source-zone memory retained by a registered
/// asset. Runtime post-load and render consumers may inspect or patch asset
/// memory without gaining access to loader cursor or block-stack control.
/// </summary>
public interface IXAssetSourceMemory
{
    byte ReadByte(XBlockAddress address);

    int ReadInt32(XBlockAddress address);

    byte[] ReadBytes(XBlockAddress address, int byteCount);

    string ReadCString(XBlockAddress address);

    void WriteUInt16(XBlockAddress address, ushort value);

    void WriteUInt64(XBlockAddress address, ulong value);

    void WriteBytes(XBlockAddress address, ReadOnlySpan<byte> bytes);
}
