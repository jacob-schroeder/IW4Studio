using System.Collections.Immutable;

using IW4.Render.Geometry;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl.Diagnostics;

/// <summary>
/// One exact scene-snapshot diagnostic submission paired with the OpenGL
/// resources created from that same conceptual source ordinal. OpenGL handles
/// remain backend-private.
/// </summary>
internal sealed class
    MapRenderOpenGlNormalCameraDiagnosticResourceBinding
{
    internal MapRenderOpenGlNormalCameraDiagnosticResourceBinding(
        RenderDiagnosticSubmissionSnapshot submission,
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryDescriptor geometry,
        RenderInstanceLayoutDescriptor? instanceLayout,
        RenderInstanceDescriptor? instances,
        GlMesh mesh,
        GlInstancedMesh instancedMesh)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(vertexLayout);
        ArgumentNullException.ThrowIfNull(geometry);
        if (submission.VertexLayoutIdentity != vertexLayout.Identity ||
            submission.GeometryIdentity != geometry.Identity ||
            geometry.VertexLayout != vertexLayout.Identity)
        {
            throw new ArgumentException(
                "The OpenGL diagnostic binding must retain the exact semantic geometry and vertex-layout identities.",
                nameof(submission));
        }
        if (!IsExactVertexLayout(vertexLayout) ||
            geometry.CoordinateSpace !=
                RenderGeometryCoordinateSpace.Render ||
            geometry.Topology != RenderPrimitiveTopology.TriangleList ||
            geometry.IndexFormat != RenderIndexFormat.Unsigned32 ||
            geometry.ByteOrder != RenderPayloadByteOrder.LittleEndian)
        {
            throw new ArgumentException(
                "The OpenGL diagnostic binding requires the exact render-space position/color U32 triangle-list resource shape.",
                nameof(submission));
        }

        bool isInstanced = submission.Kind ==
            RenderDiagnosticSubmissionKind.InstancedSolid;
        if (isInstanced)
        {
            if (submission.InstancesIdentity is not { } instancesIdentity ||
                submission.InstanceLayoutIdentity is not { } layoutIdentity ||
                instanceLayout is null ||
                instances is null ||
                instances.Identity != instancesIdentity ||
                instanceLayout.Identity != layoutIdentity ||
                instances.Layout != layoutIdentity ||
                !IsExactInstanceLayout(instanceLayout) ||
                instances.ByteOrder != RenderPayloadByteOrder.LittleEndian)
            {
                throw new ArgumentException(
                    "The OpenGL instanced diagnostic binding must retain the exact semantic instance resource and transform-row layout.",
                    nameof(submission));
            }
            if (mesh != default ||
                instancedMesh.VertexArray == 0 ||
                instancedMesh.VertexBuffer == 0 ||
                instancedMesh.ElementBuffer == 0 ||
                instancedMesh.InstanceBuffer == 0 ||
                instancedMesh.IndexCount != checked((uint)geometry.IndexCount) ||
                instancedMesh.InstanceCount !=
                    checked((uint)instances.InstanceCount))
            {
                throw new ArgumentException(
                    "The OpenGL instanced diagnostic mesh must completely realize its semantic geometry and instance counts.",
                    nameof(instancedMesh));
            }
        }
        else
        {
            if (submission.InstancesIdentity.HasValue ||
                submission.InstanceLayoutIdentity.HasValue ||
                instanceLayout is not null ||
                instances is not null ||
                instancedMesh != default ||
                mesh.VertexArray == 0 ||
                mesh.VertexBuffer == 0 ||
                mesh.ElementBuffer == 0 ||
                mesh.IndexCount != checked((uint)geometry.IndexCount))
            {
                throw new ArgumentException(
                    "The OpenGL non-instanced diagnostic mesh must completely realize only its semantic geometry.",
                    nameof(mesh));
            }
        }

        Submission = submission;
        VertexLayout = vertexLayout;
        Geometry = geometry;
        InstanceLayout = instanceLayout;
        Instances = instances;
        Mesh = mesh;
        InstancedMesh = instancedMesh;
    }

    public RenderDiagnosticSubmissionSnapshot Submission { get; }

    public RenderVertexLayoutDescriptor VertexLayout { get; }

    public RenderGeometryDescriptor Geometry { get; }

    public RenderInstanceLayoutDescriptor? InstanceLayout { get; }

    public RenderInstanceDescriptor? Instances { get; }

    public GlMesh Mesh { get; }

    public GlInstancedMesh InstancedMesh { get; }

    public bool IsInstanced => Submission.Kind ==
        RenderDiagnosticSubmissionKind.InstancedSolid;

    private static bool IsExactVertexLayout(
        RenderVertexLayoutDescriptor layout) =>
        layout.StrideBytes ==
            checked(MapRenderScene.VertexFloatCount * sizeof(float)) &&
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

    private static bool IsExactInstanceLayout(
        RenderInstanceLayoutDescriptor layout) =>
        layout.StrideBytes ==
            checked(MapRenderStaticInstanceBufferPacker
                .PlacementOnlyFloatStride * sizeof(float)) &&
        layout.Elements.SequenceEqual(
        [
            new RenderInstanceElementDescriptor(
                RenderInstanceSemantic.TransformRow,
                semanticIndex: 0,
                RenderVertexElementFormat.Float32x4,
                offsetBytes: 0),
            new RenderInstanceElementDescriptor(
                RenderInstanceSemantic.TransformRow,
                semanticIndex: 1,
                RenderVertexElementFormat.Float32x4,
                offsetBytes: 4 * sizeof(float)),
            new RenderInstanceElementDescriptor(
                RenderInstanceSemantic.TransformRow,
                semanticIndex: 2,
                RenderVertexElementFormat.Float32x4,
                offsetBytes: 8 * sizeof(float))
        ]);
}

