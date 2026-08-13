using System.Buffers.Binary;
using System.Collections.Immutable;

using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Textures;

namespace IW4.Render.Resources;

public enum RenderVertexSemantic
{
    Position,
    Normal,
    TextureCoordinate,
    Color,
    BlendWeight,
    BlendIndex,
    InstanceTransform,
    Custom,

    /// <summary>
    /// Exact authored RSX vertex-input destination. SemanticIndex is the
    /// backend-neutral RSX input destination (for example V0, V3, or V8), not
    /// a backend attribute location.
    /// </summary>
    RsxInput
}

public enum RenderVertexElementFormat
{
    Float32,
    Float32x2,
    Float32x3,
    Float32x4,
    UnsignedByte4,
    UnsignedByte4Normalized,
    UnsignedShort2,
    UnsignedShort4
}

public readonly record struct RenderVertexElementDescriptor
{
    public RenderVertexElementDescriptor(
        RenderVertexSemantic semantic,
        int semanticIndex,
        RenderVertexElementFormat format,
        int offsetBytes)
    {
        if (!Enum.IsDefined(semantic))
            throw new ArgumentOutOfRangeException(nameof(semantic));
        if (semanticIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(semanticIndex));
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));
        if (offsetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetBytes));

        Semantic = semantic;
        SemanticIndex = semanticIndex;
        Format = format;
        OffsetBytes = offsetBytes;
    }

    public RenderVertexSemantic Semantic { get; }

    public int SemanticIndex { get; }

    public RenderVertexElementFormat Format { get; }

    public int OffsetBytes { get; }

    public int SizeInBytes => GetFormatSizeInBytes(Format);

    internal static int GetFormatSizeInBytes(
        RenderVertexElementFormat format) => format switch
    {
        RenderVertexElementFormat.Float32 => 4,
        RenderVertexElementFormat.Float32x2 => 8,
        RenderVertexElementFormat.Float32x3 => 12,
        RenderVertexElementFormat.Float32x4 => 16,
        RenderVertexElementFormat.UnsignedByte4 => 4,
        RenderVertexElementFormat.UnsignedByte4Normalized => 4,
        RenderVertexElementFormat.UnsignedShort2 => 4,
        RenderVertexElementFormat.UnsignedShort4 => 8,
        _ => throw new InvalidOperationException(
            $"Unsupported vertex element format {format}.")
    };

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteInt32((int)Semantic);
        writer.WriteInt32(SemanticIndex);
        writer.WriteInt32((int)Format);
        writer.WriteInt32(OffsetBytes);
    }
}

