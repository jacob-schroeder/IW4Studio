using System.Buffers.Binary;
using System.Collections.Immutable;

using IW4.Render.Resources;
using IW4.Render.Shaders;

namespace IW4.Render.Scheduling.FramePlans;

public readonly record struct RenderPreviewRequirements
{
    public RenderPreviewRequirements(
        bool requiresPresentation,
        bool requiresScreenshot,
        MapRenderScreenshotSource? screenshotSource)
    {
        if (screenshotSource is { } source && !Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(screenshotSource));
        if (requiresScreenshot != screenshotSource.HasValue)
        {
            throw new ArgumentException(
                "Screenshot source presence must match the screenshot requirement.",
                nameof(screenshotSource));
        }

        RequiresPresentation = requiresPresentation;
        RequiresScreenshot = requiresScreenshot;
        ScreenshotSource = screenshotSource;
    }

    public bool RequiresPresentation { get; }

    public bool RequiresScreenshot { get; }

    public MapRenderScreenshotSource? ScreenshotSource { get; }

    public static RenderPreviewRequirements None { get; } =
        new(false, false, null);

    public static RenderPreviewRequirements Presentation { get; } =
        new(true, false, null);
}

public readonly record struct RenderPickingRequirements
{
    public RenderPickingRequirements(
        RenderPickingMode mode,
        RenderAttachmentIdentity? attachment,
        RenderScissor? region)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        bool hasCompleteTarget = attachment.HasValue && region.HasValue;
        if ((mode == RenderPickingMode.None &&
             (attachment.HasValue || region.HasValue)) ||
            (mode != RenderPickingMode.None && !hasCompleteTarget))
        {
            throw new ArgumentException(
                "Picking attachment and region must both be present exactly when picking is enabled.");
        }
        if (attachment is { } attachmentIdentity)
        {
            RenderAttachmentDescriptor.ValidateIdentity(
                attachmentIdentity,
                nameof(attachment));
        }
        if (region is { } requestedRegion &&
            (requestedRegion.X < 0 ||
             requestedRegion.Y < 0 ||
             requestedRegion.Width <= 0 ||
             requestedRegion.Height <= 0 ||
             (long)requestedRegion.X + requestedRegion.Width >
                 int.MaxValue ||
             (long)requestedRegion.Y + requestedRegion.Height >
                 int.MaxValue))
        {
            throw new ArgumentException(
                "Picking requires a valid non-empty scissor region.",
                nameof(region));
        }
        if (mode == RenderPickingMode.SinglePixel &&
            region is { Width: not 1 } or { Height: not 1 })
        {
            throw new ArgumentException(
                "Single-pixel picking requires an exact 1x1 region.",
                nameof(region));
        }

        Mode = mode;
        Attachment = attachment;
        Region = region;
    }

    public RenderPickingMode Mode { get; }

    public RenderAttachmentIdentity? Attachment { get; }

    public RenderScissor? Region { get; }

    public static RenderPickingRequirements None { get; } =
        new(RenderPickingMode.None, null, null);
}

/// <summary>
/// Immutable backend-neutral rendering intent. Pass order is authoritative;
/// executors may lower and batch work but must preserve semantic ordering.
/// </summary>
public sealed class RenderFramePlan
{
    private static readonly RenderResourceSnapshot EmptyResources = new(
        Array.Empty<RenderVertexLayoutDescriptor>(),
        Array.Empty<RenderGeometryDescriptor>(),
        Array.Empty<RenderTextureDescriptor>(),
        Array.Empty<RenderSamplerDescriptor>());

    public RenderFramePlan(
        long frameRevision,
        MapRenderSurfaceExtents surfaceExtents,
        ImmutableArray<RenderAttachmentDescriptor> attachments,
        ImmutableArray<RenderPassPlan> passes,
        RenderPreviewRequirements previewRequirements,
        RenderPickingRequirements pickingRequirements,
        float animationTimeSeconds = 0f)
        : this(
            frameRevision,
            surfaceExtents,
            EmptyResources,
            attachments,
            passes,
            previewRequirements,
            pickingRequirements,
            animationTimeSeconds)
    {
    }

