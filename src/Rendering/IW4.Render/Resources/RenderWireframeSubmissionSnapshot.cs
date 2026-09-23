using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Resources;

/// <summary>
/// Immutable scene-lifetime collision-wireframe draw and its backend-neutral
/// resource bindings. The source scene contains one aggregate wire draw, so
/// this snapshot deliberately has no source ordinal or backend state.
/// </summary>
public sealed class RenderWireframeSubmissionSnapshot
{
    internal RenderWireframeSubmissionSnapshot(
        RenderSemanticIdentity drawIdentity,
        RenderSemanticIdentity geometryIdentity,
        RenderSemanticIdentity vertexLayoutIdentity)
    {
        RenderVertexLayoutDescriptor.RequireIdentity(
            drawIdentity,
            RenderSemanticResourceKind.Draw);
        RenderVertexLayoutDescriptor.RequireIdentity(
            geometryIdentity,
            RenderSemanticResourceKind.Geometry);
        RenderVertexLayoutDescriptor.RequireIdentity(
            vertexLayoutIdentity,
            RenderSemanticResourceKind.VertexLayout);

        DrawIdentity = drawIdentity;
        GeometryIdentity = geometryIdentity;
        VertexLayoutIdentity = vertexLayoutIdentity;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderSemanticIdentity DrawIdentity { get; }

    public RenderSemanticIdentity GeometryIdentity { get; }

    public RenderSemanticIdentity VertexLayoutIdentity { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-wireframe-submission/v1");
        writer.WriteIdentity(DrawIdentity);
        writer.WriteIdentity(GeometryIdentity);
        writer.WriteIdentity(VertexLayoutIdentity);
    }
}
