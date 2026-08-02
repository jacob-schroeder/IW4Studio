using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

internal readonly record struct ShaderExecutionCacheKey(
    MaterialAsset? Material,
    MaterialTechniqueSetAsset? TechniqueSet,
    int TechniqueSlot,
    int PassIndex,
    MapRenderMaterialPass Pass,
    MapRenderState RenderState,
    MapRenderShaderExecutionPurpose Purpose,
    bool AuthoredProgramExecutable,
    MaterialSamplerBindingsIdentity SamplerBindingsIdentity,
    bool VertexInputPayloadReady,
    string VertexInputPayloadBlocker);

/// <summary>
/// Stable structural identity for the sampler subset that affects shader
/// execution. This mirrors the former formatted key without allocating names,
/// hexadecimal fields, texture identities, or UV route strings on every
/// surface.
/// </summary>
internal sealed class MaterialSamplerBindingsIdentity :
    IEquatable<MaterialSamplerBindingsIdentity>
{
    private readonly IReadOnlyList<MapRenderMaterialSamplerBinding> _bindings;
    private readonly int _hashCode;

    internal MaterialSamplerBindingsIdentity(
        IReadOnlyList<MapRenderMaterialSamplerBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings;

        var hash = new HashCode();
        for (int rank = 0; rank < _bindings.Count; rank++)
            hash.Add(EntryAtRank(_bindings, rank));
        _hashCode = hash.ToHashCode();
    }

    public bool Equals(MaterialSamplerBindingsIdentity? other)
    {
        if (other is null || _bindings.Count != other._bindings.Count)
            return false;

        for (int rank = 0; rank < _bindings.Count; rank++)
        {
            if (EntryAtRank(_bindings, rank) !=
                EntryAtRank(other._bindings, rank))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is MaterialSamplerBindingsIdentity other && Equals(other);

    public override int GetHashCode() => _hashCode;

    private static Entry EntryAtRank(
        IReadOnlyList<MapRenderMaterialSamplerBinding> bindings,
        int requestedRank)
    {
        for (int candidateIndex = 0;
             candidateIndex < bindings.Count;
             candidateIndex++)
        {
            MapRenderMaterialSamplerBinding candidate =
                bindings[candidateIndex];
            int rank = 0;
            for (int otherIndex = 0;
                 otherIndex < bindings.Count;
                 otherIndex++)
            {
                MapRenderMaterialSamplerBinding other = bindings[otherIndex];
                int order = other.SamplerArgIndex.CompareTo(
                    candidate.SamplerArgIndex);
                if (order == 0)
                    order = other.SamplerDest.CompareTo(candidate.SamplerDest);
                if (order < 0 || (order == 0 && otherIndex < candidateIndex))
                    rank++;
            }

            if (rank == requestedRank)
                return Entry.Create(candidate);
        }

        throw new ArgumentOutOfRangeException(nameof(requestedRank));
    }

    private readonly record struct Entry(
        int SamplerArgIndex,
        ushort SamplerDest,
        uint SamplerHash,
        MapRenderTextureBindingKey? Texture,
        MapRenderWorldRuntimeTextureIdentity? WorldRuntimeTextureIdentity,
        MapRenderUvRouteBatchKey? UvRoute)
    {
        internal static Entry Create(
            MapRenderMaterialSamplerBinding binding) => new(
                binding.SamplerArgIndex,
                binding.SamplerDest,
                binding.SamplerHash,
                binding.Texture is null
                    ? null
                    : MapRenderTextureBindingKey.Create(binding.Texture),
                binding.WorldRuntimeTextureIdentity,
                binding.UvRoute is null
                    ? null
                    : MapRenderUvRouteBatchKey.Create(binding.UvRoute));
    }
}
