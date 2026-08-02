using IW4.Render.Scheduling.Lifecycle;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Targets;

/// <summary>
/// Replays target-2 feedback invalidation, bind, viewport/surface clip,
/// anti-aliasing, and target-local clear once exact host sample equivalence is
/// available. All blockers and pure/resource/context facts are checked before
/// the first GL query or mutation.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraSceneTargetReplayer
{
    private readonly IMapRenderSilkNormalCameraSceneTargetReplayApi _api;
    private readonly string _contextIdentity;
    private readonly int _maximumCombinedTextureImageUnits;
    private readonly int _maximumSampleMaskWords;
    private readonly int _ownerThreadId;
    private readonly uint[] _textureBindingScratch = new uint[16];
    private long _lastClearedFrameRevision = -1;
    private MapRenderOpenGlNormalCameraDepthStencilTargetResource?
        _lastClearedResource;

    public MapRenderOpenGlNormalCameraSceneTargetReplayer(
        GL gl,
        string contextIdentity)
        : this(new SilkMapRenderOpenGlNormalCameraSceneTargetReplayApi(
            gl,
            contextIdentity))
    {
    }

    internal MapRenderOpenGlNormalCameraSceneTargetReplayer(
        GL gl,
        string contextIdentity,
        SilkOpenGlStateShadow stateShadow)
        : this(new SilkMapRenderOpenGlNormalCameraSceneTargetReplayApi(
            gl,
            contextIdentity,
            stateShadow))
    {
    }

    internal MapRenderOpenGlNormalCameraSceneTargetReplayer(
        IMapRenderSilkNormalCameraSceneTargetReplayApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(api.ContextIdentity);
        if (api.MaximumCombinedTextureImageUnits < 16)
        {
            throw new NotSupportedException(
                "The PS3 target feedback contract requires 16 texture-image units.");
        }
        if (api.MaximumSampleMaskWords < 1)
        {
            throw new NotSupportedException(
                "The PS3 anti-aliasing control requires sample-mask word zero.");
        }

        _api = api;
        _contextIdentity = api.ContextIdentity;
        _maximumCombinedTextureImageUnits =
            api.MaximumCombinedTextureImageUnits;
        _maximumSampleMaskWords = api.MaximumSampleMaskWords;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public string ContextIdentity
    {
        get
        {
            EnsureOwnerThread();
            return _contextIdentity;
        }
    }

    public MapRenderOpenGlNormalCameraSceneTargetReplayResult Replay(
        MapRenderOpenGlNormalCameraSceneTargetPlan plan)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(plan);
        ValidateCurrentContext(plan);
        MapRenderOpenGlNormalCameraSceneTargetPlan rebuilt =
            MapRenderOpenGlNormalCameraSceneTargetPlanner.LowerPs3(
                plan.Resources,
                plan.FramePlan,
                plan.ClearColorResult);
        ValidateExactPlan(plan, rebuilt);

        if (plan.FrameRevision < _lastClearedFrameRevision)
        {
            throw new ArgumentException(
                "The scene target plan belongs to an older frame revision.",
                nameof(plan));
        }
        if (plan.FrameRevision == _lastClearedFrameRevision)
        {
            if (!ReferenceEquals(plan.Binding.Resource, _lastClearedResource))
            {
                throw new ArgumentException(
                    "One frame revision cannot change its scene target resource.",
                    nameof(plan));
            }
            return MapRenderOpenGlNormalCameraSceneTargetReplayResult
                .AlreadyClearedThisFrame;
        }

        int previousActiveTextureUnit = _api.GetActiveTextureUnit();
        if (previousActiveTextureUnit < 0 ||
            previousActiveTextureUnit >= _maximumCombinedTextureImageUnits)
        {
            throw new InvalidOperationException(
                "The current GL active texture selector is outside the reported context limit.");
        }

        if (plan.TextureFeedbackSlotCount >
            _textureBindingScratch.Length)
        {
            throw new InvalidOperationException(
                "The scene target requests more feedback slots than the PS3 replay scratch covers.");
        }
        Span<uint> textureBindings = _textureBindingScratch.AsSpan(
            0,
            plan.TextureFeedbackSlotCount);
        _api.CaptureTextureUnitBindings(
            plan.ColorTextureTarget,
            textureBindings);

        bool hasFeedbackBinding = false;
        for (int textureUnit = 0;
             textureUnit < plan.TextureFeedbackSlotCount;
             textureUnit++)
        {
            if (textureBindings[textureUnit] ==
                plan.ColorTextureHandle)
            {
                hasFeedbackBinding = true;
                break;
            }
        }
        ValidateCurrentContext(plan);

        if (hasFeedbackBinding)
        {
            for (int textureUnit = 0;
                 textureUnit < plan.TextureFeedbackSlotCount;
                 textureUnit++)
            {
                if (textureBindings[textureUnit] !=
                    plan.ColorTextureHandle)
                {
                    continue;
                }
                _api.ActiveTexture(textureUnit);
                _api.BindTexture(plan.ColorTextureTarget, 0);
            }
            _api.ActiveTexture(previousActiveTextureUnit);
        }

        _api.BindDrawFramebuffer(plan.CombinedFramebufferHandle);
        _api.Viewport(
            plan.ViewportX,
            plan.ViewportY,
            plan.ViewportWidth,
            plan.ViewportHeight);
        _api.Scissor(
            plan.HostEffectiveScissorX,
            plan.HostEffectiveScissorY,
            plan.HostEffectiveScissorWidth,
            plan.HostEffectiveScissorHeight);
        _api.SetScissorTestEnabled(
            plan.EnablesEffectiveSurfaceClipScissor);
        ReplayAntialiasing(plan.Antialiasing);

        _api.ColorMask(true, true, true, true);
        _api.DepthMask(true);
        if (plan.StencilTargetContract.FrontWriteMask !=
            plan.StencilTargetContract.BackWriteMask)
        {
            throw new InvalidOperationException(
                "Scene clear requires one complete stencil-plane write mask.");
        }
        _api.StencilMask(plan.StencilTargetContract.FrontWriteMask);
        _api.ClearColor(
            plan.ClearColor.Red,
            plan.ClearColor.Green,
            plan.ClearColor.Blue,
            plan.ClearColor.Alpha);
        _api.ClearDepth(plan.ClearDepth);
        _api.ClearStencil(plan.ClearStencil);
        _api.Clear(plan.ClearSurfaceMask);

        _lastClearedFrameRevision = plan.FrameRevision;
        _lastClearedResource = plan.Binding.Resource;
        return MapRenderOpenGlNormalCameraSceneTargetReplayResult
            .BoundAndCleared;
    }

    private void ReplayAntialiasing(
        MapRenderOpenGlNormalCameraTargetAntialiasingPlan plan)
    {
        _api.SetMultisampleEnabled(plan.MultisampleEnabled);
        _api.SetSampleMaskEnabled(true);
        _api.SampleMask(plan.HostSampleMaskWordIndex, plan.HostSampleMaskWord);
        _api.SetSampleAlphaToCoverageEnabled(
            plan.AlphaToCoverageEnabled);
        _api.SetSampleAlphaToOneEnabled(plan.AlphaToOneEnabled);
    }

    private void ValidateCurrentContext(
        MapRenderOpenGlNormalCameraSceneTargetPlan plan)
    {
        if (!string.Equals(
                plan.ContextIdentity,
                _contextIdentity,
                StringComparison.Ordinal) ||
            !string.Equals(
                _api.ContextIdentity,
                _contextIdentity,
                StringComparison.Ordinal) ||
            _api.MaximumCombinedTextureImageUnits !=
                _maximumCombinedTextureImageUnits ||
            _api.MaximumSampleMaskWords != _maximumSampleMaskWords)
        {
            throw new InvalidOperationException(
                "The scene target plan or replay API changed current GL context identity or capabilities.");
        }
    }

    private static void ValidateExactPlan(
        MapRenderOpenGlNormalCameraSceneTargetPlan supplied,
        MapRenderOpenGlNormalCameraSceneTargetPlan rebuilt)
    {
        bool exact =
            ReferenceEquals(supplied.Resources, rebuilt.Resources) &&
            ReferenceEquals(supplied.Binding, rebuilt.Binding) &&
            ReferenceEquals(supplied.FramePlan, rebuilt.FramePlan) &&
            ReferenceEquals(supplied.ScenePass, rebuilt.ScenePass) &&
            supplied.FrameRevision == rebuilt.FrameRevision &&
            supplied.ClearColorResult == rebuilt.ClearColorResult &&
            supplied.ClearColor == rebuilt.ClearColor &&
            supplied.ClearSurfaceMask == rebuilt.ClearSurfaceMask &&
            supplied.ClearDepth == rebuilt.ClearDepth &&
            supplied.ClearStencil == rebuilt.ClearStencil &&
            supplied.StencilTargetContract.Matches(
                rebuilt.StencilTargetContract) &&
            supplied.CombinedFramebufferHandle ==
                rebuilt.CombinedFramebufferHandle &&
            supplied.ColorTextureHandle == rebuilt.ColorTextureHandle &&
            supplied.ColorTextureTarget == rebuilt.ColorTextureTarget &&
            supplied.Extent == rebuilt.Extent &&
            supplied.Antialiasing == rebuilt.Antialiasing;
        if (!exact)
        {
            throw new ArgumentException(
                "The scene target plan is stale or internally inconsistent.",
                nameof(supplied));
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "OpenGL scene target replay may only run on its owning render thread.");
        }
    }
}
