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
    private readonly Entry[] _entries;
    private readonly int _hashCode;

    internal static MaterialSamplerBindingsIdentity Empty { get; } = new([]);

    internal static MaterialSamplerBindingsIdentity Create(
        IReadOnlyList<MapRenderWorldMaterialSamplerBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return bindings.Count == 0 ? Empty : new(bindings);
    }

    private MaterialSamplerBindingsIdentity(
        IReadOnlyList<MapRenderWorldMaterialSamplerBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        // Shader execution treats material sampler order as structural: the
        // authored argument and destination own a binding, not the order in
        // which a surface happened to discover it. Canonicalize once so hash
        // and equality remain linear. The former rank scan recomputed every
        // entry with nested loops in both operations (O(n^3) per comparison).
        if (bindings.Count == 0)
        {
            _entries = [];
            _hashCode = new HashCode().ToHashCode();
            return;
        }

        if (bindings.Count == 1)
        {
            _entries = [Entry.Create(bindings[0])];
            var singleHash = new HashCode();
            singleHash.Add(_entries[0]);
            _hashCode = singleHash.ToHashCode();
            return;
        }

        var indexedEntries = new IndexedEntry[bindings.Count];
        for (int index = 0; index < bindings.Count; index++)
        {
            indexedEntries[index] = new IndexedEntry(
                Entry.Create(bindings[index]),
                index);
        }
        Array.Sort(indexedEntries, IndexedEntryComparer.Instance);

        _entries = new Entry[indexedEntries.Length];
        for (int index = 0; index < indexedEntries.Length; index++)
            _entries[index] = indexedEntries[index].Value;

        var hash = new HashCode();
        foreach (Entry entry in _entries)
            hash.Add(entry);
        _hashCode = hash.ToHashCode();
    }

    public bool Equals(MaterialSamplerBindingsIdentity? other)
    {
        if (other is null || _entries.Length != other._entries.Length)
            return false;

        for (int index = 0; index < _entries.Length; index++)
        {
            if (_entries[index] != other._entries[index])
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is MaterialSamplerBindingsIdentity other && Equals(other);

    public override int GetHashCode() => _hashCode;

    private readonly record struct IndexedEntry(Entry Value, int SourceIndex);

    private sealed class IndexedEntryComparer : IComparer<IndexedEntry>
    {
        internal static IndexedEntryComparer Instance { get; } = new();

        public int Compare(IndexedEntry left, IndexedEntry right)
        {
            int order = left.Value.SamplerArgIndex.CompareTo(
                right.Value.SamplerArgIndex);
            if (order == 0)
            {
                order = left.Value.SamplerDest.CompareTo(
                    right.Value.SamplerDest);
            }

            // Preserve discovery order for duplicate authored destinations so
            // the canonical identity exactly matches the previous rank rules.
            return order != 0
                ? order
                : left.SourceIndex.CompareTo(right.SourceIndex);
        }
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