    public RenderFramePlan(
        long frameRevision,
        MapRenderSurfaceExtents surfaceExtents,
        RenderResourceSnapshot resources,
        ImmutableArray<RenderAttachmentDescriptor> attachments,
        ImmutableArray<RenderPassPlan> passes,
        RenderPreviewRequirements previewRequirements,
        RenderPickingRequirements pickingRequirements,
        float animationTimeSeconds = 0f)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        if (!surfaceExtents.IsValid)
            throw new ArgumentOutOfRangeException(nameof(surfaceExtents));
        if (!float.IsFinite(animationTimeSeconds) ||
            animationTimeSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(animationTimeSeconds),
                "Frame animation time must be finite and nonnegative.");
        }
        ArgumentNullException.ThrowIfNull(resources);
        if (attachments.IsDefaultOrEmpty || ContainsNull(attachments))
        {
            throw new ArgumentException(
                "Frame attachments must be initialized, non-empty, and non-null.",
                nameof(attachments));
        }
        if (passes.IsDefaultOrEmpty || ContainsNull(passes))
        {
            throw new ArgumentException(
                "Frame passes must be initialized, non-empty, and non-null.",
                nameof(passes));
        }
        if (HasDuplicateAttachmentIdentity(attachments))
        {
            throw new ArgumentException(
                "Frame attachment identities must be unique.",
                nameof(attachments));
        }
        if (HasDuplicatePassIdentity(passes))
        {
            throw new ArgumentException(
                "Frame pass identities must be unique.",
                nameof(passes));
        }

        foreach (RenderPassPlan pass in passes)
        {
            ValidatePass(pass, attachments);
        }
        ValidateAttachmentLifecycles(passes);
        ValidateDescriptorIdentityConsistency(passes);
        ValidateResourceReferences(passes, resources);
        if (pickingRequirements.Attachment is { } pickingAttachment &&
            (!TryGetAttachment(
                 attachments,
                 pickingAttachment,
                 out RenderAttachmentDescriptor? descriptor) ||
             descriptor is null ||
             descriptor.Role != RenderAttachmentRole.Picking))
        {
            throw new ArgumentException(
                "Picking requirements reference no picking attachment.",
                nameof(pickingRequirements));
        }
        if (pickingRequirements.Attachment is { } requiredPickingAttachment &&
            pickingRequirements.Region is { } pickingRegion)
        {
            TryGetAttachment(
                attachments,
                requiredPickingAttachment,
                out RenderAttachmentDescriptor? pickingDescriptor);
            if (pickingRegion.Right > pickingDescriptor!.Extent.Width ||
                pickingRegion.Bottom > pickingDescriptor.Extent.Height)
            {
                throw new ArgumentException(
                    "Picking region exceeds its attachment extent.",
                    nameof(pickingRequirements));
            }
            RenderColorAttachmentPlan? finalPickingUse =
                FindFinalColorAttachmentUse(
                    passes,
                    requiredPickingAttachment);
            if (finalPickingUse is null ||
                finalPickingUse.Store !=
                    RenderAttachmentStoreRequirement.Preserve)
            {
                throw new ArgumentException(
                    "Picking requirements require the final use of the attachment to preserve it for readback.",
                    nameof(pickingRequirements));
            }
        }

        FrameRevision = frameRevision;
        SurfaceExtents = surfaceExtents;
        Resources = resources;
        Attachments = attachments;
        Passes = passes;
        PreviewRequirements = previewRequirements;
        PickingRequirements = pickingRequirements;
        AnimationTimeSeconds = animationTimeSeconds == 0f
            ? 0f
            : animationTimeSeconds;
    }

    public long FrameRevision { get; }

    public MapRenderSurfaceExtents SurfaceExtents { get; }

    public RenderResourceSnapshot Resources { get; }

    public ImmutableArray<RenderAttachmentDescriptor> Attachments { get; }

    public ImmutableArray<RenderPassPlan> Passes { get; }

    public RenderPreviewRequirements PreviewRequirements { get; }

    public RenderPickingRequirements PickingRequirements { get; }

    /// <summary>
    /// Backend-neutral effective animation time for this exact frame.
    /// </summary>
    public float AnimationTimeSeconds { get; }

    private static bool ContainsNull<T>(ImmutableArray<T> values)
        where T : class
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] is null)
                return true;
        }

        return false;
    }

    private static bool HasDuplicateAttachmentIdentity(
        ImmutableArray<RenderAttachmentDescriptor> attachments)
    {
        for (var index = 0; index < attachments.Length; index++)
        {
            RenderAttachmentIdentity identity = attachments[index].Identity;
            for (int candidate = index + 1;
                 candidate < attachments.Length;
                 candidate++)
            {
                if (attachments[candidate].Identity == identity)
                    return true;
            }
        }

        return false;
    }

    private static bool HasDuplicatePassIdentity(
        ImmutableArray<RenderPassPlan> passes)
    {
        for (var index = 0; index < passes.Length; index++)
        {
            RenderPassIdentity identity = passes[index].Identity;
            for (int candidate = index + 1;
                 candidate < passes.Length;
                 candidate++)
            {
                if (passes[candidate].Identity == identity)
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetAttachment(
        ImmutableArray<RenderAttachmentDescriptor> attachments,
        RenderAttachmentIdentity identity,
        out RenderAttachmentDescriptor? descriptor)
    {
        for (var index = 0; index < attachments.Length; index++)
        {
            RenderAttachmentDescriptor candidate = attachments[index];
            if (candidate.Identity == identity)
            {
                descriptor = candidate;
                return true;
            }
        }

        descriptor = null;
        return false;
    }

    private static RenderColorAttachmentPlan? FindFinalColorAttachmentUse(
        ImmutableArray<RenderPassPlan> passes,
        RenderAttachmentIdentity identity)
    {
        for (int passIndex = passes.Length - 1;
             passIndex >= 0;
             passIndex--)
        {
            ImmutableArray<RenderColorAttachmentPlan> colors =
                passes[passIndex].ColorAttachments;
            for (int colorIndex = colors.Length - 1;
                 colorIndex >= 0;
                 colorIndex--)
            {
                if (colors[colorIndex].Attachment == identity)
                    return colors[colorIndex];
            }
        }

        return null;
    }

    private static void ValidatePass(
        RenderPassPlan pass,
        ImmutableArray<RenderAttachmentDescriptor> attachments)
    {
        RenderAttachmentDescriptor? reference = null;
        foreach (RenderColorAttachmentPlan color in pass.ColorAttachments)
        {
            if (!TryGetAttachment(
                    attachments,
                    color.Attachment,
                    out RenderAttachmentDescriptor? descriptor) ||
                descriptor is null ||
                descriptor.Role is not
                    (RenderAttachmentRole.Color or
                     RenderAttachmentRole.Picking))
            {
                throw new ArgumentException(
                    $"Pass {pass.Identity} references no color attachment {color.Attachment}.");
            }
            reference ??= descriptor;
            ValidateCompatible(reference, descriptor, pass.Identity);
            ValidateClearValue(descriptor, color, pass.Identity);
        }

        if (pass.DepthStencilAttachment is { } depthStencil)
        {
            if (!TryGetAttachment(
                    attachments,
                    depthStencil.Attachment,
                    out RenderAttachmentDescriptor? descriptor) ||
                descriptor is null ||
                descriptor.Role != RenderAttachmentRole.DepthStencil)
            {
                throw new ArgumentException(
                    $"Pass {pass.Identity} references no depth-stencil attachment {depthStencil.Attachment}.");
            }
            reference ??= descriptor;
            ValidateCompatible(reference, descriptor, pass.Identity);
        }

        if (reference is null)
        {
            throw new ArgumentException(
                $"Pass {pass.Identity} has no attachments.");
        }
        if (pass.Viewport.Right > reference.Extent.Width ||
            pass.Viewport.Bottom > reference.Extent.Height)
        {
            throw new ArgumentException(
                $"Pass {pass.Identity} viewport exceeds attachment extent.");
        }
        if (pass.Scissor.Right > reference.Extent.Width ||
            pass.Scissor.Bottom > reference.Extent.Height)
        {
            throw new ArgumentException(
                $"Pass {pass.Identity} scissor exceeds attachment extent.");
        }

        RenderAttachmentPixelFormat? depthStencilFormat = null;
        if (pass.DepthStencilAttachment is { } passDepthStencil)
        {
            TryGetAttachment(
                attachments,
                passDepthStencil.Attachment,
                out RenderAttachmentDescriptor? descriptor);
            depthStencilFormat = descriptor!.PixelFormat;
        }
        for (var drawIndex = 0;
             drawIndex < pass.Draws.Length;
             drawIndex++)
        {
            RenderDrawPlan draw = pass.Draws[drawIndex];
            if (draw.Pipeline.SampleCount != reference.SampleCount ||
                !PipelineColorFormatsMatchPass(
                    draw.Pipeline.ColorAttachmentFormats,
                    pass.ColorAttachments,
                    attachments) ||
                draw.Pipeline.DepthStencilAttachmentFormat !=
                    depthStencilFormat)
            {
                throw new ArgumentException(
                    $"Draw {draw.Identity} pipeline is incompatible with pass {pass.Identity} attachments.");
            }
        }
    }

    private static void ValidateAttachmentLifecycles(
        ImmutableArray<RenderPassPlan> passes)
    {
        for (var passIndex = 0; passIndex < passes.Length; passIndex++)
        {
            RenderPassPlan pass = passes[passIndex];
            for (var colorIndex = 0;
                 colorIndex < pass.ColorAttachments.Length;
                 colorIndex++)
            {
                RenderColorAttachmentPlan color =
                    pass.ColorAttachments[colorIndex];
                if (color.Load == RenderAttachmentLoadRequirement.Preserve &&
                    TryFindPreviousColorStore(
                        passes,
                        passIndex,
                        color.Attachment,
                        out RenderAttachmentStoreRequirement previousStore) &&
                    previousStore !=
                        RenderAttachmentStoreRequirement.Preserve)
                {
                    throw new ArgumentException(
                        $"Pass {pass.Identity} cannot preserve color attachment " +
                        $"{color.Attachment} after its previous use discarded it.",
                        nameof(passes));
                }
            }

            if (pass.DepthStencilAttachment is not { } depthStencil)
                continue;

            if (TryFindPreviousDepthStencilStore(
                    passes,
                    passIndex,
                    depthStencil.Attachment,
                    out RenderAttachmentStoreRequirement previousDepthStore,
                    out RenderAttachmentStoreRequirement previousStencilStore))
            {
                if (depthStencil.DepthLoad ==
                        RenderAttachmentLoadRequirement.Preserve &&
                    previousDepthStore !=
                        RenderAttachmentStoreRequirement.Preserve)
                {
                    throw new ArgumentException(
                        $"Pass {pass.Identity} cannot preserve depth attachment " +
                        $"{depthStencil.Attachment} after its previous use discarded it.",
                        nameof(passes));
                }
                if (depthStencil.StencilLoad ==
                        RenderAttachmentLoadRequirement.Preserve &&
                    previousStencilStore !=
                        RenderAttachmentStoreRequirement.Preserve)
                {
                    throw new ArgumentException(
                        $"Pass {pass.Identity} cannot preserve stencil attachment " +
                        $"{depthStencil.Attachment} after its previous use discarded it.",
                        nameof(passes));
                }
            }
        }
    }

    private static bool TryFindPreviousColorStore(
        ImmutableArray<RenderPassPlan> passes,
        int beforePassIndex,
        RenderAttachmentIdentity attachment,
        out RenderAttachmentStoreRequirement store)
    {
        for (int passIndex = beforePassIndex - 1;
             passIndex >= 0;
             passIndex--)
        {
            ImmutableArray<RenderColorAttachmentPlan> colors =
                passes[passIndex].ColorAttachments;
            for (int colorIndex = colors.Length - 1;
                 colorIndex >= 0;
                 colorIndex--)
            {
                if (colors[colorIndex].Attachment == attachment)
                {
                    store = colors[colorIndex].Store;
                    return true;
                }
            }
        }

        store = default;
        return false;
    }

    private static bool TryFindPreviousDepthStencilStore(
        ImmutableArray<RenderPassPlan> passes,
        int beforePassIndex,
        RenderAttachmentIdentity attachment,
        out RenderAttachmentStoreRequirement depthStore,
        out RenderAttachmentStoreRequirement stencilStore)
    {
        for (int passIndex = beforePassIndex - 1;
             passIndex >= 0;
             passIndex--)
        {
            if (passes[passIndex].DepthStencilAttachment is
                    { } depthStencil &&
                depthStencil.Attachment == attachment)
            {
                depthStore = depthStencil.DepthStore;
                stencilStore = depthStencil.StencilStore;
                return true;
            }
        }

        depthStore = default;
        stencilStore = default;
        return false;
    }

    private static bool PipelineColorFormatsMatchPass(
        ImmutableArray<RenderAttachmentPixelFormat> pipelineFormats,
        ImmutableArray<RenderColorAttachmentPlan> passAttachments,
        ImmutableArray<RenderAttachmentDescriptor> attachments)
    {
        if (pipelineFormats.Length != passAttachments.Length)
            return false;

        for (var index = 0; index < pipelineFormats.Length; index++)
        {
            TryGetAttachment(
                attachments,
                passAttachments[index].Attachment,
                out RenderAttachmentDescriptor? descriptor);
            if (pipelineFormats[index] != descriptor!.PixelFormat)
                return false;
        }

        return true;
    }

    private static void ValidateClearValue(
        RenderAttachmentDescriptor attachment,
        RenderColorAttachmentPlan plan,
        RenderPassIdentity pass)
    {
        if (plan.ClearValue is not { } clear)
            return;

        RenderAttachmentClearValueKind requiredKind =
            attachment.PixelFormat switch
            {
                RenderAttachmentPixelFormat.Rgba8Unorm or
                RenderAttachmentPixelFormat.Rgba8Srgb =>
                    RenderAttachmentClearValueKind.NormalizedColor,
                RenderAttachmentPixelFormat.R32UnsignedInteger =>
                    RenderAttachmentClearValueKind.UnsignedInteger,
                _ => throw new ArgumentException(
                    $"Pass {pass} uses a color clear with non-color format " +
                    $"{attachment.PixelFormat}.")
            };
        if (clear.Kind != requiredKind)
        {
            throw new ArgumentException(
                $"Pass {pass} clear representation {clear.Kind} is " +
                $"incompatible with {attachment.PixelFormat}.");
        }
    }

    private static void ValidateCompatible(
        RenderAttachmentDescriptor expected,
        RenderAttachmentDescriptor actual,
        RenderPassIdentity pass)
    {
        if (expected.Extent != actual.Extent ||
            expected.SampleCount != actual.SampleCount)
        {
            throw new ArgumentException(
                $"Pass {pass} attachments must share extent and sample count.");
        }
    }

    private static void ValidateDescriptorIdentityConsistency(
        ImmutableArray<RenderPassPlan> passes)
    {
        // Most editor frames contain only sky and one world draw. Validate
        // those plans pairwise without allocating five short-lived
        // dictionaries. Larger diagnostic plans retain the linear-time
        // dictionary path.
        const int PairwiseValidationLimit = 8;
        var drawCount = 0;
        for (var passIndex = 0; passIndex < passes.Length; passIndex++)
        {
            drawCount = checked(
                drawCount + passes[passIndex].Draws.Length);
            if (drawCount > PairwiseValidationLimit)
            {
                break;
            }
        }

        if (drawCount <= 1)
            return;
        if (drawCount <= PairwiseValidationLimit)
        {
            ValidateDescriptorIdentityConsistencyPairwise(passes);
            return;
        }

        var pipelines = new Dictionary<RenderSemanticIdentity,
            RenderPipelineDescriptor>();
        var programs = new Dictionary<RenderSemanticIdentity,
            RenderShaderProgramDescriptor>();
        var shaderAbis = new Dictionary<RenderShaderAbiIdentity,
            RenderShaderAbiDescriptor>();
        var fixedStates = new Dictionary<RenderSemanticIdentity,
            RenderFixedStateDescriptor>();
        var materials = new Dictionary<RenderSemanticIdentity,
            RenderMaterialDescriptor>();

        for (var passIndex = 0; passIndex < passes.Length; passIndex++)
        {
            ImmutableArray<RenderDrawPlan> draws = passes[passIndex].Draws;
            for (var drawIndex = 0; drawIndex < draws.Length; drawIndex++)
            {
                RenderDrawPlan draw = draws[drawIndex];
                RequireConsistent(
                    pipelines,
                    draw.Pipeline.Identity,
                    draw.Pipeline,
                    PipelineEquivalent,
                    "pipeline");
                RequireConsistent(
                    programs,
                    draw.Pipeline.ShaderProgram.Identity,
                    draw.Pipeline.ShaderProgram,
                    static (left, right) => left.ContentEquals(right),
                    "shader program");
                RequireConsistentShaderAbi(
                    shaderAbis,
                    draw.Pipeline.ShaderProgram.Abi);
                RequireConsistent(
                    fixedStates,
                    draw.Pipeline.FixedState.Identity,
                    draw.Pipeline.FixedState,
                    static (left, right) => left.ContentEquals(right),
                    "fixed state");
                RequireConsistent(
                    materials,
                    draw.Material.Identity,
                    draw.Material,
                    static (left, right) => left.ContentEquals(right),
                    "material");
            }
        }
    }

    private static void ValidateDescriptorIdentityConsistencyPairwise(
        ImmutableArray<RenderPassPlan> passes)
    {
        for (var passIndex = 0; passIndex < passes.Length; passIndex++)
        {
            ImmutableArray<RenderDrawPlan> draws = passes[passIndex].Draws;
            for (var drawIndex = 0;
                 drawIndex < draws.Length;
                 drawIndex++)
            {
                RenderDrawPlan draw = draws[drawIndex];
                for (var previousPassIndex = 0;
                     previousPassIndex <= passIndex;
                     previousPassIndex++)
                {
                    ImmutableArray<RenderDrawPlan> previousDraws =
                        passes[previousPassIndex].Draws;
                    int previousDrawCount =
                        previousPassIndex == passIndex
                            ? drawIndex
                            : previousDraws.Length;
                    for (var previousDrawIndex = 0;
                         previousDrawIndex < previousDrawCount;
                         previousDrawIndex++)
                    {
                        RequireConsistentDescriptorPair(
                            previousDraws[previousDrawIndex],
                            draw);
                    }
                }
            }
        }
    }

    private static void RequireConsistentDescriptorPair(
        RenderDrawPlan left,
        RenderDrawPlan right)
    {
        if (left.Pipeline.Identity == right.Pipeline.Identity &&
            !PipelineEquivalent(left.Pipeline, right.Pipeline))
        {
            throw new ArgumentException(
                $"Semantic pipeline identity {right.Pipeline.Identity} describes conflicting state.",
                "passes");
        }
        if (left.Pipeline.ShaderProgram.Identity ==
                right.Pipeline.ShaderProgram.Identity &&
            !left.Pipeline.ShaderProgram.ContentEquals(
                right.Pipeline.ShaderProgram))
        {
            throw new ArgumentException(
                $"Semantic shader program identity {right.Pipeline.ShaderProgram.Identity} describes conflicting state.",
                "passes");
        }
        if (left.Pipeline.ShaderProgram.Abi.Identity ==
                right.Pipeline.ShaderProgram.Abi.Identity &&
            !left.Pipeline.ShaderProgram.Abi.ContentEquals(
                right.Pipeline.ShaderProgram.Abi))
        {
            throw new ArgumentException(
                $"Shader ABI identity {right.Pipeline.ShaderProgram.Abi.Identity} describes conflicting bindings.",
                "passes");
        }
        if (left.Pipeline.FixedState.Identity ==
                right.Pipeline.FixedState.Identity &&
            !left.Pipeline.FixedState.ContentEquals(
                right.Pipeline.FixedState))
        {
            throw new ArgumentException(
                $"Semantic fixed state identity {right.Pipeline.FixedState.Identity} describes conflicting state.",
                "passes");
        }
        if (left.Material.Identity == right.Material.Identity &&
            !left.Material.ContentEquals(right.Material))
        {
            throw new ArgumentException(
                $"Semantic material identity {right.Material.Identity} describes conflicting state.",
                "passes");
        }
    }

    private static void RequireConsistentShaderAbi(
        Dictionary<RenderShaderAbiIdentity, RenderShaderAbiDescriptor>
            byIdentity,
        RenderShaderAbiDescriptor descriptor)
    {
        if (byIdentity.TryGetValue(
                descriptor.Identity,
                out RenderShaderAbiDescriptor? existing))
        {
            if (existing is null ||
                !existing.ContentEquals(descriptor))
            {
                throw new ArgumentException(
                    $"Shader ABI identity {descriptor.Identity} describes conflicting bindings.",
                    "passes");
            }
            return;
        }

        byIdentity.Add(descriptor.Identity, descriptor);
    }

    private static void ValidateResourceReferences(
        ImmutableArray<RenderPassPlan> passes,
        RenderResourceSnapshot resources)
    {
        for (var passIndex = 0; passIndex < passes.Length; passIndex++)
        {
            ImmutableArray<RenderDrawPlan> draws = passes[passIndex].Draws;
            for (var drawIndex = 0; drawIndex < draws.Length; drawIndex++)
            {
                RenderDrawPlan draw = draws[drawIndex];
                try
                {
                    RenderGeometryDescriptor geometry =
                        resources.RequireGeometry(draw.Geometry.Geometry);
                    RenderVertexLayoutDescriptor layout =
                        resources.RequireVertexLayout(
                            draw.Geometry.VertexLayout);
                    if (geometry.VertexLayout != layout.Identity ||
                        geometry.Topology != draw.Geometry.Topology ||
                        geometry.IndexFormat != draw.Geometry.IndexFormat ||
                        checked(draw.Geometry.FirstVertex +
                                draw.Geometry.VertexCount) >
                            geometry.VertexCount ||
                        checked(draw.Geometry.FirstIndex +
                                draw.Geometry.IndexCount) >
                            geometry.IndexCount)
                    {
                        throw new ArgumentException(
                            $"Draw {draw.Identity} geometry slice is incompatible with resource {geometry.Identity}.",
                            nameof(resources));
                    }
                    ValidateIndexedVertexBounds(draw, geometry, resources);
                    ValidateInstanceBounds(draw, resources);

                    foreach (RenderTextureSamplerBinding binding in draw.Textures)
                    {
                        RenderTextureDescriptor texture =
                            resources.RequireTexture(binding.Texture);
                        resources.RequireSampler(binding.Sampler);
                        if (texture.Dimension != binding.TextureDimension)
                        {
                            throw new ArgumentException(
                                $"Draw {draw.Identity} texture binding shape differs from resource {texture.Identity}.",
                                nameof(resources));
                        }
                    }
                }
                catch (KeyNotFoundException failure)
                {
                    throw new ArgumentException(
                        $"Draw {draw.Identity} references a resource outside the frame snapshot.",
                        nameof(resources),
                        failure);
                }
            }
        }
    }

    private static void ValidateInstanceBounds(
        RenderDrawPlan draw,
        RenderResourceSnapshot resources)
    {
        if (draw.Instances is not { } slice)
            return;

        RenderInstanceDescriptor descriptor =
            resources.RequireInstances(slice.Instances);
        RenderSemanticIdentity pipelineLayoutIdentity =
            draw.Pipeline.InstanceLayout ?? throw new ArgumentException(
                $"Draw {draw.Identity} has instance data without a pipeline instance layout.",
                nameof(resources));
        RenderInstanceLayoutDescriptor layout =
            resources.RequireInstanceLayout(pipelineLayoutIdentity);
        long sliceEnd = (long)slice.FirstInstance + slice.InstanceCount;
        long rangeEnd = (long)draw.Range.FirstInstance +
            draw.Range.InstanceCount;
        if (descriptor.Layout != layout.Identity ||
            descriptor.StrideBytes != layout.StrideBytes ||
            !string.Equals(
                descriptor.LayoutContentDigest,
                layout.ContentDigest,
                StringComparison.Ordinal) ||
            sliceEnd > descriptor.InstanceCount ||
            draw.Range.FirstInstance < slice.FirstInstance ||
            rangeEnd > sliceEnd ||
            rangeEnd > descriptor.InstanceCount)
        {
            throw new ArgumentException(
                $"Draw {draw.Identity} instance slice is incompatible with resource {descriptor.Identity}.",
                nameof(resources));
        }
    }

    private static void ValidateIndexedVertexBounds(
        RenderDrawPlan draw,
        RenderGeometryDescriptor geometry,
        RenderResourceSnapshot resources)
    {
        // The immutable resource constructor already validates every index in
        // the overwhelmingly common whole-resource draw. Avoid turning frame
        // planning into an O(total geometry indices) steady-state operation.
        if (draw.Geometry.FirstVertex == 0 &&
            draw.Geometry.VertexCount == geometry.VertexCount &&
            draw.Geometry.FirstIndex == 0 &&
            draw.Geometry.IndexCount == geometry.IndexCount &&
            draw.Range.FirstIndex == 0 &&
            draw.Range.IndexCount == geometry.IndexCount &&
            draw.Range.BaseVertex == 0)
        {
            return;
        }

        int indexStride = geometry.IndexFormat switch
        {
            RenderIndexFormat.Unsigned16 => sizeof(ushort),
            RenderIndexFormat.Unsigned32 => sizeof(uint),
            _ => throw new ArgumentOutOfRangeException(
                nameof(geometry.IndexFormat))
        };
        long firstAllowedVertex = draw.Geometry.FirstVertex;
        long endAllowedVertex = checked(
            firstAllowedVertex + draw.Geometry.VertexCount);
        int endIndex = checked(
            draw.Range.FirstIndex + draw.Range.IndexCount);
        ReadOnlySpan<byte> indices = geometry.IndexPayload.AsSpan();
        for (int index = draw.Range.FirstIndex; index < endIndex; index++)
        {
            int byteOffset = checked(index * indexStride);
            uint relativeVertex = geometry.IndexFormat ==
                    RenderIndexFormat.Unsigned16
                ? BinaryPrimitives.ReadUInt16LittleEndian(
                    indices.Slice(byteOffset, indexStride))
                : BinaryPrimitives.ReadUInt32LittleEndian(
                    indices.Slice(byteOffset, indexStride));
            long resolvedVertex = checked(
                (long)draw.Range.BaseVertex + relativeVertex);
            if (resolvedVertex < firstAllowedVertex ||
                resolvedVertex >= endAllowedVertex ||
                resolvedVertex >= geometry.VertexCount)
            {
                throw new ArgumentException(
                    $"Draw {draw.Identity} index {index} resolves to vertex " +
                    $"{resolvedVertex}, outside geometry slice " +
                    $"[{firstAllowedVertex}, {endAllowedVertex}).",
                    nameof(resources));
            }
        }
    }

    private static void RequireConsistent<TDescriptor>(
        Dictionary<RenderSemanticIdentity, TDescriptor> byIdentity,
        RenderSemanticIdentity identity,
        TDescriptor descriptor,
        Func<TDescriptor, TDescriptor, bool> equivalent,
        string kind)
        where TDescriptor : class
    {
        if (byIdentity.TryGetValue(identity, out TDescriptor? existing))
        {
            if (existing is null || !equivalent(existing, descriptor))
            {
                throw new ArgumentException(
                    $"Semantic {kind} identity {identity} describes conflicting state.",
                    "passes");
            }
            return;
        }

        byIdentity.Add(identity, descriptor);
    }

    private static bool PipelineEquivalent(
        RenderPipelineDescriptor left,
        RenderPipelineDescriptor right) =>
        left.Identity == right.Identity &&
        left.ShaderProgram.ContentEquals(right.ShaderProgram) &&
        left.VertexLayout == right.VertexLayout &&
        left.InstanceLayout == right.InstanceLayout &&
        left.FixedState.ContentEquals(right.FixedState) &&
        left.Topology == right.Topology &&
        left.ColorAttachmentFormats.SequenceEqual(
            right.ColorAttachmentFormats) &&
        left.DepthStencilAttachmentFormat ==
            right.DepthStencilAttachmentFormat &&
        left.Multisample == right.Multisample;

}