public sealed class RenderVertexLayoutDescriptor
{
    public RenderVertexLayoutDescriptor(
        RenderSemanticIdentity identity,
        int strideBytes,
        IEnumerable<RenderVertexElementDescriptor> elements)
    {
        RequireIdentity(identity, RenderSemanticResourceKind.VertexLayout);
        if (strideBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(strideBytes));

        ImmutableArray<RenderVertexElementDescriptor> frozenElements =
            RenderSnapshotCollections.Freeze(elements, nameof(elements));
        if (frozenElements.IsEmpty)
            throw new ArgumentException("A vertex layout requires at least one element.", nameof(elements));
        if (frozenElements
                .Select(element => (element.Semantic, element.SemanticIndex))
                .Distinct()
                .Count() != frozenElements.Length)
        {
            throw new ArgumentException(
                "Vertex semantic/index pairs must be unique.",
                nameof(elements));
        }
        foreach (RenderVertexElementDescriptor element in frozenElements)
        {
            if (checked(element.OffsetBytes + element.SizeInBytes) > strideBytes)
            {
                throw new ArgumentException(
                    "A vertex element exceeds the declared stride.",
                    nameof(elements));
            }
        }

        Identity = identity;
        StrideBytes = strideBytes;
        Elements = frozenElements;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderSemanticIdentity Identity { get; }

    public int StrideBytes { get; }

    public ImmutableArray<RenderVertexElementDescriptor> Elements { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-vertex-layout/v1");
        writer.WriteIdentity(Identity);
        writer.WriteInt32(StrideBytes);
        writer.WriteInt32(Elements.Length);
        foreach (RenderVertexElementDescriptor element in Elements)
            element.AppendContent(writer);
    }

    internal static void RequireIdentity(
        RenderSemanticIdentity identity,
        RenderSemanticResourceKind expected)
    {
        if (identity.Kind != expected || string.IsNullOrWhiteSpace(identity.Value))
        {
            throw new ArgumentException(
                $"Expected a valid {expected} semantic identity.",
                nameof(identity));
        }
    }
}

public enum RenderPayloadByteOrder
{
    LittleEndian
}

/// <summary>
/// Coordinate basis produced by a geometry position after any instance
/// transform has been applied. Backends use this semantic fact to lower
/// PS3-native camera constants without assuming the OpenGL viewer basis.
/// </summary>
public enum RenderGeometryCoordinateSpace : byte
{
    Ps3Game,
    Render
}

public sealed class RenderGeometryDescriptor
{
    public RenderGeometryDescriptor(
        RenderSemanticIdentity identity,
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryCoordinateSpace coordinateSpace,
        RenderPrimitiveTopology topology,
        RenderIndexFormat indexFormat,
        int vertexCount,
        int indexCount,
        IEnumerable<byte> vertexPayload,
        IEnumerable<byte> indexPayload,
        RenderPayloadByteOrder byteOrder = RenderPayloadByteOrder.LittleEndian)
    {
        RenderVertexLayoutDescriptor.RequireIdentity(
            identity,
            RenderSemanticResourceKind.Geometry);
        ArgumentNullException.ThrowIfNull(vertexLayout);
        if (!Enum.IsDefined(coordinateSpace))
            throw new ArgumentOutOfRangeException(nameof(coordinateSpace));
        if (!Enum.IsDefined(topology))
            throw new ArgumentOutOfRangeException(nameof(topology));
        if (!Enum.IsDefined(indexFormat))
            throw new ArgumentOutOfRangeException(nameof(indexFormat));
        if (!Enum.IsDefined(byteOrder))
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        if (vertexCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (indexCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount));

        ImmutableArray<byte> frozenVertices =
            RenderSnapshotCollections.Freeze(vertexPayload, nameof(vertexPayload));
        ImmutableArray<byte> frozenIndices =
            RenderSnapshotCollections.Freeze(indexPayload, nameof(indexPayload));
        int expectedVertexBytes = checked(vertexCount * vertexLayout.StrideBytes);
        int indexElementSize = indexFormat == RenderIndexFormat.Unsigned16
            ? sizeof(ushort)
            : sizeof(uint);
        int expectedIndexBytes = checked(indexCount * indexElementSize);
        if (frozenVertices.Length != expectedVertexBytes)
        {
            throw new ArgumentException(
                "Vertex payload length does not match count and stride.",
                nameof(vertexPayload));
        }
        if (frozenIndices.Length != expectedIndexBytes)
        {
            throw new ArgumentException(
                "Index payload length does not match count and format.",
                nameof(indexPayload));
        }
        ValidateTopologyCount(topology, indexCount);
        ValidateIndexBounds(
            frozenIndices.AsSpan(),
            indexFormat,
            byteOrder,
            vertexCount);

        Identity = identity;
        VertexLayout = vertexLayout.Identity;
        VertexLayoutContentDigest = vertexLayout.ContentDigest;
        VertexStrideBytes = vertexLayout.StrideBytes;
        CoordinateSpace = coordinateSpace;
        Topology = topology;
        IndexFormat = indexFormat;
        ByteOrder = byteOrder;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        VertexPayload = frozenVertices;
        IndexPayload = frozenIndices;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderSemanticIdentity Identity { get; }

    public RenderSemanticIdentity VertexLayout { get; }

    public string VertexLayoutContentDigest { get; }

    public int VertexStrideBytes { get; }

    public RenderGeometryCoordinateSpace CoordinateSpace { get; }

    public RenderPrimitiveTopology Topology { get; }

    public RenderIndexFormat IndexFormat { get; }

    public RenderPayloadByteOrder ByteOrder { get; }

    public int VertexCount { get; }

    public int IndexCount { get; }

    public ImmutableArray<byte> VertexPayload { get; }

    public ImmutableArray<byte> IndexPayload { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-geometry/v2");
        writer.WriteIdentity(Identity);
        writer.WriteIdentity(VertexLayout);
        writer.WriteString(VertexLayoutContentDigest);
        writer.WriteInt32(VertexStrideBytes);
        writer.WriteInt32((int)CoordinateSpace);
        writer.WriteInt32((int)Topology);
        writer.WriteInt32((int)IndexFormat);
        writer.WriteInt32((int)ByteOrder);
        writer.WriteInt32(VertexCount);
        writer.WriteInt32(IndexCount);
        writer.WriteBytes(VertexPayload);
        writer.WriteBytes(IndexPayload);
    }

    private static void ValidateTopologyCount(
        RenderPrimitiveTopology topology,
        int indexCount)
    {
        bool valid = topology switch
        {
            RenderPrimitiveTopology.TriangleList => indexCount % 3 == 0,
            RenderPrimitiveTopology.LineList => indexCount % 2 == 0,
            RenderPrimitiveTopology.TriangleStrip => indexCount >= 3,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException(
                "Index count is incompatible with the primitive topology.",
                nameof(indexCount));
        }
    }

    private static void ValidateIndexBounds(
        ReadOnlySpan<byte> payload,
        RenderIndexFormat format,
        RenderPayloadByteOrder byteOrder,
        int vertexCount)
    {
        if (byteOrder != RenderPayloadByteOrder.LittleEndian)
            throw new ArgumentOutOfRangeException(nameof(byteOrder));
        int stride = format == RenderIndexFormat.Unsigned16
            ? sizeof(ushort)
            : sizeof(uint);
        for (int offset = 0; offset < payload.Length; offset += stride)
        {
            uint value = format == RenderIndexFormat.Unsigned16
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..])
                : BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
            if (value >= vertexCount)
            {
                throw new ArgumentException(
                    $"Index {value} exceeds vertex count {vertexCount}.",
                    nameof(payload));
            }
        }
    }
}

