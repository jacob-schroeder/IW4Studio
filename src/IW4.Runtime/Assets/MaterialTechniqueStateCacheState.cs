namespace IW4.Runtime.Assets;

internal sealed record MaterialTechniqueStateCacheState(
    MaterialTechniqueStateOwner?[] OwnersByHashSlot,
    int Count);
