using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Loaders.Assets.Sound;

internal sealed record SoundFileRoot(
    int Offset,
    SndAliasType Type,
    byte Exists,
    ushort Padding,
    byte[] UnionBytes,
    int UnionRaw0,
    int UnionRaw1,
    int UnionRaw2,
    XBlockAddress UnionCellAddress,
    XBlockAddress StreamedDirectoryCellAddress,
    XBlockAddress StreamedFilenameCellAddress);