public enum RenderTexturePayloadKind
{
    /// <summary>
    /// Exact authored bytes retained for diagnostics and a possible
    /// backend-native upload. This value alone does not prove that the bytes
    /// have a backend-compatible block order, endian representation, or row
    /// layout; a backend must require separate format/layout capability support
    /// before uploading them directly.
    /// </summary>
    Authored,

    /// <summary>
    /// Canonical tightly packed RGBA8 rows produced by the shared decoder.
    /// </summary>
    DecodedRgba8,

    /// <summary>
    /// Canonical tightly packed host-endian RG16F rows produced by the shared
    /// decoder from PS3 CELL_GCM_TEXTURE_Y16_X16_FLOAT storage.
    /// </summary>
    DecodedRg16Float
}

public sealed class RenderTexturePayloadDescriptor
{
    /// <summary>
    /// Retains one exact payload representation and its logical pitches.
    /// Pitches describe the captured representation; they are not permission
    /// to reinterpret an authored payload as a native API format.
    /// </summary>
    public RenderTexturePayloadDescriptor(
        RenderTexturePayloadKind kind,
        string format,
        int rowPitchBytes,
        int slicePitchBytes,
        IEnumerable<byte> payload,
        bool isDirectUploadLayoutProven)
        : this(
            kind,
            format,
            rowPitchBytes,
            slicePitchBytes,
            depthSliceCount: 1,
            payload,
            isDirectUploadLayoutProven)
    {
    }

