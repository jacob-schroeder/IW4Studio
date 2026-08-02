namespace IW4.Render.Scheduling.Lifecycle;

/// <summary>
/// PS3 render-target dimension rule selected by the target-table producer.
/// </summary>
public enum MapRenderNormalCameraTargetDimensions
{
    /// <summary>
    /// Backing width is twice display width and backing height is display
    /// height. The exposed logical viewport remains display sized.
    /// </summary>
    DoubleWidthBackingDisplayLogical = 0,

    /// <summary>Backing and logical dimensions both match the display.</summary>
    FullDisplay = 1,

    /// <summary>
    /// Backing and logical dimensions are display width/height shifted down
    /// once through the engine producer's integer shift/clamp path.
    /// </summary>
    HalfDisplayShiftClamp = 2
}

/// <summary>
/// Exact positive backing and logical dimensions produced for one
/// normal-camera target from a positive display size.
/// </summary>
public readonly record struct MapRenderNormalCameraTargetExtent
{
    public MapRenderNormalCameraTargetExtent(
        int backingWidth,
        int backingHeight,
        int logicalWidth,
        int logicalHeight)
    {
        if (backingWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(backingWidth));
        if (backingHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(backingHeight));
        if (logicalWidth <= 0 || logicalWidth > backingWidth)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        if (logicalHeight <= 0 || logicalHeight > backingHeight)
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));

        BackingWidth = backingWidth;
        BackingHeight = backingHeight;
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
    }

    public int BackingWidth { get; }

    public int BackingHeight { get; }

    public int LogicalWidth { get; }

    public int LogicalHeight { get; }
}

/// <summary>
/// Render-target ids used by the PS3 normal-camera lifecycle.
/// </summary>
public enum MapRenderNormalCameraTargetKind
{
    Framebuffer = 1,
    Scene = 2,
    ResolvedPostSun = 3,
    ResolvedScene = 4,
    FloatZ = 5,
    HalfParticles = 6,
    ProcessedFloatZ = 8
}

/// <summary>
/// Exact PS3 single-channel floating-point target used by the normal-camera
/// FloatZ production path.
/// </summary>
public sealed record MapRenderNormalCameraFloatZTargetPlan
{
    public MapRenderNormalCameraFloatZTargetPlan(
        MapRenderNormalCameraTargetKind kind)
    {
        if (kind is not MapRenderNormalCameraTargetKind.FloatZ and
            not MapRenderNormalCameraTargetKind.ProcessedFloatZ)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "Only target 5 FloatZ and target 8 ProcessedFloatZ use this exact target contract.");
        }

        Kind = kind;
    }

    public MapRenderNormalCameraTargetKind Kind { get; }

    public int TargetId => (int)Kind;

    public string Ps3Name => Kind switch
    {
        MapRenderNormalCameraTargetKind.FloatZ =>
            "R_RENDERTARGET_FLOAT_Z",
        MapRenderNormalCameraTargetKind.ProcessedFloatZ =>
            "R_RENDERTARGET_PROCESSED_FLOAT_Z",
        _ => throw new InvalidOperationException(
            $"Unknown FloatZ target {Kind}.")
    };

    public uint Ps3RowAddress =>
        MapRenderNormalCameraTargetPlan.Ps3TargetTableAddress +
        (uint)TargetId * MapRenderNormalCameraTargetPlan.Ps3TargetRowSize;

    public byte RawProgramImageSlot => Kind switch
    {
        MapRenderNormalCameraTargetKind.FloatZ => 2,
        MapRenderNormalCameraTargetKind.ProcessedFloatZ => 5,
        _ => throw new InvalidOperationException(
            $"Unknown FloatZ target {Kind}.")
    };

    /// <summary>
    /// Packed X32_FLOAT image setup presented to the PS3 program-image
    /// producer before its final format-byte decoration.
    /// </summary>
    public uint RawImageSetupFormat => 0x01aa_e49c;

    public uint RawImageSetupFlags => 0x0000_0003;

    public byte RawImageSetupTextureFormatByte => 0x9c;

    /// <summary>Final linear X32_FLOAT program-image format byte.</summary>
    public byte RawImageFormatByte => 0xbc;

    /// <summary><c>CELL_GCM_SURFACE_F_X32</c>.</summary>
    public byte RawColorFormat => 13;

    public MapRenderNormalCameraTargetDimensions Dimensions =>
        MapRenderNormalCameraTargetDimensions.HalfDisplayShiftClamp;

    public byte RawAntialias => 0;

    public int Ps3SurfaceSampleCount => 1;

    public uint RawTargetSetAntiAliasingControl => 0xffff_0000;

    public MapRenderNormalCameraTargetExtent ResolveExtent(
        int displayWidth,
        int displayHeight)
    {
        if (displayWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayWidth));
        if (displayHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayHeight));

        int width = Math.Max(1, displayWidth >> 1);
        int height = Math.Max(1, displayHeight >> 1);
        return new MapRenderNormalCameraTargetExtent(
            width,
            height,
            width,
            height);
    }
}

