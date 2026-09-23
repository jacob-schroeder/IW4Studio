namespace IW4.Render.Shaders;

/// <summary>Native <c>CELL_GCM_SURFACE_TARGET_*</c> selector.</summary>
public enum RsxSurfaceTarget : byte
{
    None = 0x00,
    SurfaceA = 0x01,
    SurfaceB = 0x02,
    SurfacesAB = 0x13,
    SurfacesABC = 0x17,
    SurfacesABCD = 0x1f
}

/// <summary>
/// Immutable relationship between the bound RSX surface-color target and the
/// draw buffers exposed by the host framebuffer.
/// </summary>
public sealed record FragmentTargetOutputAvailability
{
    public FragmentTargetOutputAvailability(
        RsxSurfaceTarget ps3SurfaceColorTarget,
        int hostDrawBufferCount)
    {
        if (hostDrawBufferCount is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(hostDrawBufferCount));

        Ps3SurfaceColorTarget = ps3SurfaceColorTarget;
        HostDrawBufferCount = hostDrawBufferCount;
        NativeOutputCount = ps3SurfaceColorTarget switch
        {
            RsxSurfaceTarget.None => 0,
            RsxSurfaceTarget.SurfaceA or RsxSurfaceTarget.SurfaceB => 1,
            RsxSurfaceTarget.SurfacesAB => 2,
            RsxSurfaceTarget.SurfacesABC => 3,
            RsxSurfaceTarget.SurfacesABCD => 4,
            _ => null
        };
    }

    /// <summary>
    /// Raw <c>CELL_GCM_SURFACE_TARGET_*</c> value from the selected surface
    /// row. The normal-camera row has a <c>+0x07</c> value of <c>1</c>
    /// (surface A).
    /// </summary>
    public RsxSurfaceTarget Ps3SurfaceColorTarget { get; }

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