    /// <summary>
    /// Retains one exact payload representation for a complete subresource.
    /// <paramref name="slicePitchBytes"/> is the byte distance between
    /// adjacent two-dimensional depth slices; it is not the total byte count
    /// when <paramref name="depthSliceCount"/> is greater than one.
    /// </summary>
    public RenderTexturePayloadDescriptor(
        RenderTexturePayloadKind kind,
        string format,
        int rowPitchBytes,
        int slicePitchBytes,
        int depthSliceCount,
        IEnumerable<byte> payload,
        bool isDirectUploadLayoutProven)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        if (rowPitchBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowPitchBytes));
        if (slicePitchBytes < rowPitchBytes)
            throw new ArgumentOutOfRangeException(nameof(slicePitchBytes));
        if (depthSliceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(depthSliceCount));
        ImmutableArray<byte> frozenPayload =
            RenderSnapshotCollections.Freeze(payload, nameof(payload));
        int expectedPayloadBytes = checked(slicePitchBytes * depthSliceCount);
        if (frozenPayload.Length != expectedPayloadBytes)
        {
            throw new ArgumentException(
                "Texture payload length must equal its two-dimensional " +
                "slice pitch multiplied by its depth-slice count.",
                nameof(payload));
        }

        Kind = kind;
        Format = format;
        RowPitchBytes = rowPitchBytes;
        SlicePitchBytes = slicePitchBytes;
        DepthSliceCount = depthSliceCount;
        Payload = frozenPayload;
        IsDirectUploadLayoutProven = isDirectUploadLayoutProven;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderTexturePayloadKind Kind { get; }

    public string Format { get; }

    public int RowPitchBytes { get; }

    public int SlicePitchBytes { get; }

    /// <summary>
    /// Number of two-dimensional depth slices retained in
    /// <see cref="Payload"/>. This is one for 2D and cube subresources.
    /// </summary>
    public int DepthSliceCount { get; }

    public int TotalPayloadBytes => Payload.Length;

    public ImmutableArray<byte> Payload { get; }

    /// <summary>
    /// True only when the payload's row/block order and endian representation
    /// are API-neutral upload input. A backend must still query support
    /// for <see cref="Format"/>; this flag is never a format-capability claim.
    /// </summary>
    public bool IsDirectUploadLayoutProven { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-texture-payload/v3");
        writer.WriteInt32((int)Kind);
        writer.WriteString(Format);
        writer.WriteInt32(RowPitchBytes);
        writer.WriteInt32(SlicePitchBytes);
        writer.WriteInt32(DepthSliceCount);
        writer.WriteBytes(Payload);
        writer.WriteBoolean(IsDirectUploadLayoutProven);
    }
}

public sealed class RenderTextureSubresourceDescriptor
{
    public RenderTextureSubresourceDescriptor(
        int mipLevel,
        int arrayLayer,
        int width,
        int height,
        IEnumerable<RenderTexturePayloadDescriptor> payloads)
        : this(
            mipLevel,
            arrayLayer,
            width,
            height,
            depth: 1,
            payloads)
    {
    }

    public RenderTextureSubresourceDescriptor(
        int mipLevel,
        int arrayLayer,
        int width,
        int height,
        int depth,
        IEnumerable<RenderTexturePayloadDescriptor> payloads)
    {
        if (mipLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        if (arrayLayer < 0)
            throw new ArgumentOutOfRangeException(nameof(arrayLayer));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth));
        ImmutableArray<RenderTexturePayloadDescriptor> frozenPayloads =
            RenderSnapshotCollections.Freeze(payloads, nameof(payloads));
        if (frozenPayloads.IsDefaultOrEmpty ||
            frozenPayloads.Any(payload => payload is null))
        {
            throw new ArgumentException(
                "A texture subresource requires at least one payload.",
                nameof(payloads));
        }
        if (frozenPayloads.Select(payload => payload.Kind).Distinct().Count() !=
            frozenPayloads.Length)
        {
            throw new ArgumentException(
                "Texture payload kinds must be unique within a subresource.",
                nameof(payloads));
        }
        foreach (RenderTexturePayloadDescriptor payload in frozenPayloads)
        {
            if (payload.DepthSliceCount != depth)
            {
                throw new ArgumentException(
                    "Texture payload depth must match its subresource depth.",
                    nameof(payloads));
            }
            if (payload.Kind is not
                (RenderTexturePayloadKind.DecodedRgba8 or
                 RenderTexturePayloadKind.DecodedRg16Float))
                continue;
            if (!payload.IsDirectUploadLayoutProven)
            {
                throw new ArgumentException(
                    "Canonical decoded pixel payloads must carry tightly packed layout metadata.",
                    nameof(payloads));
            }
            int expectedRowPitch = checked(width * 4);
            int expectedSlicePitch = checked(expectedRowPitch * height);
            if (payload.RowPitchBytes != expectedRowPitch ||
                payload.SlicePitchBytes != expectedSlicePitch)
            {
                throw new ArgumentException(
                    "Decoded pixel payload pitch does not match its dimensions.",
                    nameof(payloads));
            }
        }

