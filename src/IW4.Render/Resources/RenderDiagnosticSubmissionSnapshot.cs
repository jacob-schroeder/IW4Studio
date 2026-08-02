using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Resources;

/// <summary>
/// Exact source category for one normal-camera diagnostic draw. Source
/// ordinals remain stable even when an empty source is not materialized.
/// </summary>
public enum RenderDiagnosticSubmissionKind : byte
{
    FallbackSolid,
    Solid,
    InstancedSolid
}

/// <summary>
/// Immutable scene-lifetime diagnostic draw and its backend-neutral resource
/// bindings. It contains no backend handles or command objects.
/// </summary>
public sealed class RenderDiagnosticSubmissionSnapshot
{
    internal RenderDiagnosticSubmissionSnapshot(
        int sourceOrdinal,
        RenderDiagnosticSubmissionKind kind,
        int? instancedBatchIndex,
        RenderSemanticIdentity drawIdentity,
        RenderSemanticIdentity geometryIdentity,
        RenderSemanticIdentity vertexLayoutIdentity,
        RenderSemanticIdentity? instancesIdentity,
        RenderSemanticIdentity? instanceLayoutIdentity)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        RenderVertexLayoutDescriptor.RequireIdentity(
            drawIdentity,
            RenderSemanticResourceKind.Draw);
        RenderVertexLayoutDescriptor.RequireIdentity(
            geometryIdentity,
            RenderSemanticResourceKind.Geometry);
        RenderVertexLayoutDescriptor.RequireIdentity(
            vertexLayoutIdentity,
            RenderSemanticResourceKind.VertexLayout);

        switch (kind)
        {
            case RenderDiagnosticSubmissionKind.FallbackSolid:
                RequireNonInstanced(
                    sourceOrdinal,
                    expectedSourceOrdinal: 0,
                    instancedBatchIndex,
                    instancesIdentity,
                    instanceLayoutIdentity);
                break;
            case RenderDiagnosticSubmissionKind.Solid:
                RequireNonInstanced(
                    sourceOrdinal,
                    expectedSourceOrdinal: 1,
                    instancedBatchIndex,
                    instancesIdentity,
                    instanceLayoutIdentity);
                break;
            case RenderDiagnosticSubmissionKind.InstancedSolid:
                if (instancedBatchIndex is not { } batchIndex ||
                    batchIndex < 0 ||
                    sourceOrdinal != checked(batchIndex + 2) ||
                    instancesIdentity is not { } instances ||
                    instanceLayoutIdentity is not { } instanceLayout)
                {
                    throw new ArgumentException(
                        "An instanced diagnostic submission requires its exact source batch, instance resource, and instance layout.");
                }
                RenderVertexLayoutDescriptor.RequireIdentity(
                    instances,
                    RenderSemanticResourceKind.Instances);
                RenderVertexLayoutDescriptor.RequireIdentity(
                    instanceLayout,
                    RenderSemanticResourceKind.InstanceLayout);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        SourceOrdinal = sourceOrdinal;
        Kind = kind;
        InstancedBatchIndex = instancedBatchIndex;
        DrawIdentity = drawIdentity;
        GeometryIdentity = geometryIdentity;
        VertexLayoutIdentity = vertexLayoutIdentity;
        InstancesIdentity = instancesIdentity;
        InstanceLayoutIdentity = instanceLayoutIdentity;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public int SourceOrdinal { get; }

    public RenderDiagnosticSubmissionKind Kind { get; }

    public int? InstancedBatchIndex { get; }

    public RenderSemanticIdentity DrawIdentity { get; }

    public RenderSemanticIdentity GeometryIdentity { get; }

    public RenderSemanticIdentity VertexLayoutIdentity { get; }

    public RenderSemanticIdentity? InstancesIdentity { get; }

    public RenderSemanticIdentity? InstanceLayoutIdentity { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-diagnostic-submission/v1");
        writer.WriteInt32(SourceOrdinal);
        writer.WriteInt32((int)Kind);
        writer.WriteNullableInt32(InstancedBatchIndex);
        writer.WriteIdentity(DrawIdentity);
        writer.WriteIdentity(GeometryIdentity);
        writer.WriteIdentity(VertexLayoutIdentity);
        writer.WriteBoolean(InstancesIdentity.HasValue);
        if (InstancesIdentity is { } instances)
            writer.WriteIdentity(instances);
        writer.WriteBoolean(InstanceLayoutIdentity.HasValue);
        if (InstanceLayoutIdentity is { } instanceLayout)
            writer.WriteIdentity(instanceLayout);
    }

    private static void RequireNonInstanced(
        int sourceOrdinal,
        int expectedSourceOrdinal,
        int? instancedBatchIndex,
        RenderSemanticIdentity? instancesIdentity,
        RenderSemanticIdentity? instanceLayoutIdentity)
    {
        if (sourceOrdinal != expectedSourceOrdinal ||
            instancedBatchIndex.HasValue ||
            instancesIdentity.HasValue ||
            instanceLayoutIdentity.HasValue)
        {
            throw new ArgumentException(
                "A non-instanced diagnostic submission has inconsistent source or instance metadata.");
        }
    }
}
