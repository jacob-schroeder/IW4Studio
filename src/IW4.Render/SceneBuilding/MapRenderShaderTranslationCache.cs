using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Shaders;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Scene-local single-flight cache for immutable RSX program translation.
/// Runtime texture identities affect the surrounding execution contract, but
/// not program bytes, pass arguments, or the sampler-shape destination sets.
/// </summary>
internal sealed class MapRenderShaderTranslationCache
{
    private readonly object _gate = new();
    private readonly Dictionary<
        RsxShaderTranslationRequestKey,
        RsxShaderTranslationResult>
        _translations = [];

    internal MapRenderShaderTranslationCache()
    {
    }

    internal RsxShaderTranslationResult Resolve(
        MaterialAsset? material,
        MaterialPassAsset sourcePass,
        MapRenderSelectedPassProgramSources programSources,
        IReadOnlySet<int> cubeSamplerDestinations,
        IReadOnlySet<int> shadowSamplerDestinations,
        IReadOnlySet<int>? volumeSamplerDestinations = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePass);
        ArgumentNullException.ThrowIfNull(programSources);
        ArgumentNullException.ThrowIfNull(cubeSamplerDestinations);
        ArgumentNullException.ThrowIfNull(shadowSamplerDestinations);
        var request = RsxShaderTranslationRequestSnapshot.Capture(
            material,
            sourcePass,
            programSources,
            cubeSamplerDestinations,
            shadowSamplerDestinations,
            volumeSamplerDestinations);
        RsxShaderTranslationRequestKey key = request.CacheKey;
        lock (_gate)
        {
            if (_translations.TryGetValue(
                    key,
                    out RsxShaderTranslationResult? cached))
            {
                return cached;
            }

            RsxShaderTranslationResult result =
                RsxShaderTranslator.Translate(request);
            _translations.Add(key, result);
            return result;
        }
    }
}
