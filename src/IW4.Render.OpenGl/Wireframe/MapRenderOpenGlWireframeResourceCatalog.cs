using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl.Wireframe;

/// <summary>
/// Exact collision-wireframe snapshot objects paired with the OpenGL mesh
/// created from those same immutable payloads. OpenGL names never enter the
/// shared frame plan.
/// </summary>
internal sealed class MapRenderOpenGlWireframeResourceBinding
{
    internal MapRenderOpenGlWireframeResourceBinding(
        RenderWireframeSubmissionSnapshot submission,
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryDescriptor geometry,
        GlMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(vertexLayout);
        ArgumentNullException.ThrowIfNull(geometry);
        if (submission.VertexLayoutIdentity != vertexLayout.Identity ||
            submission.GeometryIdentity != geometry.Identity ||
            geometry.VertexLayout != vertexLayout.Identity)
        {
            throw new ArgumentException(
                "The OpenGL wireframe binding must retain the exact semantic geometry and vertex-layout identities.",
                nameof(submission));
        }
        if (!IsExactVertexLayout(vertexLayout) ||
            geometry.CoordinateSpace !=
                RenderGeometryCoordinateSpace.Render ||
            geometry.Topology != RenderPrimitiveTopology.LineList ||
            geometry.IndexFormat != RenderIndexFormat.Unsigned32 ||
            geometry.ByteOrder != RenderPayloadByteOrder.LittleEndian)
        {
            throw new ArgumentException(
                "The OpenGL wireframe binding requires the exact render-space Position3/Color3 stride-24 LineList/U32 resource shape.",
                nameof(submission));
        }
        if (mesh.VertexArray == 0 ||
            mesh.VertexBuffer == 0 ||
            mesh.ElementBuffer == 0 ||
            mesh.IndexCount != checked((uint)geometry.IndexCount))
        {
            throw new ArgumentException(
                "The OpenGL wireframe mesh must completely realize the semantic geometry with live VAO/VBO/EBO names.",
                nameof(mesh));
        }

        Submission = submission;
        VertexLayout = vertexLayout;
        Geometry = geometry;
        Mesh = mesh;
    }

    public RenderWireframeSubmissionSnapshot Submission { get; }

    public RenderVertexLayoutDescriptor VertexLayout { get; }

    public RenderGeometryDescriptor Geometry { get; }

    public GlMesh Mesh { get; }

    private static bool IsExactVertexLayout(
        RenderVertexLayoutDescriptor layout) =>
        layout.StrideBytes == 6 * sizeof(float) &&
        layout.Elements.SequenceEqual(
        [
            new RenderVertexElementDescriptor(
                RenderVertexSemantic.Position,
                semanticIndex: 0,
                RenderVertexElementFormat.Float32x3,
                offsetBytes: 0),
            new RenderVertexElementDescriptor(
                RenderVertexSemantic.Color,
                semanticIndex: 0,
                RenderVertexElementFormat.Float32x3,
                offsetBytes: 3 * sizeof(float))
        ]);
}

/// <summary>
/// Scene-lifetime lookup for the single aggregate collision-wireframe draw.
/// Construction performs no native allocation or upload.
/// </summary>
internal sealed class MapRenderOpenGlWireframeResourceCatalog
{
    private MapRenderOpenGlWireframeResourceCatalog(
        RenderSceneSnapshot scene,
        MapRenderOpenGlWireframeResourceBinding binding)
    {
        Scene = scene;
        Binding = binding;
    }

    public RenderSceneSnapshot Scene { get; }

    public MapRenderOpenGlWireframeResourceBinding Binding { get; }

    public static MapRenderOpenGlWireframeResourceCatalog Create(
        RenderSceneSnapshot scene,
        GlMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(scene);
        RenderWireframeSubmissionSnapshot submission = scene.Wireframe ??
            throw new ArgumentException(
                "An OpenGL wireframe catalog requires one frozen collision-wireframe submission.",
                nameof(scene));
        RenderVertexLayoutDescriptor vertexLayout =
            scene.Resources.RequireVertexLayout(
                submission.VertexLayoutIdentity);
        RenderGeometryDescriptor geometry =
            scene.Resources.RequireGeometry(submission.GeometryIdentity);
        var binding = new MapRenderOpenGlWireframeResourceBinding(
            submission,
            vertexLayout,
            geometry,
            mesh);
        return new MapRenderOpenGlWireframeResourceCatalog(scene, binding);
    }
}
