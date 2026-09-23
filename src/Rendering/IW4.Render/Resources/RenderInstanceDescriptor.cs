using System.Collections.Immutable;

using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Resources;

/// <summary>
/// Immutable scene-lifetime instance data. The payload remains semantic input;
/// each backend independently chooses its buffer and upload representation.
/// </summary>
public sealed class RenderInstanceDescriptor
{
    public RenderInstanceDescriptor(
        RenderSemanticIdentity identity,
        RenderInstanceLayoutDescriptor layout,
        int instanceCount,
        IEnumerable<byte> payload,
        RenderPayloadByteOrder byteOrder)
    {
        RenderVertexLayoutDescriptor.RequireIdentity(
            identity,
            RenderSemanticResourceKind.Instances);
        ArgumentNullException.ThrowIfNull(layout);
        if (instanceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(instanceCount));
        if (!Enum.IsDefined(byteOrder))
            throw new ArgumentOutOfRangeException(nameof(byteOrder));

        ImmutableArray<byte> frozenPayload =
            RenderSnapshotCollections.Freeze(payload, nameof(payload));
        int expectedPayloadBytes = checked(
            layout.StrideBytes * instanceCount);
        if (frozenPayload.Length != expectedPayloadBytes)
        {
            throw new ArgumentException(
                "Instance payload length does not match count and stride.",
                nameof(payload));
        }

        Identity = identity;
        Layout = layout.Identity;
        LayoutContentDigest = layout.ContentDigest;
        StrideBytes = layout.StrideBytes;
        InstanceCount = instanceCount;
        Payload = frozenPayload;
        ByteOrder = byteOrder;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderSemanticIdentity Identity { get; }

    public RenderSemanticIdentity Layout { get; }

    public string LayoutContentDigest { get; }

    public int StrideBytes { get; }

    public int InstanceCount { get; }

    public ImmutableArray<byte> Payload { get; }

    public RenderPayloadByteOrder ByteOrder { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-instances/v2");
        writer.WriteIdentity(Identity);
        writer.WriteIdentity(Layout);
        writer.WriteString(LayoutContentDigest);
        writer.WriteInt32(StrideBytes);
        writer.WriteInt32(InstanceCount);
        writer.WriteInt32((int)ByteOrder);
        writer.WriteBytes(Payload);
    }
}
