using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IW4.Render.OpenGl.StaticModels;

internal enum MapRenderOpenGlStaticGeometryLayout : byte
{
    GenericTextured = 0,
    TranslatedRsx = 1
}

internal readonly record struct MapRenderOpenGlStaticGeometryBuffers(
    uint VertexBuffer,
    uint ElementBuffer);

/// <summary>
/// Owns one immutable VBO/EBO pair for each exact static-model geometry
/// payload. Static batch VAOs and mutable instance buffers deliberately stay
/// outside this cache because their attribute and selection state is local to
/// each executable pass.
/// </summary>
internal sealed class MapRenderOpenGlStaticGeometryUploadCache
{
    private readonly Dictionary<
        ReferenceKey,
        MapRenderOpenGlStaticGeometryBuffers> _referenceAliases =
            new(ReferenceKeyComparer.Instance);
    private readonly Dictionary<
        ContentKey,
        MapRenderOpenGlStaticGeometryBuffers> _contentEntries =
            new(ContentKeyComparer.Instance);

    internal long SourceGeometryCount { get; private set; }

    internal long UniqueGeometryCount { get; private set; }

    internal long ReusedGeometryCount { get; private set; }

    internal long ImmutableBufferUploadCount =>
        checked(UniqueGeometryCount * 2);

    internal long ImmutableBufferUploadBytes { get; private set; }

    internal MapRenderOpenGlStaticGeometryBuffers GetOrAdd(
        MapRenderOpenGlStaticGeometryLayout layout,
        float[] vertices,
        uint[] indices,
        Func<MapRenderOpenGlStaticGeometryBuffers> upload,
        Action<MapRenderOpenGlStaticGeometryBuffers> releaseOnFailure,
        int vertexLayoutVariant = 0,
        long? uploadedVertexBytes = null)
    {
        if (!Enum.IsDefined(layout))
            throw new ArgumentOutOfRangeException(nameof(layout));
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(releaseOnFailure);
        if (vertices.Length == 0)
            throw new ArgumentException(
                "Static geometry requires at least one vertex value.",
                nameof(vertices));
        if (indices.Length == 0)
            throw new ArgumentException(
                "Static geometry requires at least one index.",
                nameof(indices));
        long resolvedUploadedVertexBytes =
            uploadedVertexBytes ??
            checked((long)vertices.Length * sizeof(float));
        if (resolvedUploadedVertexBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uploadedVertexBytes));
        }

        SourceGeometryCount = checked(SourceGeometryCount + 1);
        var referenceKey = new ReferenceKey(
            layout,
            vertexLayoutVariant,
            vertices,
            indices);
        if (_referenceAliases.TryGetValue(
                referenceKey,
                out MapRenderOpenGlStaticGeometryBuffers buffers))
        {
            ReusedGeometryCount = checked(ReusedGeometryCount + 1);
            return buffers;
        }

        int contentHash = ComputeContentHash(
            layout,
            vertexLayoutVariant,
            vertices,
            indices);
        var contentKey = new ContentKey(
            layout,
            vertexLayoutVariant,
            vertices,
            indices,
            contentHash);
        if (_contentEntries.TryGetValue(contentKey, out buffers))
        {
            _referenceAliases.Add(referenceKey, buffers);
            ReusedGeometryCount = checked(ReusedGeometryCount + 1);
            return buffers;
        }

        buffers = upload();
        if (buffers.VertexBuffer == 0 || buffers.ElementBuffer == 0)
        {
            if (buffers.VertexBuffer != 0 || buffers.ElementBuffer != 0)
                releaseOnFailure(buffers);
            throw new InvalidOperationException(
                "A static geometry upload must return both immutable buffer handles.");
        }

        try
        {
            _contentEntries.Add(contentKey, buffers);
            _referenceAliases.Add(referenceKey, buffers);
        }
        catch
        {
            _contentEntries.Remove(contentKey);
            _referenceAliases.Remove(referenceKey);
            releaseOnFailure(buffers);
            throw;
        }

        UniqueGeometryCount = checked(UniqueGeometryCount + 1);
        ImmutableBufferUploadBytes = checked(
            ImmutableBufferUploadBytes +
            resolvedUploadedVertexBytes +
            ((long)indices.Length * sizeof(uint)));
        return buffers;
    }

    internal void ReleaseAll(
        Action<MapRenderOpenGlStaticGeometryBuffers> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        try
        {
            foreach (MapRenderOpenGlStaticGeometryBuffers buffers in
                     _contentEntries.Values)
            {
                release(buffers);
            }
        }
        finally
        {
            _contentEntries.Clear();
            _referenceAliases.Clear();
            SourceGeometryCount = 0;
            UniqueGeometryCount = 0;
            ReusedGeometryCount = 0;
            ImmutableBufferUploadBytes = 0;
        }
    }

    private static int ComputeContentHash(
        MapRenderOpenGlStaticGeometryLayout layout,
        int vertexLayoutVariant,
        float[] vertices,
        uint[] indices)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        unchecked
        {
            hash = (hash ^ (byte)layout) * prime;
            hash = (hash ^ (uint)vertexLayoutVariant) * prime;
            hash = (hash ^ (uint)vertices.Length) * prime;
            foreach (uint value in MemoryMarshal.Cast<float, uint>(vertices))
                hash = (hash ^ value) * prime;
            hash = (hash ^ (uint)indices.Length) * prime;
            foreach (uint value in indices)
                hash = (hash ^ value) * prime;
        }
        return unchecked((int)(hash ^ (hash >> 32)));
    }

    private readonly record struct ReferenceKey(
        MapRenderOpenGlStaticGeometryLayout Layout,
        int VertexLayoutVariant,
        float[] Vertices,
        uint[] Indices);

    private sealed class ReferenceKeyComparer :
        IEqualityComparer<ReferenceKey>
    {
        internal static readonly ReferenceKeyComparer Instance = new();

        public bool Equals(ReferenceKey left, ReferenceKey right) =>
            left.Layout == right.Layout &&
            left.VertexLayoutVariant == right.VertexLayoutVariant &&
            ReferenceEquals(left.Vertices, right.Vertices) &&
            ReferenceEquals(left.Indices, right.Indices);

        public int GetHashCode(ReferenceKey key) =>
            HashCode.Combine(
                key.Layout,
                key.VertexLayoutVariant,
                RuntimeHelpers.GetHashCode(key.Vertices),
                RuntimeHelpers.GetHashCode(key.Indices));
    }

    private readonly record struct ContentKey(
        MapRenderOpenGlStaticGeometryLayout Layout,
        int VertexLayoutVariant,
        float[] Vertices,
        uint[] Indices,
        int ContentHash);

    private sealed class ContentKeyComparer :
        IEqualityComparer<ContentKey>
    {
        internal static readonly ContentKeyComparer Instance = new();

        public bool Equals(ContentKey left, ContentKey right)
        {
            if (left.Layout != right.Layout ||
                left.VertexLayoutVariant != right.VertexLayoutVariant ||
                left.Vertices.Length != right.Vertices.Length ||
                left.Indices.Length != right.Indices.Length)
            {
                return false;
            }

            return (
                    ReferenceEquals(left.Vertices, right.Vertices) ||
                    MemoryMarshal.AsBytes(left.Vertices.AsSpan())
                        .SequenceEqual(
                            MemoryMarshal.AsBytes(
                                right.Vertices.AsSpan()))) &&
                (ReferenceEquals(left.Indices, right.Indices) ||
                 left.Indices.AsSpan().SequenceEqual(right.Indices));
        }

        public int GetHashCode(ContentKey key) => key.ContentHash;
    }
}
