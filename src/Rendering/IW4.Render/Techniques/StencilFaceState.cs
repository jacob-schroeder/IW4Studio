namespace IW4.Render.Techniques;

/// <summary>
/// Numeric RSX stencil payloads for one face. Symbolic API enum names follow
/// platform terminology.
/// </summary>
public readonly record struct StencilFaceState(
    RsxCompareFunction Function,
    int Reference,
    uint CompareMask,
    RsxStencilOperation FailOperation,
    RsxStencilOperation DepthFailOperation,
    RsxStencilOperation PassOperation)
{
    public static StencilFaceState KeepAlways { get; } = new(
        Function: RsxCompareFunction.Always,
        Reference: 0,
        CompareMask: 0xff,
        FailOperation: RsxStencilOperation.Keep,
        DepthFailOperation: RsxStencilOperation.Keep,
        PassOperation: RsxStencilOperation.Keep);
}
