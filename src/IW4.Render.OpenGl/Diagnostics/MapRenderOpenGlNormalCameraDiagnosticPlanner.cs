using System.Collections.Immutable;
using System.Numerics;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Diagnostics;

/// <summary>
/// Resolves one backend-neutral diagnostics pass against OpenGL-owned scene
/// resources. The already-established production matrix is checked against
/// the plan rather than being recomputed from backend state.
/// </summary>
internal static class MapRenderOpenGlNormalCameraDiagnosticPlanner
{
    public static MapRenderOpenGlNormalCameraDiagnosticPlan Lower(
        RenderFramePlan framePlan,
        MapRenderOpenGlNormalCameraDiagnosticResourceCatalog resources,
        Matrix4x4 preparedHostViewProjection)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(resources);
        if (!ReferenceEquals(framePlan.Resources, resources.Scene.Resources))
        {
            throw new ArgumentException(
                "The OpenGL diagnostic catalog must belong to the exact scene resource snapshot used by the frame plan.",
                nameof(resources));
        }

        RenderPassPlan diagnosticsPass = ValidatePass(framePlan);
        if (diagnosticsPass.Draws.IsEmpty)
        {
            return new MapRenderOpenGlNormalCameraDiagnosticPlan(
                framePlan,
                diagnosticsPass,
                resources,
                preparedHostViewProjection,
                ImmutableArray<
                    MapRenderOpenGlNormalCameraDiagnosticDrawCommand>.Empty);
        }
        if (!resources.ResourcesAvailable ||
            diagnosticsPass.Draws.Length != resources.Bindings.Length)
        {
            throw new ArgumentException(
                "A non-empty diagnostics pass requires every exact OpenGL catalog resource binding.",
                nameof(resources));
        }

        var commands = ImmutableArray.CreateBuilder<
            MapRenderOpenGlNormalCameraDiagnosticDrawCommand>(
                diagnosticsPass.Draws.Length);
        bool reachedInstanced = false;
        Matrix4x4? semanticHostViewProjection = null;
        for (var drawIndex = 0;
             drawIndex < diagnosticsPass.Draws.Length;
             drawIndex++)
        {
            RenderDrawPlan draw = diagnosticsPass.Draws[drawIndex];
            MapRenderOpenGlNormalCameraDiagnosticResourceBinding resource =
                resources.Bindings[drawIndex];
            ValidateDraw(draw, resource, framePlan.Resources, drawIndex);
            if (resource.IsInstanced)
            {
                reachedInstanced = true;
            }
            else if (reachedInstanced)
            {
                throw InvalidDraw(
                    drawIndex,
                    "a non-instanced source follows an instanced source");
            }

            Matrix4x4 drawHostViewProjection =
                OpenGlRsxClipSpaceLowering
                    .CreateDirectEditorPreviewHostViewProjectionFromPs3Native(
                        DecodePs3NativeWorldViewProjection(draw));
            if (semanticHostViewProjection is { } previous &&
                previous != drawHostViewProjection)
            {
                throw InvalidDraw(
                    drawIndex,
                    "world-view-projection differs from the other diagnostics draws");
            }
            semanticHostViewProjection = drawHostViewProjection;
            commands.Add(
                new MapRenderOpenGlNormalCameraDiagnosticDrawCommand(
                    draw,
                    resource));
        }

        if (semanticHostViewProjection != preparedHostViewProjection)
        {
            throw new ArgumentException(
                "The prepared OpenGL diagnostics matrix differs from the frame plan's PS3-native world-view-projection lowering.",
                nameof(preparedHostViewProjection));
        }

