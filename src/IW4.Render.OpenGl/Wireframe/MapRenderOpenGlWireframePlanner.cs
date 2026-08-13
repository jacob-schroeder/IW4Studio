using System.Collections.Immutable;
using System.Numerics;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Wireframe;

/// <summary>
/// Strict lowering of the shared normal-camera collision-wireframe pass. This
/// path creates no OpenGL object and never mutates the immutable core plan.
/// </summary>
internal static class MapRenderOpenGlWireframePlanner
{
    /// <summary>
    /// Lowers the exact normal-camera wireframe pass and derives the prepared
    /// host matrix from the immutable PS3-native rows in that same pass. This
    /// is the cold diagnostic-owner route; the production renderer overload
    /// below continues to verify its independently prepared matrix.
    /// </summary>
    public static MapRenderOpenGlWireframePlan LowerNormalCamera(
        RenderFramePlan framePlan,
        MapRenderOpenGlWireframeResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(resources);
        RenderPassPlan pass = framePlan.Passes.SingleOrDefault(candidate =>
                candidate.Identity ==
                    RenderFramePlanner.NormalCameraWireframePass) ??
            throw new ArgumentException(
                "The normal-camera frame has no wireframe pass.",
                nameof(framePlan));
        RenderDrawPlan draw = pass.Draws.Length == 1
            ? pass.Draws[0]
            : throw new ArgumentException(
                "The normal-camera wireframe pass must contain one draw.",
                nameof(framePlan));
        Matrix4x4 preparedHostViewProjection =
            OpenGlRsxClipSpaceLowering
                .CreateDirectEditorPreviewHostViewProjectionFromPs3Native(
                    DecodePs3NativeWorldViewProjection(draw));
        return LowerNormalCamera(
            framePlan,
            resources,
            preparedHostViewProjection);
    }

    public static MapRenderOpenGlWireframePlan LowerNormalCamera(
        RenderFramePlan framePlan,
        MapRenderOpenGlWireframeResourceCatalog resources,
        Matrix4x4 preparedHostViewProjection)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(resources);
        if (!ReferenceEquals(framePlan.Resources, resources.Scene.Resources))
        {
            throw new ArgumentException(
                "The OpenGL wireframe catalog must belong to the exact scene resource snapshot used by the frame plan.",
                nameof(resources));
        }

        ValidateNormalCameraFrame(
            framePlan,
            out RenderAttachmentDescriptor color,
            out RenderAttachmentDescriptor depthStencil,
            out RenderPassPlan pass,
            out RenderColorAttachmentPlan colorPlan,
            out RenderDepthStencilAttachmentPlan depthStencilPlan);
        RenderDrawPlan draw = pass.Draws[0];
        ValidateDraw(
            draw,
            color,
            depthStencil,
            resources.Binding,
            framePlan.Resources,
            RenderFramePlanner.WireframePipelineIdentity,
            RenderMultisampleStateDescriptor.Ps3Target2);

        Matrix4x4 hostWorldViewProjection =
            OpenGlRsxClipSpaceLowering
                .CreateDirectEditorPreviewHostViewProjectionFromPs3Native(
                    DecodePs3NativeWorldViewProjection(draw));
        if (hostWorldViewProjection != preparedHostViewProjection)
        {
            throw new ArgumentException(
                "The prepared OpenGL wireframe matrix differs from the frame plan's PS3-native world-view-projection lowering.",
                nameof(preparedHostViewProjection));
        }