        MipLevel = mipLevel;
        ArrayLayer = arrayLayer;
        Width = width;
        Height = height;
        Depth = depth;
        Payloads = frozenPayloads;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public int MipLevel { get; }

    public int ArrayLayer { get; }

    public int Width { get; }

    public int Height { get; }

    public int Depth { get; }

    public ImmutableArray<RenderTexturePayloadDescriptor> Payloads { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-texture-subresource/v2");
        writer.WriteInt32(MipLevel);
        writer.WriteInt32(ArrayLayer);
        writer.WriteInt32(Width);
        writer.WriteInt32(Height);
        writer.WriteInt32(Depth);
        writer.WriteInt32(Payloads.Length);
        foreach (RenderTexturePayloadDescriptor payload in Payloads)
            payload.AppendContent(writer);
    }
}

public sealed class RenderTextureSourceDescriptor
{
    public RenderTextureSourceDescriptor(RsxTextureCommandState source)
    {
        ArgumentNullException.ThrowIfNull(source);

        TexOffsetPayload = source.TexOffsetPayload;
        TexFormatPayload = source.TexFormatPayload;
        TexNpotSizePayload = source.TexNpotSizePayload;
        TexSize1Payload = source.TexSize1Payload;
        TexSwizzlePayload = source.TexSwizzlePayload;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public uint TexOffsetPayload { get; }

    public uint TexFormatPayload { get; }

    public uint TexNpotSizePayload { get; }

    public uint TexSize1Payload { get; }

    public uint TexSwizzlePayload { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-texture-source/v1");
        writer.WriteUInt32(TexOffsetPayload);
        writer.WriteUInt32(TexFormatPayload);
        writer.WriteUInt32(TexNpotSizePayload);
        writer.WriteUInt32(TexSize1Payload);
        writer.WriteUInt32(TexSwizzlePayload);
    }
}

public sealed class RenderTextureDescriptor
{
    /// <summary>
    /// Compatibility constructor for the original 2D/cube contract. A 2D
    /// texture still requires one layer and a cube still requires six flat
    /// face layers. Use the depth/face-count overload for 2D arrays, cube
    /// arrays, or 3D textures.
    /// </summary>
    public RenderTextureDescriptor(
        RenderSemanticIdentity identity,
        string name,
        string authoredFormat,
        RenderTextureDimension dimension,
        int width,
        int height,
        int mipCount,
        int arrayLayerCount,
        bool hasTransparency,
        RenderTextureSourceDescriptor source,
        IEnumerable<RenderTextureSubresourceDescriptor> subresources)
        : this(
            identity,
            name,
            authoredFormat,
            dimension,
            width,
            height,
            depth: 1,
            mipCount,
            arrayLayerCount,
            faceCount: dimension == RenderTextureDimension.TextureCube ? 6 : 1,
            hasTransparency,
            source,
            subresources,
            preserveLegacyLayerRules: true)
    {
    }

    /// <summary>
    /// Describes a backend-neutral texture with explicit depth and face
    /// topology. <paramref name="arrayLayerCount"/> is the flat subresource
    /// layer count. For cube arrays it is a multiple of six and
    /// <paramref name="faceCount"/> is six. <see cref="LayerCount"/> exposes
    /// the corresponding logical array/cube count.
    /// </summary>
    public RenderTextureDescriptor(
        RenderSemanticIdentity identity,
        string name,
        string authoredFormat,
        RenderTextureDimension dimension,
        int width,
        int height,
        int depth,
        int mipCount,
        int arrayLayerCount,
        int faceCount,
        bool hasTransparency,
        RenderTextureSourceDescriptor source,
        IEnumerable<RenderTextureSubresourceDescriptor> subresources)
        : this(
            identity,
            name,
            authoredFormat,
            dimension,
            width,
            height,
            depth,
            mipCount,
            arrayLayerCount,
            faceCount,
            hasTransparency,
            source,
            subresources,
            preserveLegacyLayerRules: false)
    {
    }

