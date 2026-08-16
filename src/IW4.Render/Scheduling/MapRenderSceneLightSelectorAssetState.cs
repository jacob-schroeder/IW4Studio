namespace IW4.Render.Scheduling;

using IW4.Assets.Assets.ComWorld;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Shadows;

/// <summary>
/// Immutable selector inputs reconstructed from the loaded ComWorld primary
/// lights. The base columns and shadow-map eligibility bytes are asset state;
/// the final per-frame shadow-map bits are deliberately not stored here.
/// </summary>
public sealed class MapRenderSceneLightSelectorAssetState
{
    private readonly byte[] _baseColumnByLight;
    private readonly byte[] _canUseShadowMapByLight;

    internal MapRenderSceneLightSelectorAssetState(
        ReadOnlySpan<byte> baseColumnByLight,
        ReadOnlySpan<byte> canUseShadowMapByLight)
    {
        if (baseColumnByLight.Length != canUseShadowMapByLight.Length)
        {
            throw new ArgumentException(
                "Base-column and shadow-eligibility storage must describe the same scene lights.",
                nameof(canUseShadowMapByLight));
        }

        if (baseColumnByLight.Length > MapRenderDrawMethodPageProducer.PageLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseColumnByLight),
                $"The PS3 selector page can address at most {MapRenderDrawMethodPageProducer.PageLength} scene lights.");
        }

        _baseColumnByLight = baseColumnByLight.ToArray();
        _canUseShadowMapByLight = canUseShadowMapByLight.ToArray();
        BaseColumnByLight = Array.AsReadOnly(_baseColumnByLight);
        CanUseShadowMapByLight = Array.AsReadOnly(_canUseShadowMapByLight);
    }

    /// <summary>
    /// ComPrimaryLight.Type bytes used as the unshadowed selector columns.
    /// </summary>
    public IReadOnlyList<byte> BaseColumnByLight { get; }

    /// <summary>
    /// ComPrimaryLight.CanUseShadowMap bytes retained as eligibility metadata.
    /// These bytes do not say that a light has a shadow map in the current frame.
    /// </summary>
    public IReadOnlyList<byte> CanUseShadowMapByLight { get; }

    public int SceneLightCount => _baseColumnByLight.Length;

    /// <summary>
    /// Returns whether the loaded light has a supported producer for the runtime
    /// selector's shadow-allocated (+3) column. Directional light type 1 owns
    /// the dedicated sun-shadow producer; type-2 spot lights additionally
    /// require the raw ComPrimaryLight.CanUseShadowMap eligibility byte. This
    /// is preparation metadata only and never asserts that a map exists in the
    /// current frame.
    /// </summary>
    public bool CanPrepareShadowAllocatedVariant(int lightIndex)
    {
        if ((uint)lightIndex >= (uint)SceneLightCount)
            throw new ArgumentOutOfRangeException(nameof(lightIndex));

        return _baseColumnByLight[lightIndex] ==
                (byte)GfxLightType.Directional ||
            (_baseColumnByLight[lightIndex] == (byte)GfxLightType.Spot &&
             _canUseShadowMapByLight[lightIndex] != 0);
    }

    /// <summary>
    /// Combines the asset-derived base columns with caller-supplied current-frame
    /// shadow-map bits for the PS3 normal R_RenderScene camera. That camera writes
    /// the selector-light gate as enabled.
    /// </summary>
    public MapRenderSceneLightSelectorState CreateNormalViewSelectorState(
        ReadOnlySpan<uint> runtimeShadowMapBits) =>
        new(
            _baseColumnByLight,
            SceneLightCount,
            alternateVariantGateEnabled: true,
            runtimeShadowMapBits);

    /// <summary>
    /// Creates an all-clear selector frame. It does not claim that a shadow
    /// atlas exists, and therefore cannot select a +3 column.
    /// </summary>
    public MapRenderSceneLightSelectorFrameState
        CreateUnshadowedNormalViewSelectorFrame(long revision)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        var clearBits = new uint[(SceneLightCount + 31) / 32];
        return new(
            revision,
            CreateNormalViewSelectorState(clearBits),
            sunShadowAtlasReady: null);
    }

    /// <summary>
    /// Creates a selector frame whose set allocation bits may address only
    /// type-1 directional lights owned by the dedicated sun-shadow producer.
    /// The raw ComPrimaryLight.CanUseShadowMap byte is separate asset metadata;
    /// it is not the current-frame sun allocation bit. The readiness token can
    /// only be produced after both sun-shadow partitions complete for this
    /// revision.
    /// </summary>
    public MapRenderSceneLightSelectorFrameState
        CreateSunShadowReadyNormalViewSelectorFrame(
            MapRenderSunShadowAtlasReadyState atlasReady,
            ReadOnlySpan<uint> runtimeSunShadowMapBits)
    {
        ArgumentNullException.ThrowIfNull(atlasReady);
        ValidateDirectionalSunShadowBits(runtimeSunShadowMapBits);
        return CreateShadowReadyNormalViewSelectorFrame(
            atlasReady,
            spotShadowAtlasReady: null,
            runtimeSunShadowMapBits);
    }

    /// <summary>
    /// Creates a render-ready selector frame. Every set +3 bit must have an
    /// exact same-frame owner: the directional sun atlas for a type-1 light,
    /// or a completed normal-atlas entry for an eligible type-2 spot light.
    /// </summary>
    public MapRenderSceneLightSelectorFrameState
        CreateShadowReadyNormalViewSelectorFrame(
            MapRenderSunShadowAtlasReadyState? sunShadowAtlasReady,
            MapRenderSpotShadowAtlasReadyState? spotShadowAtlasReady,
            ReadOnlySpan<uint> runtimeShadowMapBits)
    {
        if (sunShadowAtlasReady is null && spotShadowAtlasReady is null)
        {
            throw new ArgumentException(
                "A shadow-ready selector frame requires a completed sun or spot atlas.");
        }

        MapRenderWorldDpvsThreeViewFrame frame =
            sunShadowAtlasReady?.Frame ?? spotShadowAtlasReady!.Frame;
        if (sunShadowAtlasReady is not null &&
            spotShadowAtlasReady is not null &&
            !ReferenceEquals(
                sunShadowAtlasReady.Frame,
                spotShadowAtlasReady.Frame))
        {
            throw new ArgumentException(
                "Sun and spot readiness must reference the exact same three-view frame.");
        }

        ValidateReadyShadowBits(
            runtimeShadowMapBits,
            sunShadowAtlasReady,
            spotShadowAtlasReady);
        return new(
            frame.Revision,
            CreateNormalViewSelectorState(
                runtimeShadowMapBits[
                    ..((SceneLightCount + 31) / 32)]),
            sunShadowAtlasReady,
            spotShadowAtlasReady);
    }

    /// <summary>
    /// Creates an internal selector snapshot for validating the exact +3
    /// receiver closure before the atlas is written. This state must never be
    /// published as render-ready: its null readiness token is the authority
    /// boundary. A successful preflight is rebound to the completed
    /// same-revision token before receiver drawing begins.
    /// </summary>
    internal MapRenderSceneLightSelectorFrameState
        CreateSunShadowPreflightNormalViewSelectorFrame(
            long revision,
            ReadOnlySpan<uint> runtimeSunShadowMapBits)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ValidateDirectionalSunShadowBits(runtimeSunShadowMapBits);
        return new(
            revision,
            CreateNormalViewSelectorState(
                runtimeSunShadowMapBits[
                    ..((SceneLightCount + 31) / 32)]),
            sunShadowAtlasReady: null,
            spotShadowAtlasReady: null,
            isShadowAllocationPreflight: true);
    }

    /// <summary>
    /// Creates an internal planned-allocation selector used only to verify
    /// exact +3 technique closure before any atlas tile is written.
    /// </summary>
    internal MapRenderSceneLightSelectorFrameState
        CreateShadowPreflightNormalViewSelectorFrame(
            long revision,
            ReadOnlySpan<uint> runtimeShadowMapBits)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ValidatePreflightShadowBits(runtimeShadowMapBits);
        return new(
            revision,
            CreateNormalViewSelectorState(
                runtimeShadowMapBits[
                    ..((SceneLightCount + 31) / 32)]),
            sunShadowAtlasReady: null,
            spotShadowAtlasReady: null,
            isShadowAllocationPreflight: true);
    }

    private void ValidateReadyShadowBits(
        ReadOnlySpan<uint> runtimeShadowMapBits,
        MapRenderSunShadowAtlasReadyState? sunShadowAtlasReady,
        MapRenderSpotShadowAtlasReadyState? spotShadowAtlasReady)
    {
        ValidateBitStorage(runtimeShadowMapBits, nameof(runtimeShadowMapBits));
        if (spotShadowAtlasReady is not null)
        {
            foreach (MapRenderSpotShadowAtlasEntry entry in
                     spotShadowAtlasReady.Entries)
            {
                ValidateEligibleSpotEntry(entry.SceneLightIndex);
            }
        }

        for (int lightIndex = 0;
             lightIndex < SceneLightCount;
             lightIndex++)
        {
            if (!IsSet(runtimeShadowMapBits, lightIndex))
                continue;

            if (_baseColumnByLight[lightIndex] ==
                (byte)GfxLightType.Directional)
            {
                if (sunShadowAtlasReady is null)
                {
                    throw new InvalidDataException(
                        $"Directional scene light {lightIndex} has no completed sun-shadow atlas.");
                }
                continue;
            }

            ValidateEligibleSpotEntry(lightIndex);
            if (spotShadowAtlasReady?.TryGetEntry(lightIndex, out _) != true)
            {
                throw new InvalidDataException(
                    $"Spot scene light {lightIndex} has no completed same-frame atlas entry.");
            }
        }
    }

    private void ValidatePreflightShadowBits(
        ReadOnlySpan<uint> runtimeShadowMapBits)
    {
        ValidateBitStorage(runtimeShadowMapBits, nameof(runtimeShadowMapBits));
        for (int lightIndex = 0;
             lightIndex < SceneLightCount;
             lightIndex++)
        {
            if (!IsSet(runtimeShadowMapBits, lightIndex) ||
                _baseColumnByLight[lightIndex] ==
                (byte)GfxLightType.Directional)
            {
                continue;
            }

            ValidateEligibleSpotEntry(lightIndex);
        }
    }

    private void ValidateEligibleSpotEntry(int lightIndex)
    {
        if ((uint)lightIndex >= (uint)SceneLightCount)
        {
            throw new InvalidDataException(
                $"Spot-shadow entry {lightIndex} is outside the loaded scene-light table.");
        }
        if (_baseColumnByLight[lightIndex] != (byte)GfxLightType.Spot ||
            _canUseShadowMapByLight[lightIndex] == 0)
        {
            throw new InvalidDataException(
                $"Scene light {lightIndex} is not an eligible local spot-shadow producer.");
        }
    }

    private void ValidateBitStorage(
        ReadOnlySpan<uint> runtimeShadowMapBits,
        string parameterName)
    {
        int requiredWords = (SceneLightCount + 31) / 32;
        if (runtimeShadowMapBits.Length < requiredWords)
        {
            throw new ArgumentException(
                "Runtime shadow bit storage does not cover every scene light.",
                parameterName);
        }
    }

    private static bool IsSet(
        ReadOnlySpan<uint> bits,
        int lightIndex) =>
        (bits[lightIndex >> 5] & (1u << (lightIndex & 31))) != 0;

    private void ValidateDirectionalSunShadowBits(
        ReadOnlySpan<uint> runtimeSunShadowMapBits)
    {
        ValidateBitStorage(
            runtimeSunShadowMapBits,
            nameof(runtimeSunShadowMapBits));

        for (int lightIndex = 0;
             lightIndex < SceneLightCount;
             lightIndex++)
        {
            bool allocated = IsSet(runtimeSunShadowMapBits, lightIndex);
            if (allocated && _baseColumnByLight[lightIndex] !=
                    (byte)GfxLightType.Directional)
            {
                throw new InvalidDataException(
                    $"Scene light {lightIndex} cannot consume the directional-sun shadow atlas.");
            }
        }
    }
}