        var command = new MapRenderOpenGlWireframeDrawCommand(
            draw,
            resources.Binding,
            hostWorldViewProjection);
        return new MapRenderOpenGlWireframePlan(
            framePlan,
            color,
            depthStencil,
            pass,
            colorPlan,
            depthStencilPlan,
            resources,
            command);
    }

    private static void ValidateNormalCameraFrame(
        RenderFramePlan framePlan,
        out RenderAttachmentDescriptor color,
        out RenderAttachmentDescriptor depthStencil,
        out RenderPassPlan wireframePass,
        out RenderColorAttachmentPlan colorPlan,
        out RenderDepthStencilAttachmentPlan depthStencilPlan)
    {
        if (framePlan.Attachments.Length != 2)
            throw Invalid("exactly the normal-camera color and depth-stencil attachments");
        color = framePlan.Attachments[0];
        depthStencil = framePlan.Attachments[1];
        if (color.Identity !=
                RenderFramePlanner.NormalCameraSceneColorAttachment ||
            color.Role != RenderAttachmentRole.Color ||
            color.PixelFormat != RenderAttachmentPixelFormat.Rgba8Unorm ||
            color.SampleCount != 2 ||
            depthStencil.Identity != RenderFramePlanner
                .NormalCameraSceneDepthStencilAttachment ||
            depthStencil.Role != RenderAttachmentRole.DepthStencil ||
            depthStencil.PixelFormat !=
                RenderAttachmentPixelFormat.Depth24Stencil8 ||
            depthStencil.SampleCount != 2 ||
            color.Extent != depthStencil.Extent ||
            color.Extent != framePlan.SurfaceExtents.SceneTarget)
        {
            throw Invalid(
                "matching two-sample normal-camera RGBA8 and D24S8 attachments");
        }

        if (framePlan.Passes.IsEmpty ||
            framePlan.Passes[0].Identity !=
                RenderFramePlanner.NormalCameraScenePass ||
            framePlan.Passes.Count(pass => pass.Identity ==
                RenderFramePlanner.NormalCameraWireframePass) != 1)
        {
            throw Invalid(
                "one normal-camera scene entry and exactly one wireframe pass");
        }
        var wireframeIndex = -1;
        for (var index = 0; index < framePlan.Passes.Length; index++)
        {
            if (framePlan.Passes[index].Identity ==
                RenderFramePlanner.NormalCameraWireframePass)
            {
                wireframeIndex = index;
                break;
            }
        }
        if (wireframeIndex != framePlan.Passes.Length - 1)
            throw Invalid("the wireframe pass last in normal-camera order");

        int expectedIndex = 1;
        if (framePlan.Passes.Any(pass => pass.Identity ==
                RenderFramePlanner.NormalCameraSkyPass))
        {
            if (framePlan.Passes.Count(pass => pass.Identity ==
                    RenderFramePlanner.NormalCameraSkyPass) != 1 ||
                framePlan.Passes[expectedIndex++].Identity !=
                    RenderFramePlanner.NormalCameraSkyPass)
            {
                throw Invalid("at most one sky pass immediately after scene entry");
            }
        }
        if (framePlan.Passes.Any(pass => pass.Identity ==
                RenderFramePlanner.NormalCameraDiagnosticsPass))
        {
            if (framePlan.Passes.Count(pass => pass.Identity ==
                    RenderFramePlanner.NormalCameraDiagnosticsPass) != 1 ||
                framePlan.Passes[expectedIndex++].Identity !=
                    RenderFramePlanner.NormalCameraDiagnosticsPass)
            {
                throw Invalid(
                    "at most one diagnostics pass after scene/sky and before wireframe");
            }
        }
        if (expectedIndex != wireframeIndex)
        {
            throw Invalid(
                "only scene, optional sky, optional diagnostics, then wireframe passes");
        }

        RenderPassPlan scenePass = framePlan.Passes[0];
        wireframePass = framePlan.Passes[wireframeIndex];
        if (wireframePass.Purpose != RenderPassPurpose.Wireframe ||
            wireframePass.Viewport != scenePass.Viewport ||
            wireframePass.Scissor != scenePass.Scissor ||
            wireframePass.ColorAttachments.Length != 1 ||
            wireframePass.DepthStencilAttachment is null ||
            wireframePass.Draws.Length != 1)
        {
            throw Invalid(
                "one exact full-target wireframe draw after scene-target entry");
        }

        colorPlan = wireframePass.ColorAttachments[0];
        depthStencilPlan = wireframePass.DepthStencilAttachment;
        if (colorPlan.Attachment != color.Identity ||
            colorPlan.Load != RenderAttachmentLoadRequirement.Preserve ||
            colorPlan.Store != RenderAttachmentStoreRequirement.Preserve ||
            colorPlan.ClearValue is not null ||
            depthStencilPlan.Attachment != depthStencil.Identity ||
            depthStencilPlan.DepthLoad !=
                RenderAttachmentLoadRequirement.Preserve ||
            depthStencilPlan.DepthStore !=
                RenderAttachmentStoreRequirement.Preserve ||
            depthStencilPlan.ClearDepth is not null ||
            depthStencilPlan.StencilLoad !=
                RenderAttachmentLoadRequirement.Preserve ||
            depthStencilPlan.StencilStore !=
                RenderAttachmentStoreRequirement.Preserve ||
            depthStencilPlan.ClearStencil is not null)
        {
            throw Invalid(
                "preserve/preserve scene color and depth-stencil attachment intent");
        }
        if (framePlan.PreviewRequirements !=
                RenderPreviewRequirements.Presentation ||
            framePlan.PickingRequirements != RenderPickingRequirements.None)
        {
            throw Invalid(
                "presentation-only requirements with no picking state");
        }
    }

    private static void ValidateDraw(
        RenderDrawPlan draw,
        RenderAttachmentDescriptor color,
        RenderAttachmentDescriptor depthStencil,
        MapRenderOpenGlWireframeResourceBinding binding,
        RenderResourceSnapshot frameResources,
        RenderSemanticIdentity expectedPipelineIdentity,
        RenderMultisampleStateDescriptor expectedMultisample)
    {
        RenderWireframeSubmissionSnapshot submission = binding.Submission;
        RenderGeometryDescriptor geometry = binding.Geometry;
        RenderPipelineDescriptor pipeline = draw.Pipeline;
        if (draw.Identity != submission.DrawIdentity ||
            draw.Geometry != new RenderGeometrySlice(
                submission.GeometryIdentity,
                submission.VertexLayoutIdentity,
                RenderPrimitiveTopology.LineList,
                firstVertex: 0,
                geometry.VertexCount,
                firstIndex: 0,
                geometry.IndexCount,
                RenderIndexFormat.Unsigned32) ||
            draw.Instances is not null ||
            !draw.Textures.IsEmpty ||
            draw.Range != new RenderDrawRange(
                firstIndex: 0,
                geometry.IndexCount,
                baseVertex: 0,
                firstInstance: 0,
                instanceCount: 1) ||
            draw.SortKey != new RenderDrawSortKey(0, 0, 0) ||
            draw.PickingIdentity is not null ||
            draw.PreviewRequirement !=
                (RenderPreviewDrawRequirement.VisibleInPreview |
                 RenderPreviewDrawRequirement.EligibleForScreenshot))
        {
            throw Invalid(
                "one full non-instanced source-zero LineList/U32 draw with no texture or picking bindings");
        }

        if (pipeline.Identity != expectedPipelineIdentity ||
            pipeline.VertexLayout != submission.VertexLayoutIdentity ||
            pipeline.InstanceLayout is not null ||
            pipeline.Topology != RenderPrimitiveTopology.LineList ||
            !pipeline.ColorAttachmentFormats.AsSpan().SequenceEqual(
                [color.PixelFormat]) ||
            pipeline.DepthStencilAttachmentFormat !=
                depthStencil.PixelFormat ||
            pipeline.Multisample != expectedMultisample ||
            !pipeline.FixedState.ContentEquals(
                RenderFixedStatePresets.WireframeV1) ||
            !pipeline.ShaderProgram.ContentEquals(
                RenderFramePlanner.WireframeShaderProgram) ||
            !draw.Material.ContentEquals(
                RenderFramePlanner.WireframeMaterial))
        {
            throw Invalid(
                "the exact line-width-1.25, depth-disabled wireframe pipeline, shader, material, and target formats");
        }

        RenderDynamicConstantBinding? constants =
            draw.DynamicConstants.SingleOrDefault();
        if (constants is null ||
            constants.Identity != RenderFramePlanner
                .WireframeWorldViewProjectionConstantIdentity ||
            constants.BindingPoint != RenderFramePlanner
                .WireframeWorldViewProjectionBindingPoint ||
            constants.Encoding !=
                RenderDynamicConstantEncoding.Matrix4x4Rows ||
            constants.CoordinateSpace !=
                RenderShaderCoordinateSpace.Ps3Native ||
            constants.Values.Length != 4)
        {
            throw Invalid(
                "exactly four PS3-native WVP rows at vertex destination zero");
        }

        if (!ReferenceEquals(
                frameResources.RequireVertexLayout(
                    submission.VertexLayoutIdentity),
                binding.VertexLayout) ||
            !ReferenceEquals(
                frameResources.RequireGeometry(
                    submission.GeometryIdentity),
                binding.Geometry))
        {
            throw Invalid(
                "resource identities resolving to the exact retained snapshot objects");
        }
    }

    private static Matrix4x4 DecodePs3NativeWorldViewProjection(
        RenderDrawPlan draw)
    {
        ImmutableArray<Vector4> rows =
            draw.DynamicConstants.Single().Values;
        return new Matrix4x4(
            rows[0].X, rows[0].Y, rows[0].Z, rows[0].W,
            rows[1].X, rows[1].Y, rows[1].Z, rows[1].W,
            rows[2].X, rows[2].Y, rows[2].Z, rows[2].W,
            rows[3].X, rows[3].Y, rows[3].Z, rows[3].W);
    }

    private static ArgumentException Invalid(string requirement) => new(
        $"OpenGL wireframe lowering requires {requirement}.",
        "framePlan");
}