    private RenderTextureDescriptor(
        RenderSemanticIdentity identity,
        string name,
        string authoredFormat,
        RenderTextureDimension dimension,
        int width,
        int height,
        int depth,
        int mipCount,
        int arrayLayerCount,
        int faceCount,
        bool hasTransparency,
        RenderTextureSourceDescriptor source,
        IEnumerable<RenderTextureSubresourceDescriptor> subresources,
        bool preserveLegacyLayerRules)
    {
        RenderVertexLayoutDescriptor.RequireIdentity(
            identity,
            RenderSemanticResourceKind.Texture);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredFormat);
        if (!Enum.IsDefined(dimension))
            throw new ArgumentOutOfRangeException(nameof(dimension));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth));
        if (mipCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(mipCount));
        if (arrayLayerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(arrayLayerCount));
        if (faceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(faceCount));
        if (preserveLegacyLayerRules &&
            ((dimension == RenderTextureDimension.Texture2D &&
              arrayLayerCount != 1) ||
             (dimension == RenderTextureDimension.TextureCube &&
              arrayLayerCount != 6)))
        {
            throw new ArgumentException(
                "Texture dimension and array-layer count are incompatible.",
                nameof(arrayLayerCount));
        }
        switch (dimension)
        {
            case RenderTextureDimension.Texture2D:
                if (depth != 1 || faceCount != 1)
                {
                    throw new ArgumentException(
                        "2D textures require depth one and one face.",
                        nameof(dimension));
                }
                break;
            case RenderTextureDimension.TextureCube:
                if (depth != 1 || faceCount != 6 ||
                    arrayLayerCount % faceCount != 0)
                {
                    throw new ArgumentException(
                        "Cube textures require depth one and complete " +
                        "six-face layer groups.",
                        nameof(dimension));
                }
                break;
            case RenderTextureDimension.Texture3D:
                if (faceCount != 1 || arrayLayerCount != 1)
                {
                    throw new ArgumentException(
                        "3D textures require one face and one array layer.",
                        nameof(dimension));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dimension));
        }
        if (dimension == RenderTextureDimension.TextureCube && width != height)
        {
            throw new ArgumentException(
                "Cube textures require square faces.",
                nameof(height));
        }
        int maximumMipCount = MaximumMipCount(width, height, depth);
        if (mipCount > maximumMipCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mipCount),
                mipCount,
                $"Texture dimensions {width}x{height}x{depth} support at most " +
                $"{maximumMipCount} mip levels.");
        }
        ArgumentNullException.ThrowIfNull(source);

        ImmutableArray<RenderTextureSubresourceDescriptor> frozenSubresources =
            RenderSnapshotCollections.Freeze(subresources, nameof(subresources));
        if (frozenSubresources.Any(subresource => subresource is null) ||
            frozenSubresources.Length != checked(mipCount * arrayLayerCount))
        {
            throw new ArgumentException(
                "Texture subresources must cover every layer and mip exactly once.",
                nameof(subresources));
        }
        int ordinal = 0;
        for (int layer = 0; layer < arrayLayerCount; layer++)
        {
            int expectedWidth = width;
            int expectedHeight = height;
            int expectedDepth = depth;
            for (int mip = 0; mip < mipCount; mip++, ordinal++)
            {
                RenderTextureSubresourceDescriptor subresource =
                    frozenSubresources[ordinal];
                if (subresource.ArrayLayer != layer ||
                    subresource.MipLevel != mip ||
                    subresource.Width != expectedWidth ||
                    subresource.Height != expectedHeight ||
                    subresource.Depth != expectedDepth)
                {
                    throw new ArgumentException(
                        "Texture subresources must be layer-major and match the canonical mip dimensions.",
                        nameof(subresources));
                }
                expectedWidth = Math.Max(1, expectedWidth / 2);
                expectedHeight = Math.Max(1, expectedHeight / 2);
                if (dimension == RenderTextureDimension.Texture3D)
                    expectedDepth = Math.Max(1, expectedDepth / 2);
            }
        }

        Identity = identity;
        Name = name;
        AuthoredFormat = authoredFormat;
        Dimension = dimension;
        Width = width;
        Height = height;
        Depth = depth;
        MipCount = mipCount;
        ArrayLayerCount = arrayLayerCount;
        FaceCount = faceCount;
        HasTransparency = hasTransparency;
        Source = source;
        Subresources = frozenSubresources;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderSemanticIdentity Identity { get; }

    public string Name { get; }

    public string AuthoredFormat { get; }

    public RenderTextureDimension Dimension { get; }

    public int Width { get; }

    public int Height { get; }

    public int Depth { get; }

    public int MipCount { get; }

    public int ArrayLayerCount { get; }

    /// <summary>
    /// Number of faces in one logical layer: six for cube textures and one
    /// for 2D/3D textures.
    /// </summary>
    public int FaceCount { get; }

    /// <summary>
    /// Number of logical array entries or cubes. The flat subresource layer
    /// count remains available through <see cref="ArrayLayerCount"/> for
    /// compatibility with existing frame plans.
    /// </summary>
    public int LayerCount => ArrayLayerCount / FaceCount;

    public bool HasTransparency { get; }

    public RenderTextureSourceDescriptor Source { get; }

    public ImmutableArray<RenderTextureSubresourceDescriptor> Subresources
        { get; }

    public string ContentDigest { get; }

    public RenderTextureSubresourceDescriptor RequireSubresource(
        int mipLevel,
        int arrayLayer)
    {
        if ((uint)mipLevel >= (uint)MipCount)
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        if ((uint)arrayLayer >= (uint)ArrayLayerCount)
            throw new ArgumentOutOfRangeException(nameof(arrayLayer));
        return Subresources[checked(arrayLayer * MipCount + mipLevel)];
    }

    public RenderTextureSubresourceDescriptor RequireSubresource(
        int mipLevel,
        int layer,
        int face)
    {
        if ((uint)layer >= (uint)LayerCount)
            throw new ArgumentOutOfRangeException(nameof(layer));
        if ((uint)face >= (uint)FaceCount)
            throw new ArgumentOutOfRangeException(nameof(face));
        return RequireSubresource(
            mipLevel,
            checked(layer * FaceCount + face));
    }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-texture/v2");
        writer.WriteIdentity(Identity);
        writer.WriteString(Name);
        writer.WriteString(AuthoredFormat);
        writer.WriteInt32((int)Dimension);
        writer.WriteInt32(Width);
        writer.WriteInt32(Height);
        writer.WriteInt32(Depth);
        writer.WriteInt32(MipCount);
        writer.WriteInt32(ArrayLayerCount);
        writer.WriteInt32(FaceCount);
        writer.WriteBoolean(HasTransparency);
        Source.AppendContent(writer);
        writer.WriteInt32(Subresources.Length);
        foreach (RenderTextureSubresourceDescriptor subresource in Subresources)
            subresource.AppendContent(writer);
    }

    private static int MaximumMipCount(int width, int height, int depth)
    {
        int extent = Math.Max(Math.Max(width, height), depth);
        int count = 1;
        while (extent > 1)
        {
            extent /= 2;
            count++;
        }
        return count;
    }
}