/// <summary>
/// Backend-private scene-lifetime diagnostic lookup. Bindings retain frozen
/// semantic order even though empty source categories remain absent from the
/// shared snapshot.
/// </summary>
internal sealed class MapRenderOpenGlNormalCameraDiagnosticResourceCatalog
{
    private MapRenderOpenGlNormalCameraDiagnosticResourceCatalog(
        RenderSceneSnapshot scene,
        bool resourcesAvailable,
        ImmutableArray<
            MapRenderOpenGlNormalCameraDiagnosticResourceBinding> bindings)
    {
        Scene = scene;
        ResourcesAvailable = resourcesAvailable;
        Bindings = bindings;
    }

    public RenderSceneSnapshot Scene { get; }

    public bool ResourcesAvailable { get; }

    public ImmutableArray<
        MapRenderOpenGlNormalCameraDiagnosticResourceBinding> Bindings
        { get; }

    public static MapRenderOpenGlNormalCameraDiagnosticResourceCatalog
        Create(
            RenderSceneSnapshot scene,
            GlMesh fallbackSolid,
            GlMesh solid,
            IReadOnlyList<GlInstancedMesh> instancedSolid)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(instancedSolid);

        RenderDiagnosticSubmissionSnapshot? fallback = scene.Diagnostics
            .SingleOrDefault(submission => submission.Kind ==
                RenderDiagnosticSubmissionKind.FallbackSolid);
        RenderDiagnosticSubmissionSnapshot? regular = scene.Diagnostics
            .SingleOrDefault(submission => submission.Kind ==
                RenderDiagnosticSubmissionKind.Solid);
        RequirePresenceMatches(fallbackSolid, fallback, "fallbackSolid");
        RequirePresenceMatches(solid, regular, "solid");

