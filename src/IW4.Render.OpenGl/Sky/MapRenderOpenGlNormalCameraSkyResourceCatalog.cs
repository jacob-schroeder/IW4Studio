using System.Collections.Immutable;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl.Sky;

/// <summary>
/// One exact scene-snapshot sky submission paired with the OpenGL resources
/// created for that same source ordinal. Handles never flow back into the
/// backend-neutral resource snapshot.
/// </summary>
internal sealed class MapRenderOpenGlNormalCameraSkyResourceBinding
{
    internal MapRenderOpenGlNormalCameraSkyResourceBinding(
        RenderSkySubmissionSnapshot submission,
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryDescriptor geometry,
        RenderTextureDescriptor texture,
        RenderSamplerDescriptor sampler,
        GlSkyMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(vertexLayout);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(sampler);
        if (submission.VertexLayoutIdentity != vertexLayout.Identity ||
            submission.GeometryIdentity != geometry.Identity ||
            submission.TextureIdentity != texture.Identity ||
            submission.SamplerIdentity != sampler.Identity ||
            geometry.VertexLayout != vertexLayout.Identity)
        {
            throw new ArgumentException(
                "The OpenGL sky binding must retain the exact semantic resource identities.",
                nameof(submission));
        }
        if (vertexLayout.StrideBytes !=
                checked(MapRenderScene.VertexFloatCount * sizeof(float)) ||
            vertexLayout.Elements.Length != 1 ||
            vertexLayout.Elements[0] != new RenderVertexElementDescriptor(
                RenderVertexSemantic.Position,
                semanticIndex: 0,
                RenderVertexElementFormat.Float32x3,
                offsetBytes: 0) ||
            geometry.CoordinateSpace !=
                RenderGeometryCoordinateSpace.Render ||
            geometry.Topology != RenderPrimitiveTopology.TriangleList ||
            geometry.IndexFormat != RenderIndexFormat.Unsigned32 ||
            texture.Dimension != RenderTextureDimension.TextureCube ||
            texture.ArrayLayerCount != 6)
        {
            throw new ArgumentException(
                "The OpenGL sky binding requires the exact position-only triangle-list cubemap resource shape.",
                nameof(submission));
        }
        if (mesh.VertexArray == 0 ||
            mesh.VertexBuffer == 0 ||
            mesh.ElementBuffer == 0 ||
            mesh.Texture == 0 ||
            mesh.IndexCount == 0 ||
            mesh.IndexCount != checked((uint)geometry.IndexCount))
        {
            throw new ArgumentException(
                "The OpenGL sky mesh must completely realize its semantic geometry and cubemap.",
                nameof(mesh));
        }

        Submission = submission;
        VertexLayout = vertexLayout;
        Geometry = geometry;
        Texture = texture;
        Sampler = sampler;
        Mesh = mesh;
    }

    public RenderSkySubmissionSnapshot Submission { get; }

    public RenderVertexLayoutDescriptor VertexLayout { get; }

    public RenderGeometryDescriptor Geometry { get; }

    public RenderTextureDescriptor Texture { get; }

    public RenderSamplerDescriptor Sampler { get; }

    public GlSkyMesh Mesh { get; }
}

/// <summary>
/// Backend-private scene-lifetime lookup. Its binding order is the source
/// scene ordinal order; no filtering, compaction, or identity substitution is
/// permitted while pairing semantic resources with OpenGL handles.
/// </summary>
internal sealed class MapRenderOpenGlNormalCameraSkyResourceCatalog
{
    private MapRenderOpenGlNormalCameraSkyResourceCatalog(
        RenderSceneSnapshot scene,
        ImmutableArray<MapRenderOpenGlNormalCameraSkyResourceBinding>
            bindings)
    {
        Scene = scene;
        Bindings = bindings;
    }

    public RenderSceneSnapshot Scene { get; }

    public ImmutableArray<MapRenderOpenGlNormalCameraSkyResourceBinding>
        Bindings { get; }

    public static MapRenderOpenGlNormalCameraSkyResourceCatalog Create(
        RenderSceneSnapshot scene,
        IReadOnlyList<GlSkyMesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(meshes);
        if (meshes.Count != scene.Skies.Length)
        {
            throw new ArgumentException(
                "OpenGL sky resources must preserve every scene sky ordinal without filtering or compaction.",
                nameof(meshes));
        }

        var bindings = ImmutableArray.CreateBuilder<
            MapRenderOpenGlNormalCameraSkyResourceBinding>(meshes.Count);
        for (var ordinal = 0; ordinal < meshes.Count; ordinal++)
        {
            RenderSkySubmissionSnapshot submission = scene.Skies[ordinal];
            if (submission.SceneOrdinal != ordinal)
            {
                throw new ArgumentException(
                    "The scene snapshot contains a noncanonical sky ordinal.",
                    nameof(scene));
            }

            bindings.Add(new MapRenderOpenGlNormalCameraSkyResourceBinding(
                submission,
                scene.Resources.RequireVertexLayout(
                    submission.VertexLayoutIdentity),
                scene.Resources.RequireGeometry(submission.GeometryIdentity),
                scene.Resources.RequireTexture(submission.TextureIdentity),
                scene.Resources.RequireSampler(submission.SamplerIdentity),
                meshes[ordinal]));
        }

        return new MapRenderOpenGlNormalCameraSkyResourceCatalog(
            scene,
            bindings.MoveToImmutable());
    }
}
