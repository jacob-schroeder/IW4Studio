namespace IW4.Render.Techniques;

/// <summary>
/// Complete front/back stencil state emitted by the PS3 material-state writer.
/// BackFaceStateIsIndependent retains the raw stateBits1 0x80 branch instead
/// of inferring independence by comparing the normalized face values.
/// </summary>
public readonly record struct StencilState(
    bool Enabled,
    bool BackFaceStateIsIndependent,
    StencilFaceState Front,
    StencilFaceState Back)
{
    public static StencilState Disabled { get; } = new(
        Enabled: false,
        BackFaceStateIsIndependent: false,
        Front: StencilFaceState.KeepAlways,
        Back: StencilFaceState.KeepAlways);
}
