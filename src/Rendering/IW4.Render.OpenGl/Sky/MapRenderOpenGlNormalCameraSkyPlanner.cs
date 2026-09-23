using System.Collections.Immutable;
using System.Numerics;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.OpenGl.Sky;

/// <summary>
/// Resolves one backend-neutral sky pass against OpenGL-owned scene resources
/// and lowers only its explicitly PS3-native camera constant.
/// </summary>
internal static class MapRenderOpenGlNormalCameraSkyPlanner
{
    public static MapRenderOpenGlNormalCameraSkyPlan Lower(
        RenderFramePlan framePlan,
        MapRenderOpenGlNormalCameraSkyResourceCatalog resources)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(resources);
        if (!ReferenceEquals(
                framePlan.Resources,
                resources.Scene.Resources))
        {
            throw new ArgumentException(
                "The OpenGL sky catalog must belong to the exact scene resource snapshot used by the frame plan.",
                nameof(resources));
        }

        RenderPassPlan skyPass = ValidatePass(framePlan);
        if (skyPass.Draws.IsEmpty)
        {
            return new MapRenderOpenGlNormalCameraSkyPlan(
                framePlan,
                skyPass,
                resources,
                ImmutableArray<MapRenderOpenGlNormalCameraSkyDrawCommand>
                    .Empty);
        }
        if (skyPass.Draws.Length != resources.Bindings.Length)
        {
            throw new ArgumentException(
                "A non-empty sky pass must draw every catalog sky in exact source order.",
                nameof(framePlan));
        }

        var commands = ImmutableArray.CreateBuilder<
            MapRenderOpenGlNormalCameraSkyDrawCommand>(skyPass.Draws.Length);
        for (var drawIndex = 0;
             drawIndex < skyPass.Draws.Length;
             drawIndex++)
        {
            RenderDrawPlan draw = skyPass.Draws[drawIndex];
            MapRenderOpenGlNormalCameraSkyResourceBinding resource =
                resources.Bindings[drawIndex];
            ValidateDraw(draw, resource, framePlan.Resources, drawIndex);
            Matrix4x4 nativeWorldViewProjection =
                DecodePs3NativeWorldViewProjection(draw);
            Matrix4x4 hostViewProjection =
                OpenGlRsxClipSpaceLowering
                    .CreateDirectEditorPreviewHostViewProjectionFromPs3Native(
                        nativeWorldViewProjection);
            commands.Add(new MapRenderOpenGlNormalCameraSkyDrawCommand(
                draw,
                resource,
                hostViewProjection));
        }

