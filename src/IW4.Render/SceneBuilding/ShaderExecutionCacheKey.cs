using IW4.Render.Techniques;
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
    MaterialPassIdentity Pass,
    MaterialSamplerIdentity PrimarySampler,
    MaterialStreamSource TexCoordSource,
    RenderState RenderState,
    ShaderExecutionPurpose Purpose,
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
    private readonly IReadOnlyList<MapRenderWorldMaterialSamplerBinding>
        _bindings;
    private readonly int _hashCode;

    internal MaterialSamplerBindingsIdentity(
        IReadOnlyList<MapRenderWorldMaterialSamplerBinding> bindings)
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
        IReadOnlyList<MapRenderWorldMaterialSamplerBinding> bindings,
        int requestedRank)
    {
        for (int candidateIndex = 0;
             candidateIndex < bindings.Count;
             candidateIndex++)
        {
            MapRenderWorldMaterialSamplerBinding candidate =
                bindings[candidateIndex];
            int rank = 0;
            for (int otherIndex = 0;
                 otherIndex < bindings.Count;
                 otherIndex++)
            {
                MapRenderWorldMaterialSamplerBinding other = bindings[otherIndex];
                int order = other.Binding.Identity.SamplerArgIndex.CompareTo(
                    candidate.Binding.Identity.SamplerArgIndex);
                if (order == 0)
                {
                    order = other.Binding.Identity.SamplerDest.CompareTo(
                        candidate.Binding.Identity.SamplerDest);
                }
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
        TextureBindingKey? Texture,
        MapRenderWorldRuntimeTextureIdentity? WorldRuntimeTextureIdentity,
        UvRouteBatchKey? UvRoute)
    {
        internal static Entry Create(
            MapRenderWorldMaterialSamplerBinding binding) => new(
                binding.Binding.Identity.SamplerArgIndex,
                binding.Binding.Identity.SamplerDest,
                binding.Binding.Identity.SamplerHash,
                binding.Binding.Texture is null
                    ? null
                    : TextureBindingKey.Create(binding.Binding.Texture),
                binding.RuntimeTextureIdentity,
                binding.Binding.UvRoute is null
                    ? null
                    : UvRouteBatchKey.Create(binding.Binding.UvRoute));
    }
}