        var instancedByBatch = scene.Diagnostics
            .Where(submission => submission.Kind ==
                RenderDiagnosticSubmissionKind.InstancedSolid)
            .ToDictionary(
                submission => submission.InstancedBatchIndex ??
                    throw new ArgumentException(
                        "An instanced diagnostic submission has no source batch index.",
                        nameof(scene)));
        for (var batchIndex = 0;
             batchIndex < instancedSolid.Count;
             batchIndex++)
        {
            bool expected = instancedByBatch.ContainsKey(batchIndex);
            bool realized = instancedSolid[batchIndex] != default;
            if (expected != realized)
            {
                throw new ArgumentException(
                    $"OpenGL instanced diagnostic source batch {batchIndex} does not match snapshot materialization.",
                    nameof(instancedSolid));
            }
        }
        if (instancedByBatch.Keys.Any(batchIndex =>
                batchIndex < 0 || batchIndex >= instancedSolid.Count))
        {
            throw new ArgumentException(
                "OpenGL instanced diagnostic resources do not retain every materialized snapshot source batch.",
                nameof(instancedSolid));
        }

        var bindings = ImmutableArray.CreateBuilder<
            MapRenderOpenGlNormalCameraDiagnosticResourceBinding>(
                scene.Diagnostics.Length);
        foreach (RenderDiagnosticSubmissionSnapshot submission in
                 scene.Diagnostics)
        {
            GlMesh mesh = submission.Kind switch
            {
                RenderDiagnosticSubmissionKind.FallbackSolid =>
                    fallbackSolid,
                RenderDiagnosticSubmissionKind.Solid => solid,
                RenderDiagnosticSubmissionKind.InstancedSolid => default,
                _ => throw new ArgumentOutOfRangeException(nameof(scene))
            };
            GlInstancedMesh instancedMesh = submission.Kind ==
                RenderDiagnosticSubmissionKind.InstancedSolid
                    ? instancedSolid[
                        submission.InstancedBatchIndex ??
                        throw new ArgumentException(
                            "An instanced diagnostic submission has no source batch index.",
                            nameof(scene))]
                    : default;
            RenderInstanceLayoutDescriptor? instanceLayout =
                submission.InstanceLayoutIdentity is { } layoutIdentity
                    ? scene.Resources.RequireInstanceLayout(layoutIdentity)
                    : null;
            RenderInstanceDescriptor? instances =
                submission.InstancesIdentity is { } instancesIdentity
                    ? scene.Resources.RequireInstances(instancesIdentity)
                    : null;
            bindings.Add(new
                MapRenderOpenGlNormalCameraDiagnosticResourceBinding(
                    submission,
                    scene.Resources.RequireVertexLayout(
                        submission.VertexLayoutIdentity),
                    scene.Resources.RequireGeometry(
                        submission.GeometryIdentity),
                    instanceLayout,
                    instances,
                    mesh,
                    instancedMesh));
        }

        return new MapRenderOpenGlNormalCameraDiagnosticResourceCatalog(
            scene,
            resourcesAvailable: true,
            bindings.MoveToImmutable());
    }

    /// <summary>
    /// Isolation deliberately uploads no diagnostic geometry. This catalog
    /// can lower only a frame whose diagnostics pass is correspondingly empty.
    /// </summary>
    public static MapRenderOpenGlNormalCameraDiagnosticResourceCatalog
        CreateUnavailable(RenderSceneSnapshot scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new MapRenderOpenGlNormalCameraDiagnosticResourceCatalog(
            scene,
            resourcesAvailable: false,
            ImmutableArray<
                MapRenderOpenGlNormalCameraDiagnosticResourceBinding>.Empty);
    }

    private static void RequirePresenceMatches(
        GlMesh mesh,
        RenderDiagnosticSubmissionSnapshot? submission,
        string parameterName)
    {
        if ((mesh != default) != (submission is not null))
        {
            throw new ArgumentException(
                "OpenGL diagnostic source materialization does not match its frozen snapshot category.",
                parameterName);
        }
    }
}