/// <summary>
/// One PS3 <c>0x48</c>-byte render-target row used by the normal-camera path.
/// Runtime image pointers and color-A offset/pitch are deliberately not
/// invented by this structural plan.
/// </summary>
public sealed record MapRenderNormalCameraTargetPlan
{
    public const uint Ps3TargetOwnerAddress = 0x022a_ca00;
    public const uint Ps3TargetTableAddress = 0x022a_ca80;
    public const uint Ps3TargetRowSize = 0x48;

    public MapRenderNormalCameraTargetPlan(
        MapRenderNormalCameraTargetKind kind,
        string ps3Name,
        byte rawProgramImageSlot,
        MapRenderNormalCameraTargetDimensions dimensions,
        MapRenderNormalCameraTargetKind? initialAliasOf = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind is MapRenderNormalCameraTargetKind.FloatZ or
            MapRenderNormalCameraTargetKind.ProcessedFloatZ)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "FloatZ targets require MapRenderNormalCameraFloatZTargetPlan.");
        }
        if (string.IsNullOrWhiteSpace(ps3Name))
            throw new ArgumentException("A PS3 render-target name is required.", nameof(ps3Name));
        if (!Enum.IsDefined(dimensions))
            throw new ArgumentOutOfRangeException(nameof(dimensions));
        if (initialAliasOf is not null && !Enum.IsDefined(initialAliasOf.Value))
            throw new ArgumentOutOfRangeException(nameof(initialAliasOf));
        if (initialAliasOf == kind)
            throw new ArgumentException("A render target cannot alias itself.", nameof(initialAliasOf));

        Kind = kind;
        Ps3Name = ps3Name;
        RawProgramImageSlot = rawProgramImageSlot;
        Dimensions = dimensions;
        InitialAliasOf = initialAliasOf;
    }

    public MapRenderNormalCameraTargetKind Kind { get; }

    public int TargetId => (int)Kind;

    public string Ps3Name { get; }

    public uint Ps3RowAddress => Ps3TargetTableAddress + (uint)TargetId * Ps3TargetRowSize;

    /// <summary>
    /// Raw program-image slot passed to the PS3 image allocator. This identifies
    /// the renderer-owned image object; it is not a color-format selector.
    /// </summary>
    public byte RawProgramImageSlot { get; }

    /// <summary>
    /// Packed PS3 image setup format used by the four target producers. Its low
    /// byte is <c>CELL_GCM_TEXTURE_A8R8G8B8</c> and the setup flags add the
    /// linear-layout bit, producing image format byte <c>0xA5</c>.
    /// </summary>
    public uint RawImageSetupFormat => 0x01aa_e485;

    public uint RawImageSetupFlags => 0x0200_0003;

    public byte RawImageFormatByte => 0xa5;

    public MapRenderNormalCameraTargetDimensions Dimensions { get; }

    /// <summary>Raw PS3 target dimension family.</summary>
    public byte RawDimensionFamily => Dimensions switch
    {
        MapRenderNormalCameraTargetDimensions
            .DoubleWidthBackingDisplayLogical => 3,
        MapRenderNormalCameraTargetDimensions.FullDisplay => 2,
        MapRenderNormalCameraTargetDimensions.HalfDisplayShiftClamp => 2,
        _ => throw new InvalidOperationException(
            $"Unknown target dimension rule {Dimensions}.")
    };

    /// <summary>PS3 target-dimension arithmetic-right-shift count.</summary>
    public byte RawDimensionShift => Dimensions ==
        MapRenderNormalCameraTargetDimensions.HalfDisplayShiftClamp
            ? (byte)1
            : (byte)0;

    /// <summary>
    /// Target whose complete <c>0x48</c>-byte row was copied into this target
    /// during initialization. This represents an initial shared resource only.
    /// </summary>
    public MapRenderNormalCameraTargetKind? InitialAliasOf { get; }

    public bool IsInitialAlias => InitialAliasOf is not null;

    public byte RawSurfaceType => 1;

    /// <summary>
    /// Final row value after the family-3 target producer patches Scene from
    /// center-one-sample to diagonal-centered-two-sample. The same path halves
    /// row <c>+0x38</c>, so Scene keeps a doubled sample backing but exposes a
    /// display-width surface clip.
    /// </summary>
    public byte RawAntialias => Kind == MapRenderNormalCameraTargetKind.Scene
        ? (byte)3
        : (byte)0;

    public int Ps3SurfaceSampleCount => RawAntialias switch
    {
        0 => 1,
        3 => 2,
        _ => throw new InvalidOperationException(
            $"Unsupported PS3 surface antialias value {RawAntialias}.")
    };

    public bool SurfaceClipUsesLogicalExtent => true;

    /// <summary>
    /// Raw <c>NV4097_SET_ANTI_ALIASING_CONTROL</c> value emitted after this
    /// target row is selected. Target 2 enables multisample processing; all
    /// other targets disable it. Every target retains the full 16-bit sample
    /// mask and disables alpha-to-coverage and alpha-to-one.
    /// </summary>
    public uint RawTargetSetAntiAliasingControl => Kind ==
        MapRenderNormalCameraTargetKind.Scene
            ? 0xffff_0001u
            : 0xffff_0000u;

    public bool TargetSetMultisampleEnabled =>
        (RawTargetSetAntiAliasingControl & 0x1u) != 0;

    public bool TargetSetAlphaToCoverageEnabled =>
        (RawTargetSetAntiAliasingControl & 0x10u) != 0;

    public bool TargetSetAlphaToOneEnabled =>
        (RawTargetSetAntiAliasingControl & 0x100u) != 0;

    public ushort TargetSetSampleMask =>
        checked((ushort)(RawTargetSetAntiAliasingControl >> 16));

    public byte RawColorTargetMask => 1;

    public int ColorAttachmentCount => 1;

    public bool UsesMultipleRenderTargets => false;

    public bool ColorLocationsAreAllZero => true;

    public bool SecondaryColorOffsetsAreZero => true;

    public bool SecondaryColorPitchesAre64 => true;

    public byte RawDepthFormat => 2;

    public byte RawDepthLocation => 0;

    /// <summary>
    /// Placeholder written by PS3 target-row construction before any
    /// target-specific depth allocation is attached.
    /// </summary>
    public uint RawConstructorDepthOffset => 0;

    /// <summary>
    /// Placeholder written by PS3 target-row construction before any
    /// target-specific depth allocation is attached.
    /// </summary>
    public uint RawConstructorDepthPitch => 64;

    /// <summary>
    /// Final statically known depth offset. Scene and half-particle rows are
    /// patched from runtime local-memory allocations, so their exact offsets
    /// are intentionally not invented here.
    /// </summary>
    public uint? RawDepthOffset => HasDedicatedDepthAllocation
        ? null
        : RawConstructorDepthOffset;

    /// <summary>
    /// Final statically known depth pitch. Scene and half-particle rows receive
    /// the runtime result of <c>cellGcmGetTiledPitchSize</c>.
    /// </summary>
    public uint? RawDepthPitch => HasDedicatedDepthAllocation
        ? null
        : RawConstructorDepthPitch;

    public uint Ps3DepthLocationFieldAddress => Ps3RowAddress + 0x2d;

    public uint Ps3DepthOffsetFieldAddress => Ps3RowAddress + 0x30;

    public uint Ps3DepthPitchFieldAddress => Ps3RowAddress + 0x34;

    /// <summary>
    /// PS3 initialization gives Scene and HalfParticles dedicated depth
    /// allocations and patches their row depth tuples.
    /// </summary>
    public bool HasDedicatedDepthAllocation => Kind is
        MapRenderNormalCameraTargetKind.Scene or
        MapRenderNormalCameraTargetKind.HalfParticles;

    public bool DepthFieldsArePatchedAfterRowConstruction =>
        HasDedicatedDepthAllocation;

    /// <summary>
    /// Packed allocator format whose low byte is
    /// <c>CELL_GCM_TEXTURE_DEPTH24_D8</c> (<c>0x90</c>).
    /// </summary>
    public uint? RawDepthAllocationSetupFormat =>
        HasDedicatedDepthAllocation ? 0x01aa_e490u : null;

    public byte? RawDepthAllocationTextureFormatByte =>
        HasDedicatedDepthAllocation ? (byte)0x90 : null;

    /// <summary>
    /// Separate A8R8G8B8 linear program-image view over the dedicated depth
    /// memory. This is a shader-readable view, not the allocation's format.
    /// </summary>
    public byte? RawDepthSamplingViewProgramImageSlot => Kind switch
    {
        MapRenderNormalCameraTargetKind.Scene => 6,
        MapRenderNormalCameraTargetKind.HalfParticles => 4,
        _ => null
    };

    public uint? RawDepthSamplingViewSetupFormat =>
        HasDedicatedDepthAllocation ? 0x01aa_e485u : null;

    public uint? RawDepthSamplingViewSetupFlags =>
        HasDedicatedDepthAllocation ? 0x0200_0003u : null;

    public bool DedicatedDepthExtentMatchesColorBacking =>
        HasDedicatedDepthAllocation;

    public ushort SurfaceX => 0;

    public ushort SurfaceY => 0;

    /// <summary><c>CELL_GCM_SURFACE_A8R8G8B8</c>.</summary>
    public byte RawColorFormat => 8;

    /// <summary>
    /// Replays the PS3 dimension-family selection, arithmetic right shift,
    /// per-axis minimum-one clamp, and target-2 logical-width adjustment.
    /// </summary>
    public MapRenderNormalCameraTargetExtent ResolveExtent(
        int displayWidth,
        int displayHeight)
    {
        if (displayWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayWidth));
        if (displayHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayHeight));

        return Dimensions switch
        {
            MapRenderNormalCameraTargetDimensions
                    .DoubleWidthBackingDisplayLogical =>
                new MapRenderNormalCameraTargetExtent(
                    checked(displayWidth * 2),
                    displayHeight,
                    displayWidth,
                    displayHeight),
            MapRenderNormalCameraTargetDimensions.FullDisplay =>
                new MapRenderNormalCameraTargetExtent(
                    displayWidth,
                    displayHeight,
                    displayWidth,
                    displayHeight),
            MapRenderNormalCameraTargetDimensions.HalfDisplayShiftClamp =>
                new MapRenderNormalCameraTargetExtent(
                    Math.Max(1, displayWidth >> 1),
                    Math.Max(1, displayHeight >> 1),
                    Math.Max(1, displayWidth >> 1),
                    Math.Max(1, displayHeight >> 1)),
            _ => throw new InvalidOperationException(
                $"Unknown target dimension rule {Dimensions}.")
        };
    }
}