        return new MapRenderOpenGlNormalCameraDiagnosticPlan(
            framePlan,
            diagnosticsPass,
            resources,
            preparedHostViewProjection,
            commands.MoveToImmutable());
    }

    private static RenderPassPlan ValidatePass(RenderFramePlan framePlan)
    {
        if (framePlan.Passes.IsEmpty ||
            framePlan.Passes[0].Identity !=
                RenderFramePlanner.NormalCameraScenePass ||
            framePlan.Passes.Count(pass => pass.Identity ==
                RenderFramePlanner.NormalCameraDiagnosticsPass) != 1)
        {
            throw new ArgumentException(
                "OpenGL diagnostic lowering requires exactly one normal-camera diagnostics pass after scene-target entry.",
                nameof(framePlan));
        }

        bool hasSky = framePlan.Passes.Any(pass => pass.Identity ==
            RenderFramePlanner.NormalCameraSkyPass);
        if (framePlan.Passes.Count(pass => pass.Identity ==
                RenderFramePlanner.NormalCameraSkyPass) > 1)
        {
            throw new ArgumentException(
                "OpenGL diagnostic lowering cannot accept duplicate normal-camera sky passes.",
                nameof(framePlan));
        }
        int diagnosticsIndex = hasSky ? 2 : 1;
        if (framePlan.Passes.Length <= diagnosticsIndex ||
            (hasSky && framePlan.Passes[1].Identity !=
                RenderFramePlanner.NormalCameraSkyPass) ||
            framePlan.Passes[diagnosticsIndex].Identity !=
                RenderFramePlanner.NormalCameraDiagnosticsPass)
        {
            throw new ArgumentException(
                "OpenGL diagnostic lowering requires scene, optional sky, then diagnostics in exact order.",
                nameof(framePlan));
        }

        RenderPassPlan scenePass = framePlan.Passes[0];
        RenderPassPlan diagnosticsPass = framePlan.Passes[diagnosticsIndex];
        if (diagnosticsPass.Purpose != RenderPassPurpose.Diagnostics ||
            diagnosticsPass.Viewport != scenePass.Viewport ||
            diagnosticsPass.Scissor != scenePass.Scissor ||
            diagnosticsPass.ColorAttachments.Length != 1 ||
            diagnosticsPass.ColorAttachments[0].Attachment !=
                RenderFramePlanner.NormalCameraSceneColorAttachment ||
            diagnosticsPass.ColorAttachments[0].Load !=
                RenderAttachmentLoadRequirement.Preserve ||
            diagnosticsPass.ColorAttachments[0].Store !=
                RenderAttachmentStoreRequirement.Preserve ||
            diagnosticsPass.ColorAttachments[0].ClearValue is not null ||
            diagnosticsPass.DepthStencilAttachment is not
                { } depthStencil ||
            depthStencil.Attachment != RenderFramePlanner
                .NormalCameraSceneDepthStencilAttachment ||
            depthStencil.DepthLoad !=
                RenderAttachmentLoadRequirement.Preserve ||
            depthStencil.DepthStore !=
                RenderAttachmentStoreRequirement.Preserve ||
            depthStencil.ClearDepth is not null ||
            depthStencil.StencilLoad !=
                RenderAttachmentLoadRequirement.Preserve ||
            depthStencil.StencilStore !=
                RenderAttachmentStoreRequirement.Preserve ||
            depthStencil.ClearStencil is not null)
        {
            throw new ArgumentException(
                "OpenGL diagnostic lowering requires exact preserve semantics for the scene color and depth-stencil attachments.",
                nameof(framePlan));
        }

        RenderAttachmentDescriptor? color = framePlan.Attachments
            .SingleOrDefault(attachment => attachment.Identity ==
                RenderFramePlanner.NormalCameraSceneColorAttachment);
        RenderAttachmentDescriptor? depth = framePlan.Attachments
            .SingleOrDefault(attachment => attachment.Identity ==
                RenderFramePlanner.NormalCameraSceneDepthStencilAttachment);
        if (color is null ||
            depth is null ||
            color.Role != RenderAttachmentRole.Color ||
            color.PixelFormat != RenderAttachmentPixelFormat.Rgba8Unorm ||
            color.SampleCount != 2 ||
            depth.Role != RenderAttachmentRole.DepthStencil ||
            depth.PixelFormat !=
                RenderAttachmentPixelFormat.Depth24Stencil8 ||
            depth.SampleCount != 2 ||
            color.Extent != depth.Extent)
        {
            throw new ArgumentException(
                "OpenGL diagnostic lowering requires the exact two-sample scene attachments.",
                nameof(framePlan));
        }

        return diagnosticsPass;
    }

    private static void ValidateDraw(
        RenderDrawPlan draw,
        MapRenderOpenGlNormalCameraDiagnosticResourceBinding resource,
        RenderResourceSnapshot frameResources,
        int drawIndex)
    {
        RenderDiagnosticSubmissionSnapshot submission =
            resource.Submission;
        RenderGeometryDescriptor geometry = resource.Geometry;
        RenderPipelineDescriptor pipeline = draw.Pipeline;
        bool isInstanced = submission.Kind ==
            RenderDiagnosticSubmissionKind.InstancedSolid;
        if (draw.Identity != submission.DrawIdentity ||
            draw.SortKey != new RenderDrawSortKey(
                Primary: 0,
                Secondary: 0,
                SourceOrdinal: submission.SourceOrdinal) ||
            draw.Geometry.Geometry != submission.GeometryIdentity ||
            draw.Geometry.VertexLayout != submission.VertexLayoutIdentity ||
            draw.Geometry.Topology != RenderPrimitiveTopology.TriangleList ||
            draw.Geometry.FirstVertex != 0 ||
            draw.Geometry.VertexCount != geometry.VertexCount ||
            draw.Geometry.FirstIndex != 0 ||
            draw.Geometry.IndexCount != geometry.IndexCount ||
            draw.Geometry.IndexFormat != RenderIndexFormat.Unsigned32 ||
            geometry.CoordinateSpace !=
                RenderGeometryCoordinateSpace.Render ||
            !draw.Textures.IsEmpty ||
            draw.PickingIdentity is not null ||
            draw.PreviewRequirement !=
                (RenderPreviewDrawRequirement.VisibleInPreview |
                 RenderPreviewDrawRequirement.EligibleForScreenshot))
        {
            throw InvalidDraw(
                drawIndex,
                "draw identity, source order, geometry, or preview range differs from the exact diagnostics contract");
        }

        RenderInstanceDescriptor? instances = resource.Instances;
        if (isInstanced)
        {
            if (submission.InstancesIdentity is not
                    { } instancesIdentity ||
                submission.InstanceLayoutIdentity is not
                    { } instanceLayoutIdentity ||
                instances is null ||
                draw.Instances is not { } slice ||
                slice.Instances != instancesIdentity ||
                slice.FirstInstance != 0 ||
                slice.InstanceCount != instances.InstanceCount ||
                draw.Range != new RenderDrawRange(
                    firstIndex: 0,
                    geometry.IndexCount,
                    baseVertex: 0,
                    firstInstance: 0,
                    instances.InstanceCount) ||
                pipeline.Identity != RenderFramePlanner
                    .DiagnosticsInstancedSolidPipelineIdentity ||
                pipeline.InstanceLayout != instanceLayoutIdentity ||
                !pipeline.ShaderProgram.ContentEquals(
                    RenderFramePlanner
                        .DiagnosticsInstancedSolidShaderProgram))
            {
                throw InvalidDraw(
                    drawIndex,
                    "instance slice, range, pipeline, or instanced shader variant differs from the exact diagnostics contract");
            }
        }
        else if (draw.Instances is not null ||
                 draw.Range != new RenderDrawRange(
                     firstIndex: 0,
                     geometry.IndexCount,
                     baseVertex: 0,
                     firstInstance: 0,
                     instanceCount: 1) ||
                 pipeline.Identity != RenderFramePlanner
                     .DiagnosticsSolidPipelineIdentity ||
                 pipeline.InstanceLayout is not null ||
                 !pipeline.ShaderProgram.ContentEquals(
                     RenderFramePlanner.DiagnosticsSolidShaderProgram))
        {
            throw InvalidDraw(
                drawIndex,
                "non-instanced range, pipeline, or shader variant differs from the exact diagnostics contract");
        }

        if (pipeline.VertexLayout != submission.VertexLayoutIdentity ||
            pipeline.Topology != RenderPrimitiveTopology.TriangleList ||
            !pipeline.ColorAttachmentFormats.SequenceEqual(
                [RenderAttachmentPixelFormat.Rgba8Unorm]) ||
            pipeline.DepthStencilAttachmentFormat !=
                RenderAttachmentPixelFormat.Depth24Stencil8 ||
            pipeline.Multisample !=
                RenderMultisampleStateDescriptor.Ps3Target2 ||
            !pipeline.FixedState.ContentEquals(
                RenderFixedStatePresets.DiagnosticsV1) ||
            !draw.Material.ContentEquals(
                RenderFramePlanner.DiagnosticsMaterial))
        {
            throw InvalidDraw(
                drawIndex,
                "pipeline attachments, default fixed state, or material differs from the exact diagnostics contract");
        }

        RenderDynamicConstantBinding? constants = draw.DynamicConstants
            .SingleOrDefault();
        if (constants is null ||
            constants.Identity != RenderFramePlanner
                .DiagnosticsWorldViewProjectionConstantIdentity ||
            constants.BindingPoint != RenderFramePlanner
                .DiagnosticsWorldViewProjectionBindingPoint ||
            constants.Encoding !=
                RenderDynamicConstantEncoding.Matrix4x4Rows ||
            constants.CoordinateSpace !=
                RenderShaderCoordinateSpace.Ps3Native ||
            constants.Values.Length != 4)
        {
            throw InvalidDraw(
                drawIndex,
                "dynamic constants differ from the exact diagnostics shader ABI");
        }

        if (!ReferenceEquals(
                frameResources.RequireVertexLayout(
                    submission.VertexLayoutIdentity),
                resource.VertexLayout) ||
            !ReferenceEquals(
                frameResources.RequireGeometry(
                    submission.GeometryIdentity),
                resource.Geometry) ||
            (submission.InstanceLayoutIdentity is { } layoutIdentity &&
             !ReferenceEquals(
                 frameResources.RequireInstanceLayout(layoutIdentity),
                 resource.InstanceLayout)) ||
            (submission.InstancesIdentity is { } referencedInstances &&
             !ReferenceEquals(
                 frameResources.RequireInstances(referencedInstances),
                 resource.Instances)))
        {
            throw InvalidDraw(
                drawIndex,
                "OpenGL resource resolution does not retain the frame snapshot objects");
        }
    }

    private static Matrix4x4 DecodePs3NativeWorldViewProjection(
        RenderDrawPlan draw)
    {
        ImmutableArray<Vector4> rows =
            draw.DynamicConstants.Single().Values;
        return new Matrix4x4(
            rows[0].X,
            rows[0].Y,
            rows[0].Z,
            rows[0].W,
            rows[1].X,
            rows[1].Y,
            rows[1].Z,
            rows[1].W,
            rows[2].X,
            rows[2].Y,
            rows[2].Z,
            rows[2].W,
            rows[3].X,
            rows[3].Y,
            rows[3].Z,
            rows[3].W);
    }

    private static ArgumentException InvalidDraw(
        int drawIndex,
        string reason) => new(
        $"OpenGL diagnostic draw {drawIndex} is invalid: {reason}.",
        "framePlan");
}
