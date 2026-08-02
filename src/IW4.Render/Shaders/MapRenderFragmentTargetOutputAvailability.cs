namespace IW4.Render.Shaders;

/// <summary>
/// Immutable relationship between the bound RSX surface-color target and the
/// draw buffers exposed by the host framebuffer.
/// </summary>
public sealed record MapRenderFragmentTargetOutputAvailability
{
    public MapRenderFragmentTargetOutputAvailability(
        byte rawPs3SurfaceColorTarget,
        int hostDrawBufferCount)
    {
        if (hostDrawBufferCount is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(hostDrawBufferCount));

        RawPs3SurfaceColorTarget = rawPs3SurfaceColorTarget;
        HostDrawBufferCount = hostDrawBufferCount;
        NativeOutputCount = rawPs3SurfaceColorTarget switch
        {
            0x00 => 0,
            0x01 or 0x02 => 1,
            0x13 => 2,
            0x17 => 3,
            0x1f => 4,
            _ => null
        };
    }

    /// <summary>
    /// Raw <c>CELL_GCM_SURFACE_TARGET_*</c> value from the selected surface
    /// row. The normal-camera row has a <c>+0x07</c> value of <c>1</c>
    /// (surface A).
    /// </summary>
    public byte RawPs3SurfaceColorTarget { get; }

    /// <summary>
    /// Number of fixed RSX fragment output registers made active by the raw
    /// surface target. The fixed H0/H4/H6/H8 or R0/R2/R3/R4 set is truncated
    /// to the bound target's MRT count.
    /// </summary>
    public int? NativeOutputCount { get; }

    public int HostDrawBufferCount { get; }

    public bool HasKnownNativeOutputCount => NativeOutputCount.HasValue;

    public bool IsNativeOutputActive(int colorTarget) =>
        colorTarget >= 0 &&
        NativeOutputCount is { } count &&
        colorTarget < count;

    public bool IsHostDrawBufferAvailable(int colorTarget) =>
        colorTarget >= 0 && colorTarget < HostDrawBufferCount;
}