public sealed class RenderSamplerDescriptor
{
    internal RenderSamplerDescriptor(
        RenderSemanticIdentity identity,
        RsxSamplerState source)
        : this(
            identity,
            source?.RawState ?? throw new ArgumentNullException(nameof(source)),
            source.RsxClampMax,
            source.MinLodControl,
            source.UseSrgbReads,
            source.RsxSamplerCachePayload,
            source.RsxTexEnablePayload,
            source.RsxTexFilterPayload,
            source.RsxTexWrapPayload,
            source.TableIndex,
            source.FilterClass,
            source.MipClass,
            source.MinFilter,
            source.MagFilter,
            source.MipFilter,
            source.MaxAnisotropy,
            source.MipLodBias,
            source.AddressU,
            source.AddressV,
            source.AddressW)
    {
    }

    public RenderSamplerDescriptor(
        RenderSemanticIdentity identity,
        byte rawState,
        int rsxClampMax,
        byte minLodControl,
        byte useSrgbReads,
        uint samplerCachePayload,
        uint rsxTexEnablePayload,
        uint rsxTexFilterPayload,
        uint rsxTexWrapPayload,
        int tableIndex,
        int filterClass,
        int mipClass,
        TextureFilter minFilter,
        TextureFilter magFilter,
        TextureFilter mipFilter,
        int maxAnisotropy,
        float mipLodBias,
        TextureAddressMode addressU,
        TextureAddressMode addressV,
        TextureAddressMode addressW)
    {
        RenderVertexLayoutDescriptor.RequireIdentity(
            identity,
            RenderSemanticResourceKind.Sampler);
        if (!Enum.IsDefined(minFilter) ||
            !Enum.IsDefined(magFilter) ||
            !Enum.IsDefined(mipFilter) ||
            !Enum.IsDefined(addressU) ||
            !Enum.IsDefined(addressV) ||
            !Enum.IsDefined(addressW))
        {
            throw new ArgumentException(
                "Sampler contains an undefined semantic value.");
        }
        if (maxAnisotropy <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAnisotropy));
        if (!float.IsFinite(mipLodBias))
            throw new ArgumentOutOfRangeException(nameof(mipLodBias));

