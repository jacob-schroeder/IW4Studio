using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.MapEnts;

public sealed class MapTriggers
{
    public const int SerializedSize = 0x18;

    // Three count/pointer pairs are serialized in model, hull, slab order.
    public uint Count { get; init; }
    public XPointer<TriggerModel[]> ModelsPointer { get; init; }
    public IReadOnlyList<TriggerModel> Models { get; init; } = [];
    public uint HullCount { get; init; }
    public XPointer<TriggerHull[]> HullsPointer { get; init; }
    public IReadOnlyList<TriggerHull> Hulls { get; init; } = [];
    public uint SlabCount { get; init; }
    public XPointer<TriggerSlab[]> SlabsPointer { get; init; }
    public IReadOnlyList<TriggerSlab> Slabs { get; init; } = [];
}
