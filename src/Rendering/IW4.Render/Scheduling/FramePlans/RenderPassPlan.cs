using System.Collections.Immutable;

namespace IW4.Render.Scheduling.FramePlans;

public sealed class RenderAttachmentDescriptor
{
    public RenderAttachmentDescriptor(
        RenderAttachmentIdentity identity,
        RenderAttachmentRole role,
        MapRenderPixelExtent extent,
        RenderAttachmentPixelFormat pixelFormat,
        int sampleCount)
    {
        ValidateIdentity(identity, nameof(identity));
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (extent.Width <= 0 || extent.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(extent));
        if (!Enum.IsDefined(pixelFormat))
            throw new ArgumentOutOfRangeException(nameof(pixelFormat));
        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if ((role == RenderAttachmentRole.Color &&
             pixelFormat is not
                (RenderAttachmentPixelFormat.Rgba8Unorm or
                 RenderAttachmentPixelFormat.Rgba8Srgb)) ||
            (role == RenderAttachmentRole.DepthStencil &&
             pixelFormat != RenderAttachmentPixelFormat.Depth24Stencil8) ||
            (role == RenderAttachmentRole.Picking &&
             pixelFormat != RenderAttachmentPixelFormat.R32UnsignedInteger))
        {
            throw new ArgumentException(
                "Attachment role and pixel format are incompatible.",
                nameof(pixelFormat));
        }

        Identity = identity;
        Role = role;
        Extent = extent;
        PixelFormat = pixelFormat;
        SampleCount = sampleCount;
    }

    public RenderAttachmentIdentity Identity { get; }

    public RenderAttachmentRole Role { get; }

    public MapRenderPixelExtent Extent { get; }

    public RenderAttachmentPixelFormat PixelFormat { get; }

    public int SampleCount { get; }

    internal static void ValidateIdentity(
        RenderAttachmentIdentity identity,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identity.Value))
            throw new ArgumentException("An attachment identity is required.", parameterName);
    }
}

public sealed class RenderColorAttachmentPlan
{
    public RenderColorAttachmentPlan(
        RenderAttachmentIdentity attachment,
        RenderAttachmentLoadRequirement load,
        RenderAttachmentStoreRequirement store,
        RenderAttachmentClearValue? clearValue)
    {
        RenderAttachmentDescriptor.ValidateIdentity(
            attachment,
            nameof(attachment));
        if (!Enum.IsDefined(load))
            throw new ArgumentOutOfRangeException(nameof(load));
        if (!Enum.IsDefined(store))
            throw new ArgumentOutOfRangeException(nameof(store));
        if ((load == RenderAttachmentLoadRequirement.Clear) !=
            clearValue.HasValue)
        {
            throw new ArgumentException(
                "Color clear value presence must exactly match the clear load requirement.",
                nameof(clearValue));
        }
        if (clearValue is { } typedClear)
            typedClear.Validate(nameof(clearValue));

        Attachment = attachment;
        Load = load;
        Store = store;
        ClearValue = clearValue;
    }

    public RenderAttachmentIdentity Attachment { get; }

    public RenderAttachmentLoadRequirement Load { get; }

    public RenderAttachmentStoreRequirement Store { get; }

    public RenderAttachmentClearValue? ClearValue { get; }
}

public sealed class RenderDepthStencilAttachmentPlan
{
    public RenderDepthStencilAttachmentPlan(
        RenderAttachmentIdentity attachment,
        RenderAttachmentLoadRequirement depthLoad,
        RenderAttachmentStoreRequirement depthStore,
        float? clearDepth,
        RenderAttachmentLoadRequirement stencilLoad,
        RenderAttachmentStoreRequirement stencilStore,
        byte? clearStencil)
    {
        RenderAttachmentDescriptor.ValidateIdentity(
            attachment,
            nameof(attachment));
        if (!Enum.IsDefined(depthLoad))
            throw new ArgumentOutOfRangeException(nameof(depthLoad));
        if (!Enum.IsDefined(depthStore))
            throw new ArgumentOutOfRangeException(nameof(depthStore));
        if (!Enum.IsDefined(stencilLoad))
            throw new ArgumentOutOfRangeException(nameof(stencilLoad));
        if (!Enum.IsDefined(stencilStore))
            throw new ArgumentOutOfRangeException(nameof(stencilStore));
        if ((depthLoad == RenderAttachmentLoadRequirement.Clear) !=
            clearDepth.HasValue)
        {
            throw new ArgumentException(
                "Depth clear value presence must exactly match the clear load requirement.",
                nameof(clearDepth));
        }
        if (clearDepth is { } depth &&
            (!float.IsFinite(depth) || depth is < 0f or > 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(clearDepth));
        }
        if ((stencilLoad == RenderAttachmentLoadRequirement.Clear) !=
            clearStencil.HasValue)
        {
            throw new ArgumentException(
                "Stencil clear value presence must exactly match the clear load requirement.",
                nameof(clearStencil));
        }

        Attachment = attachment;
        DepthLoad = depthLoad;
        DepthStore = depthStore;
        ClearDepth = clearDepth;
        StencilLoad = stencilLoad;
        StencilStore = stencilStore;
        ClearStencil = clearStencil;
    }

    public RenderAttachmentIdentity Attachment { get; }

    public RenderAttachmentLoadRequirement DepthLoad { get; }

    public RenderAttachmentStoreRequirement DepthStore { get; }

    public float? ClearDepth { get; }

    public RenderAttachmentLoadRequirement StencilLoad { get; }

