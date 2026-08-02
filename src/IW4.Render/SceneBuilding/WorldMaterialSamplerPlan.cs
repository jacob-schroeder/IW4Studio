using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Materials;

namespace IW4.Render.SceneBuilding;

internal readonly record struct WorldMaterialSamplerPlanCacheKey(
    MaterialAsset Material,
    MaterialTechniqueSetAsset TechniqueSet,
    int TechniqueSlot,
    int PassIndex);

/// <summary>
/// Immutable material/pass work that does not depend on a GfxSurface. Texture
/// decode and UV routing remain per submission, while argument scans, source
/// pass lookup, texture-table lookup, and role classification happen once.
/// </summary>
internal sealed class WorldMaterialSamplerPlan
{
    private readonly Dictionary<uint, WorldMaterialSamplerArgumentMatch>
        _uniqueArgumentByHash = [];
    private readonly HashSet<uint> _ambiguousArgumentHashes = [];

    internal WorldMaterialSamplerPlan(
        MaterialPassAsset? sourcePass,
        MaterialVertexDeclarationAsset? vertexDeclaration,
        IReadOnlyList<WorldMaterialSamplerPlanEntry> entries,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments)
    {
        SourcePass = sourcePass;
        VertexDeclaration = vertexDeclaration;
        Entries = entries;
        for (int index = 0; index < arguments.Count; index++)
        {
            MaterialShaderArgumentAsset argument = arguments[index];
            if (argument.Type !=
                MaterialShaderArgumentType.MaterialPixelSampler)
            {
                continue;
            }

            uint hash = unchecked((uint)argument.ArgumentRaw);
            if (!_uniqueArgumentByHash.TryAdd(
                    hash,
                    new WorldMaterialSamplerArgumentMatch(argument, index)))
            {
                _ambiguousArgumentHashes.Add(hash);
            }
        }
    }

    internal static WorldMaterialSamplerPlan Empty { get; } =
        new(null, null, [], []);

    internal MaterialPassAsset? SourcePass { get; }

    internal MaterialVertexDeclarationAsset? VertexDeclaration { get; }

    internal IReadOnlyList<WorldMaterialSamplerPlanEntry> Entries { get; }

    internal bool TryGetUniqueArgument(
        uint samplerHash,
        out WorldMaterialSamplerArgumentMatch match)
    {
        if (_ambiguousArgumentHashes.Contains(samplerHash))
        {
            match = default;
            return false;
        }

        return _uniqueArgumentByHash.TryGetValue(samplerHash, out match);
    }
}

internal readonly record struct WorldMaterialSamplerArgumentMatch(
    MaterialShaderArgumentAsset Argument,
    int ArgumentIndex);

internal sealed record WorldMaterialSamplerPlanEntry(
    int ArgumentIndex,
    MaterialShaderArgumentAsset Argument,
    uint SamplerHash,
    MaterialTextureDef? MaterialTexture,
    GfxImageAsset? Image,
    MapRenderEditorMaterialTextureRole EditorTextureRole,
    int TextureTableOrdinal);