        return new MapRenderOpenGlNormalCameraSkyPlan(
            framePlan,
            skyPass,
            resources,
            commands.MoveToImmutable());
    }

    private static RenderPassPlan ValidatePass(RenderFramePlan framePlan)
    {
        if (framePlan.Passes.Length < 2 ||
            framePlan.Passes[0].Identity !=
                RenderFramePlanner.NormalCameraScenePass ||
            framePlan.Passes[1].Identity !=
                RenderFramePlanner.NormalCameraSkyPass ||
            framePlan.Passes.Count(pass =>
                pass.Identity == RenderFramePlanner.NormalCameraSkyPass) != 1)
        {
            throw new ArgumentException(
                "OpenGL sky lowering requires the exact ordered normal-camera sky pass after scene-target entry.",
                nameof(framePlan));
        }

        RenderPassPlan scenePass = framePlan.Passes[0];
        RenderPassPlan skyPass = framePlan.Passes[1];
        if (skyPass.Purpose != RenderPassPurpose.Sky ||
            skyPass.Viewport != scenePass.Viewport ||
            skyPass.Scissor != scenePass.Scissor ||
            skyPass.ColorAttachments.Length != 1 ||
            skyPass.ColorAttachments[0].Attachment !=
                RenderFramePlanner.NormalCameraSceneColorAttachment ||
            skyPass.ColorAttachments[0].Load !=
                RenderAttachmentLoadRequirement.Preserve ||
            skyPass.ColorAttachments[0].Store !=
                RenderAttachmentStoreRequirement.Preserve ||
            skyPass.ColorAttachments[0].ClearValue is not null ||
            skyPass.DepthStencilAttachment is not { } depthStencil ||
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
                "OpenGL sky lowering requires exact preserve semantics for the scene color and depth-stencil attachments.",
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
                "OpenGL sky lowering requires the exact two-sample scene attachments.",
                nameof(framePlan));
        }

        return skyPass;
    }

    private static void ValidateDraw(
        RenderDrawPlan draw,
        MapRenderOpenGlNormalCameraSkyResourceBinding resource,
        RenderResourceSnapshot frameResources,
        int drawIndex)
    {
        RenderSkySubmissionSnapshot submission = resource.Submission;
        RenderPipelineDescriptor pipeline = draw.Pipeline;
        RenderShaderProgramDescriptor program = pipeline.ShaderProgram;
        if (submission.SceneOrdinal != drawIndex ||
            draw.SortKey != new RenderDrawSortKey(0, 0, drawIndex) ||
            draw.Identity != submission.DrawIdentity ||
            draw.Geometry.Geometry != submission.GeometryIdentity ||
            draw.Geometry.VertexLayout !=
                submission.VertexLayoutIdentity ||
            draw.Geometry.Topology != RenderPrimitiveTopology.TriangleList ||
            draw.Geometry.FirstVertex != 0 ||
            draw.Geometry.VertexCount != resource.Geometry.VertexCount ||
            draw.Geometry.FirstIndex != 0 ||
            draw.Geometry.IndexCount != resource.Geometry.IndexCount ||
            draw.Geometry.IndexFormat != RenderIndexFormat.Unsigned32 ||
            resource.Geometry.CoordinateSpace !=
                RenderGeometryCoordinateSpace.Render ||
            draw.Instances is not null ||
            draw.Range != new RenderDrawRange(
                firstIndex: 0,
                resource.Geometry.IndexCount,
                baseVertex: 0,
                firstInstance: 0,
                instanceCount: 1) ||
            draw.PickingIdentity is not null ||
            draw.PreviewRequirement !=
                (RenderPreviewDrawRequirement.VisibleInPreview |
                 RenderPreviewDrawRequirement.EligibleForScreenshot))
        {
            throw InvalidDraw(drawIndex, "draw identity, order, geometry, or range differs from the exact sky contract");
        }
        if (pipeline.Identity != RenderFramePlanner.SkyPipelineIdentity ||
            pipeline.VertexLayout != submission.VertexLayoutIdentity ||
            pipeline.Topology != RenderPrimitiveTopology.TriangleList ||
            !pipeline.ColorAttachmentFormats.SequenceEqual(
                [RenderAttachmentPixelFormat.Rgba8Unorm]) ||
            pipeline.DepthStencilAttachmentFormat !=
                RenderAttachmentPixelFormat.Depth24Stencil8 ||
            pipeline.Multisample !=
                RenderMultisampleStateDescriptor.Ps3Target2 ||
            !pipeline.FixedState.ContentEquals(RenderFixedStatePresets.SkyV1))
        {
            throw InvalidDraw(drawIndex, "pipeline or fixed state differs from the exact sky contract");
        }
        if (!program.ContentEquals(RenderFramePlanner.SkyShaderProgram) ||
            !draw.Material.ContentEquals(RenderFramePlanner.SkyMaterial))
        {
            throw InvalidDraw(drawIndex, "shader ABI, program, or material differs from the exact sky contract");
        }

        RenderTextureSamplerBinding? texture = draw.Textures
            .SingleOrDefault();
        RenderDynamicConstantBinding? constants = draw.DynamicConstants
            .SingleOrDefault();
        if (texture is null ||
            texture.BindingPoint != RenderFramePlanner.SkyTextureBindingPoint ||
            texture.Texture != submission.TextureIdentity ||
            texture.Sampler != submission.SamplerIdentity ||
            texture.TextureDimension != RenderTextureDimension.TextureCube ||
            constants is null ||
            constants.Identity != RenderFramePlanner
                .SkyWorldViewProjectionConstantIdentity ||
            constants.BindingPoint != RenderFramePlanner
                .SkyWorldViewProjectionBindingPoint ||
            constants.Encoding !=
                RenderDynamicConstantEncoding.Matrix4x4Rows ||
            constants.CoordinateSpace !=
                RenderShaderCoordinateSpace.Ps3Native ||
            constants.Values.Length != 4)
        {
            throw InvalidDraw(drawIndex, "resource bindings differ from the exact sky shader ABI");
        }

        if (!ReferenceEquals(
                frameResources.RequireVertexLayout(
                    submission.VertexLayoutIdentity),
                resource.VertexLayout) ||
            !ReferenceEquals(
                frameResources.RequireGeometry(submission.GeometryIdentity),
                resource.Geometry) ||
            !ReferenceEquals(
                frameResources.RequireTexture(submission.TextureIdentity),
                resource.Texture) ||
            !ReferenceEquals(
                frameResources.RequireSampler(submission.SamplerIdentity),
                resource.Sampler))
        {
            throw InvalidDraw(drawIndex, "OpenGL resource resolution does not retain the frame snapshot objects");
        }
    }

    private static Matrix4x4 DecodePs3NativeWorldViewProjection(
        RenderDrawPlan draw)
    {
        RenderDynamicConstantBinding binding =
            draw.DynamicConstants.Single();
        ImmutableArray<Vector4> rows = binding.Values;
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
        $"OpenGL sky draw {drawIndex} is invalid: {reason}.",
        "framePlan");
}
