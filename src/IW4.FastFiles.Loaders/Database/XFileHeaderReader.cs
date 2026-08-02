using IW4.FastFiles.Zone;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Database;

public sealed class XFileHeaderReader
{
    public XFile Read(FastFileCursor cursor)
    {
        uint size = cursor.ReadUInt32();
        uint externalSize = cursor.ReadUInt32();
        var blockSizes = new uint[XFile.BlockCount];
        for (int index = 0; index < blockSizes.Length; index++)
            blockSizes[index] = cursor.ReadUInt32();

        return new XFile(size, externalSize, blockSizes);
    }
}
