namespace IW4.Render.Scheduling;

/// <summary>
/// Immutable operational frame inputs consumed by the PS3 selector-page
/// producer. These values are reconstructed engine state, not diagnostic or
/// captured source state.
/// </summary>
public sealed class MapRenderSceneLightSelectorState
{
    private readonly byte[] _variantSelectorByLight;
    private readonly uint[] _alternateVariantBits;

    public MapRenderSceneLightSelectorState(
        ReadOnlySpan<byte> variantSelectorByLight,
        int sceneLightCount,
        bool alternateVariantGateEnabled,
        ReadOnlySpan<uint> alternateVariantBits)
    {
        if ((uint)sceneLightCount > MapRenderDrawMethodPageProducer.PageLength)
            throw new ArgumentOutOfRangeException(nameof(sceneLightCount));
        if (variantSelectorByLight.Length < sceneLightCount)
        {
            throw new ArgumentException(
                "Variant-selector storage does not cover every scene light.",
                nameof(variantSelectorByLight));
        }

        int requiredBitWords = (sceneLightCount + 31) / 32;
        if (alternateVariantGateEnabled && alternateVariantBits.Length < requiredBitWords)
        {
            throw new ArgumentException(
                "Alternate-variant bit storage does not cover every scene light.",
                nameof(alternateVariantBits));
        }

        _variantSelectorByLight = variantSelectorByLight[..sceneLightCount].ToArray();
        _alternateVariantBits = alternateVariantGateEnabled
            ? alternateVariantBits[..requiredBitWords].ToArray()
            : [];
        VariantSelectorByLight = Array.AsReadOnly(_variantSelectorByLight);
        AlternateVariantBits = Array.AsReadOnly(_alternateVariantBits);
        SceneLightCount = sceneLightCount;
        AlternateVariantGateEnabled = alternateVariantGateEnabled;
    }

    public IReadOnlyList<byte> VariantSelectorByLight { get; }

    public int SceneLightCount { get; }

    public bool AlternateVariantGateEnabled { get; }

    public IReadOnlyList<uint> AlternateVariantBits { get; }

    internal ReadOnlySpan<byte> VariantSelectorSpan => _variantSelectorByLight;

    internal ReadOnlySpan<uint> AlternateVariantBitSpan => _alternateVariantBits;

    public int GetEffectiveVariant(int lightIndex)
    {
        if ((uint)lightIndex >= (uint)SceneLightCount)
            throw new ArgumentOutOfRangeException(nameof(lightIndex));

        int variant = _variantSelectorByLight[lightIndex];
        if (AlternateVariantGateEnabled &&
            (_alternateVariantBits[lightIndex >> 5] &
             (1u << (lightIndex & 31))) != 0)
        {
            variant += MapRenderDrawMethodPageProducer.AlternateVariantDelta;
        }

        if ((uint)variant >= MapRenderDrawMethodPageProducer.VariantCount)
        {
            throw new InvalidDataException(
                $"Scene light {lightIndex} selected draw-method variant {variant}; valid PS3 table columns are 0..{MapRenderDrawMethodPageProducer.VariantCount - 1}.");
        }

        return variant;
    }

    public bool IsAlternateVariantAllocated(int lightIndex)
    {
        if ((uint)lightIndex >= (uint)SceneLightCount)
            throw new ArgumentOutOfRangeException(nameof(lightIndex));

        return AlternateVariantGateEnabled &&
            (_alternateVariantBits[lightIndex >> 5] &
             (1u << (lightIndex & 31))) != 0;
    }
}
