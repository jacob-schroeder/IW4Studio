using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Loaders.Database;

internal readonly record struct StreamBlockFrame(
    XFileBlockType PreviousBlock,
    int PushedBlockPosition,
    long PreviousTempEpoch);
