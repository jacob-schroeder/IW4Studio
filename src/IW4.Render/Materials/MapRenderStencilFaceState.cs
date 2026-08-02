namespace IW4.Render.Materials;

/// <summary>
/// Numeric RSX stencil payloads for one face. Symbolic API enum names follow
/// platform terminology.
/// </summary>
public readonly record struct MapRenderStencilFaceState(
    uint Function,
    int Reference,
    uint CompareMask,
    uint FailOperation,
    uint DepthFailOperation,
    uint PassOperation)
{
    public static MapRenderStencilFaceState KeepAlways { get; } = new(
        Function: 0x0207,
        Reference: 0,
        CompareMask: 0xff,
        FailOperation: 0x1e00,
        DepthFailOperation: 0x1e00,
        PassOperation: 0x1e00);
}
