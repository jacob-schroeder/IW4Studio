using IW4.Render.OpenGl.Targets;
using IW4.Render.Scheduling.Lifecycle;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

/// <summary>
/// Immutable operational ownership of the Scene target's eight-bit stencil
/// plane. The contract is created only by the exact D24S8 Scene target plan;
/// material-state planning cannot invent a write mask or face convention.
/// </summary>
public sealed class MapRenderOpenGlStencilTargetContract
{
    internal MapRenderOpenGlStencilTargetContract(
        string contextIdentity,
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding binding,
        byte clearValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        ArgumentNullException.ThrowIfNull(binding);
        if (clearValue != 0)
            throw new ArgumentOutOfRangeException(nameof(clearValue));
        if (binding.Key.Target != MapRenderNormalCameraTargetKind.Scene ||
            binding.Key.HostStorageSemantics !=
                MapRenderOpenGlNormalCameraDepthStencilStorageSemantics
                    .Depth24Stencil8 ||
            binding.Resource.DepthStencilTextureHandle == 0 ||
            binding.Resource.CombinedFramebufferHandle == 0)
        {
            throw new ArgumentException(
                "Stencil execution requires the exact Scene D24S8 resource binding.",
                nameof(binding));
        }

        ContextIdentity = contextIdentity;
        Binding = binding;
        Target = binding.Key.Target;
        StorageSemantics = binding.Key.HostStorageSemantics;
        ClearValue = clearValue;
    }

    public string ContextIdentity { get; }

    public MapRenderNormalCameraTargetKind Target { get; }

    public MapRenderOpenGlNormalCameraDepthStencilStorageSemantics
        StorageSemantics { get; }

    public byte ClearValue { get; }

    /// <summary>
    /// The PS3 material writer never varies the eight-bit stencil
    /// write mask. Scene-target entry establishes the complete plane before
    /// its clear, and pass replay writes it explicitly for both faces.
    /// </summary>
    public uint FrontWriteMask => byte.MaxValue;

    public uint BackWriteMask => byte.MaxValue;

    /// <summary>
    /// PS3 methods 0x0330/0x033C are the front-face tuple. Existing
    /// RSX-to-OpenGL winding lowering makes the host front classification CW,
    /// but the face selector itself remains OpenGL Front.
    /// </summary>
    public TriangleFace Ps3FrontHostFace => TriangleFace.Front;

    /// <summary>PS3 methods 0x0350/0x035C are the back-face tuple.</summary>
    public TriangleFace Ps3BackHostFace => TriangleFace.Back;

    internal MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding
        Binding { get; }

    internal bool Matches(
        MapRenderOpenGlStencilTargetContract other) =>
        other is not null &&
        ReferenceEquals(Binding, other.Binding) &&
        string.Equals(
            ContextIdentity,
            other.ContextIdentity,
            StringComparison.Ordinal) &&
        Target == other.Target &&
        StorageSemantics == other.StorageSemantics &&
        ClearValue == other.ClearValue &&
        FrontWriteMask == other.FrontWriteMask &&
        BackWriteMask == other.BackWriteMask &&
        Ps3FrontHostFace == other.Ps3FrontHostFace &&
        Ps3BackHostFace == other.Ps3BackHostFace;
}
