using System.Collections.Immutable;

using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Resources;

/// <summary>
/// Semantic meaning of one per-instance input. These values describe renderer
/// intent; a backend independently chooses its attribute locations and API
/// vertex-input declarations.
/// </summary>
public enum RenderInstanceSemantic : byte
{
    TransformRow = 1,
    BaseLightingCoordinates,
    LightProbeAmbient,
    Color,
    Custom
}

/// <summary>
/// One backend-neutral element in a per-instance record.
/// </summary>
public readonly record struct RenderInstanceElementDescriptor
{
    public RenderInstanceElementDescriptor(
        RenderInstanceSemantic semantic,
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

    public RenderInstanceSemantic Semantic { get; }

    public int SemanticIndex { get; }

    public RenderVertexElementFormat Format { get; }

    public int OffsetBytes { get; }

    public int SizeInBytes => RenderVertexElementDescriptor
        .GetFormatSizeInBytes(Format);

    internal void Validate(string parameterName)
    {
        if (!Enum.IsDefined(Semantic) ||
            SemanticIndex < 0 ||
            !Enum.IsDefined(Format) ||
            OffsetBytes < 0)
        {
            throw new ArgumentException(
                "An instance element is uninitialized or invalid.",
                parameterName);
        }
    }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteInt32((int)Semantic);
        writer.WriteInt32(SemanticIndex);
        writer.WriteInt32((int)Format);
        writer.WriteInt32(OffsetBytes);
    }
}

/// <summary>
/// Reusable immutable per-instance input layout. It is pipeline state, not an
/// instance-buffer identity and not a graphics-API object.
/// </summary>
public sealed class RenderInstanceLayoutDescriptor
{
    public RenderInstanceLayoutDescriptor(
        RenderSemanticIdentity identity,
        int strideBytes,
        IEnumerable<RenderInstanceElementDescriptor> elements)
    {
        RenderVertexLayoutDescriptor.RequireIdentity(
            identity,
            RenderSemanticResourceKind.InstanceLayout);
        if (strideBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(strideBytes));

        ImmutableArray<RenderInstanceElementDescriptor> frozenElements =
            RenderSnapshotCollections.Freeze(elements, nameof(elements));
        if (frozenElements.IsEmpty)
        {
            throw new ArgumentException(
                "An instance layout requires at least one element.",
                nameof(elements));
        }

        foreach (RenderInstanceElementDescriptor element in frozenElements)
            element.Validate(nameof(elements));

        if (frozenElements
                .Select(element => (element.Semantic, element.SemanticIndex))
                .Distinct()
                .Count() != frozenElements.Length)
        {
            throw new ArgumentException(
                "Instance semantic/index pairs must be unique.",
                nameof(elements));
        }

        RenderInstanceElementDescriptor[] byOffset =
            frozenElements.OrderBy(element => element.OffsetBytes).ToArray();
        long previousEnd = 0;
        for (int index = 0; index < byOffset.Length; index++)
        {
            RenderInstanceElementDescriptor element = byOffset[index];
            long elementEnd = (long)element.OffsetBytes + element.SizeInBytes;
            if (elementEnd > strideBytes)
            {
                throw new ArgumentException(
                    "An instance element exceeds the declared stride.",
                    nameof(elements));
            }
            if (index > 0 && element.OffsetBytes < previousEnd)
            {
                throw new ArgumentException(
                    "Instance elements cannot overlap.",
                    nameof(elements));
            }

            previousEnd = elementEnd;
        }

        Identity = identity;
        StrideBytes = strideBytes;
        Elements = frozenElements;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderSemanticIdentity Identity { get; }

    public int StrideBytes { get; }

    public ImmutableArray<RenderInstanceElementDescriptor> Elements { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-instance-layout/v1");
        writer.WriteIdentity(Identity);
        writer.WriteInt32(StrideBytes);
        writer.WriteInt32(Elements.Length);
        foreach (RenderInstanceElementDescriptor element in Elements)
            element.AppendContent(writer);
    }
}
