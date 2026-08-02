using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

internal sealed record XAssetPoolState(
    Dictionary<(XAssetType Type, string Name), XAssetSlot> SlotsByIdentity,
    Dictionary<int, XAssetSlot> SlotsByRawPointer,
    int NextSlot,
    uint NextOffset,
    long NextProviderId,
    long NextRegistrationSequence,
    long Revision);
