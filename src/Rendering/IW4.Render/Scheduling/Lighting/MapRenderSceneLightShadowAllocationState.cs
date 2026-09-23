namespace IW4.Render.Scheduling.Lighting;

/// <summary>
/// Immutable current-frame ownership of the renderer's scene-light shadow-map
/// allocation bits. Asset eligibility is intentionally not accepted as a
/// substitute for this dynamic allocation result.
/// </summary>
public sealed class MapRenderSceneLightShadowAllocationState
{
    private readonly uint[] _allocatedBits;

    public MapRenderSceneLightShadowAllocationState(
        int sceneLightCount,
        ReadOnlySpan<uint> allocatedBits,
        string producerIdentity,
        long sourceRevision)
    {
        if ((uint)sceneLightCount >
            MapRenderDrawMethodPageProducer.PageLength)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneLightCount));
        }
        int requiredWords = checked((sceneLightCount + 31) / 32);
        if (allocatedBits.Length < requiredWords)
        {
            throw new ArgumentException(
                $"Shadow-allocation storage has {allocatedBits.Length} words, but {sceneLightCount} scene lights require {requiredWords}.",
                nameof(allocatedBits));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        if (sourceRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));

        _allocatedBits = allocatedBits[..requiredWords].ToArray();
        SceneLightCount = sceneLightCount;
        AllocatedBits = Array.AsReadOnly(_allocatedBits);
        ProducerIdentity = producerIdentity;
        SourceRevision = sourceRevision;
    }

    public int SceneLightCount { get; }

    public IReadOnlyList<uint> AllocatedBits { get; }

    public string ProducerIdentity { get; }

    public long SourceRevision { get; }

    public static MapRenderSceneLightShadowAllocationState CreateAllClear(
        int sceneLightCount,
        string producerIdentity,
        long sourceRevision) => new(
            sceneLightCount,
            new uint[checked((sceneLightCount + 31) / 32)],
            producerIdentity,
            sourceRevision);

    public bool IsShadowMapAllocated(int sceneLightIndex)
    {
        if ((uint)sceneLightIndex >= (uint)SceneLightCount)
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));

        return (_allocatedBits[sceneLightIndex >> 5] &
                (1u << (sceneLightIndex & 31))) != 0;
    }
}