        Identity = identity;
        RawState = rawState;
        RsxClampMax = rsxClampMax;
        MinLodControl = minLodControl;
        UseSrgbReads = useSrgbReads;
        RsxSamplerCachePayload = samplerCachePayload;
        RsxTexEnablePayload = rsxTexEnablePayload;
        RsxTexFilterPayload = rsxTexFilterPayload;
        RsxTexWrapPayload = rsxTexWrapPayload;
        TableIndex = tableIndex;
        FilterClass = filterClass;
        MipClass = mipClass;
        MinFilter = minFilter;
        MagFilter = magFilter;
        MipFilter = mipFilter;
        MaxAnisotropy = maxAnisotropy;
        MipLodBias = mipLodBias;
        AddressU = addressU;
        AddressV = addressV;
        AddressW = addressW;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderSemanticIdentity Identity { get; }
    public byte RawState { get; }
    public int RsxClampMax { get; }
    public byte MinLodControl { get; }
    public byte UseSrgbReads { get; }
    public uint RsxSamplerCachePayload { get; }
    public uint RsxTexEnablePayload { get; }
    public uint RsxTexFilterPayload { get; }
    public uint RsxTexWrapPayload { get; }
    public int TableIndex { get; }
    public int FilterClass { get; }
    public int MipClass { get; }
    public TextureFilter MinFilter { get; }
    public TextureFilter MagFilter { get; }
    public TextureFilter MipFilter { get; }
    public int MaxAnisotropy { get; }
    public float MipLodBias { get; }
    public TextureAddressMode AddressU { get; }
    public TextureAddressMode AddressV { get; }
    public TextureAddressMode AddressW { get; }
    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-sampler/v1");
        writer.WriteIdentity(Identity);
        writer.WriteByte(RawState);
        writer.WriteInt32(RsxClampMax);
        writer.WriteByte(MinLodControl);
        writer.WriteByte(UseSrgbReads);
        writer.WriteUInt32(RsxSamplerCachePayload);
        writer.WriteUInt32(RsxTexEnablePayload);
        writer.WriteUInt32(RsxTexFilterPayload);
        writer.WriteUInt32(RsxTexWrapPayload);
        writer.WriteInt32(TableIndex);
        writer.WriteInt32(FilterClass);
        writer.WriteInt32(MipClass);
        writer.WriteInt32((int)MinFilter);
        writer.WriteInt32((int)MagFilter);
        writer.WriteInt32((int)MipFilter);
        writer.WriteInt32(MaxAnisotropy);
        writer.WriteSingle(MipLodBias);
        writer.WriteInt32((int)AddressU);
        writer.WriteInt32((int)AddressV);
        writer.WriteInt32((int)AddressW);
    }
}