/// <summary>
/// Bit layout consumed by the PS3 <c>NV4097_CLEAR_SURFACE</c> method.
/// </summary>
[Flags]
public enum MapRenderSceneClearSurfaceMask : byte
{
    None = 0x00,
    Depth = 0x01,
    Stencil = 0x02,
    Red = 0x10,
    Green = 0x20,
    Blue = 0x40,
    Alpha = 0x80,
    Rgba = Red | Green | Blue | Alpha
}

/// <summary>
/// Arguments for the one scene-target clear that precedes the PS3
/// normal-camera core phases. The clear color remains runtime supplied and is
/// deliberately not inferred here.
/// </summary>
public sealed record MapRenderSceneTargetClearPlan
{
    private const MapRenderSceneClearSurfaceMask DefinedSurfaceMask =
        MapRenderSceneClearSurfaceMask.Depth |
        MapRenderSceneClearSurfaceMask.Stencil |
        MapRenderSceneClearSurfaceMask.Rgba;

    public MapRenderSceneTargetClearPlan(
        int targetId,
        MapRenderSceneClearSurfaceMask surfaceMask,
        float depth,
        byte stencil)
    {
        if (targetId < 0)
            throw new ArgumentOutOfRangeException(nameof(targetId));
        if ((surfaceMask & ~DefinedSurfaceMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceMask));
        if (!float.IsFinite(depth) || depth is < 0.0f or > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(depth));

        TargetId = targetId;
        SurfaceMask = surfaceMask;
        Depth = depth;
        Stencil = stencil;
    }

    public int TargetId { get; }

    public MapRenderSceneClearSurfaceMask SurfaceMask { get; }

    public byte RawSurfaceMask => (byte)SurfaceMask;

    public float Depth { get; }

    public byte Stencil { get; }

}
