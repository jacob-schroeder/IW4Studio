namespace IW4.Render.Materials;

/// <summary>
/// Complete front/back stencil state emitted by the PS3 material-state writer.
/// BackFaceStateIsIndependent retains the raw stateBits1 0x80 branch instead
/// of inferring independence by comparing the normalized face values.
/// </summary>
public readonly record struct MapRenderStencilState(
    bool Enabled,
    bool BackFaceStateIsIndependent,
    MapRenderStencilFaceState Front,
    MapRenderStencilFaceState Back)
{
    public static MapRenderStencilState Disabled { get; } = new(
        Enabled: false,
        BackFaceStateIsIndependent: false,
        Front: MapRenderStencilFaceState.KeepAlways,
        Back: MapRenderStencilFaceState.KeepAlways);
}
