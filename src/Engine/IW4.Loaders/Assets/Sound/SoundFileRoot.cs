using IW4.Game.Assets.Sound;
using IW4.Game.Zone;

namespace IW4.Loaders.Assets.Sound;

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
