using IW4.Game.Zone;

namespace IW4.Loaders.Database;

internal readonly record struct StreamBlockFrame(
    XFileBlockType PreviousBlock,
    int PushedBlockPosition,
    long PreviousTempEpoch);