    public RenderAttachmentStoreRequirement StencilStore { get; }

    public byte? ClearStencil { get; }
}

public sealed class RenderPassPlan
{
    public RenderPassPlan(
        RenderPassIdentity identity,
        RenderPassPurpose purpose,
        RenderViewport viewport,
        RenderScissor scissor,
        ImmutableArray<RenderColorAttachmentPlan> colorAttachments,
        RenderDepthStencilAttachmentPlan? depthStencilAttachment,
        ImmutableArray<RenderDrawPlan> draws)
    {
        if (string.IsNullOrWhiteSpace(identity.Value))
            throw new ArgumentException("A render-pass identity is required.", nameof(identity));
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewport));
        if (scissor.Width <= 0 || scissor.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(scissor));
        if (colorAttachments.IsDefault ||
            ContainsNull(colorAttachments))
        {
            throw new ArgumentException(
                "Color attachments must be initialized and non-null.",
                nameof(colorAttachments));
        }
        if (draws.IsDefault || ContainsNull(draws))
        {
            throw new ArgumentException(
                "Draws must be initialized and non-null.",
                nameof(draws));
        }
        if (HasDuplicateColorAttachment(colorAttachments))
        {
            throw new ArgumentException(
                "A pass cannot bind one color attachment more than once.",
                nameof(colorAttachments));
        }
        if (depthStencilAttachment is { } depthStencil &&
            colorAttachments.Any(color =>
                color.Attachment == depthStencil.Attachment))
        {
            throw new ArgumentException(
                "A pass attachment cannot be both color and depth-stencil.");
        }
        if (HasDuplicateDrawIdentity(draws))
        {
            throw new ArgumentException(
                "Draw identities must be unique within a pass.",
                nameof(draws));
        }
        if (HasDuplicateDrawSourceOrdinal(draws))
        {
            throw new ArgumentException(
                "Draw source ordinals must be unique within a pass.",
                nameof(draws));
        }
        for (var drawIndex = 1; drawIndex < draws.Length; drawIndex++)
        {
            if (CompareSortKeys(
                    draws[drawIndex - 1].SortKey,
                    draws[drawIndex].SortKey) > 0)
            {
                throw new ArgumentException(
                    "Draw order must already follow ascending sort-key order.",
                    nameof(draws));
            }
        }

        Identity = identity;
        Purpose = purpose;
        Viewport = viewport;
        Scissor = scissor;
        ColorAttachments = colorAttachments;
        DepthStencilAttachment = depthStencilAttachment;
        Draws = draws;
    }

    public RenderPassIdentity Identity { get; }

    public RenderPassPurpose Purpose { get; }

    public RenderViewport Viewport { get; }

    public RenderScissor Scissor { get; }

    public ImmutableArray<RenderColorAttachmentPlan> ColorAttachments
        { get; }

    public RenderDepthStencilAttachmentPlan? DepthStencilAttachment
        { get; }

    public ImmutableArray<RenderDrawPlan> Draws { get; }

    private static int CompareSortKeys(
        RenderDrawSortKey left,
        RenderDrawSortKey right)
    {
        int primary = left.Primary.CompareTo(right.Primary);
        if (primary != 0)
            return primary;
        int secondary = left.Secondary.CompareTo(right.Secondary);
        if (secondary != 0)
            return secondary;
        return left.SourceOrdinal.CompareTo(right.SourceOrdinal);
    }

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

    private static bool HasDuplicateColorAttachment(
        ImmutableArray<RenderColorAttachmentPlan> attachments)
    {
        for (var index = 0; index < attachments.Length; index++)
        {
            RenderAttachmentIdentity identity =
                attachments[index].Attachment;
            for (int candidate = index + 1;
                 candidate < attachments.Length;
                 candidate++)
            {
                if (attachments[candidate].Attachment == identity)
                    return true;
            }
        }

        return false;
    }

    private static bool HasDuplicateDrawIdentity(
        ImmutableArray<RenderDrawPlan> draws)
    {
        const int PairwiseValidationLimit = 8;
        if (draws.Length > PairwiseValidationLimit)
        {
            var identities = new HashSet<RenderSemanticIdentity>();
            for (var index = 0; index < draws.Length; index++)
            {
                if (!identities.Add(draws[index].Identity))
                    return true;
            }

            return false;
        }

        for (var index = 0; index < draws.Length; index++)
        {
            RenderSemanticIdentity identity = draws[index].Identity;
            for (int candidate = index + 1;
                 candidate < draws.Length;
                 candidate++)
            {
                if (draws[candidate].Identity == identity)
                    return true;
            }
        }

        return false;
    }

    private static bool HasDuplicateDrawSourceOrdinal(
        ImmutableArray<RenderDrawPlan> draws)
    {
        const int PairwiseValidationLimit = 8;
        if (draws.Length > PairwiseValidationLimit)
        {
            var sourceOrdinals = new HashSet<long>();
            for (var index = 0; index < draws.Length; index++)
            {
                if (!sourceOrdinals.Add(
                        draws[index].SortKey.SourceOrdinal))
                {
                    return true;
                }
            }

            return false;
        }

        for (var index = 0; index < draws.Length; index++)
        {
            long sourceOrdinal = draws[index].SortKey.SourceOrdinal;
            for (int candidate = index + 1;
                 candidate < draws.Length;
                 candidate++)
            {
                if (draws[candidate].SortKey.SourceOrdinal == sourceOrdinal)
                    return true;
            }
        }

        return false;
    }
}
