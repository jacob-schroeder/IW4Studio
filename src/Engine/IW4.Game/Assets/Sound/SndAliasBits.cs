namespace IW4.Game.Assets.Sound;

/// <summary>
/// Lossless decoded view of an alias flag word. The raw signed integer keeps
/// every serialized bit; decoded meanings reflect the recovered consumers
/// and retain platform-specific uncertainty.
/// </summary>
public readonly record struct SndAliasBits(int RawValue)
{
    public const int LoopingMask = 0x0000_0001;
    public const int MasterMask = 0x0000_0002;
    public const int ConditionalVolumeScaleMask = 0x0000_0004;
    public const int DistanceGateMask = 0x0000_0040;
    public const int PlaybackTypeMask = 0x0000_0180;
    public const int PlaybackTypeShift = 7;
    public const int EntityChannelMask = 0x0000_7e00;
    public const int EntityChannelShift = 9;

    private const int MappedMask = LoopingMask |
                                   MasterMask |
                                   ConditionalVolumeScaleMask |
                                   DistanceGateMask |
                                   PlaybackTypeMask |
                                   EntityChannelMask;

    /// <summary>
    /// Whether streamed audio loops at its end. This behavior is confirmed
    /// by PS3 stream-owner 0x002FEEA0 and decoder setup 0x002FE9F0.
    /// </summary>
    public bool IsLooping => (RawValue & LoopingMask) != 0;

    /// <summary>Returns a copy with the PS3-confirmed looping bit changed.</summary>
    public SndAliasBits WithLooping(bool value) => WithFlag(LoopingMask, value);

    /// <summary>
    /// Whether playback requests master status. PS3 consumer 0x0033CC60
    /// sets request byte +0x30 from this bit or a caller override, matching
    /// the Xbox PDB's <c>SndStartAliasInfo.master</c> field.
    /// </summary>
    public bool IsMaster => (RawValue & MasterMask) != 0;

    /// <summary>
    /// Returns a copy with the PS3-confirmed master request bit changed.
    /// </summary>
    public SndAliasBits WithMaster(bool value) => WithFlag(MasterMask, value);

    /// <summary>
    /// Whether the conditional extra volume multiplier is enabled. PS3
    /// consumers use helper 0x00335530 when this bit is set and their other
    /// conditions hold. Its formula matches Xbox's named
    /// <c>SND_GetLerpedSlavePercentage</c> helper at 0x82392E78.
    /// </summary>
    public bool UsesConditionalVolumeScale =>
        (RawValue & ConditionalVolumeScaleMask) != 0;

    /// <summary>
    /// Returns a copy with conditional lerped slave-volume scaling changed.
    /// </summary>
    public SndAliasBits WithConditionalVolumeScale(bool value) =>
        WithFlag(ConditionalVolumeScaleMask, value);

    /// <summary>
    /// Whether spatial checks are forced even for a non-3D channel and
    /// sounds closer than the interpolated minimum distance are rejected.
    /// This is a behavior label for PS3 0x0033CC60, not a recovered native
    /// flag name. The maximum-distance check also applies in that branch.
    /// </summary>
    public bool EnforcesDistanceGate => (RawValue & DistanceGateMask) != 0;

    /// <summary>Returns a copy with the PS3-observed distance-gate bit changed.</summary>
    public SndAliasBits WithDistanceGate(bool value) => WithFlag(DistanceGateMask, value);

    /// <summary>
    /// Raw two-bit playback route. PS3 consumer 0x0033CC60 maps 1 to sample
    /// and 2 to stream, corroborated by Xbox symbols. Values 0 and 3 are
    /// preserved without assigning further meanings.
    /// </summary>
    public byte PlaybackType =>
        (byte)((RawValue & PlaybackTypeMask) >> PlaybackTypeShift);

    /// <summary>
    /// Returns a copy with the raw playback route changed: 1 selects sample
    /// playback and 2 selects streaming in the recovered PS3 consumer.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is greater than 3.</exception>
    public SndAliasBits WithPlaybackType(byte value)
    {
        if (value > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Playback type must be between 0 and 3.");
        }

        return new SndAliasBits((RawValue & ~PlaybackTypeMask) |
                                (value << PlaybackTypeShift));
    }

    /// <summary>
    /// Six-bit entity-channel index (0–63), extracted by PS3 0x00301D78
    /// and used by the voice-limit gate at 0x00335220.
    /// </summary>
    public byte EntityChannelIndex =>
        (byte)((RawValue & EntityChannelMask) >> EntityChannelShift);

    /// <summary>
    /// Returns a copy with the PS3-confirmed entity-channel index changed.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is greater than 63.</exception>
    public SndAliasBits WithEntityChannelIndex(byte value)
    {
        if (value > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Entity-channel index must be between 0 and 63.");
        }

        return new SndAliasBits((RawValue & ~EntityChannelMask) |
                                (value << EntityChannelShift));
    }

    /// <summary>
    /// Bits whose meanings are not established by the recovered consumers:
    /// bits 3–5 and 15–31. These remain intact in <see cref="RawValue"/>.
    /// </summary>
    public int UnmappedBits => RawValue & ~MappedMask;

    private SndAliasBits WithFlag(int mask, bool value) =>
        new(value ? RawValue | mask : RawValue & ~mask);
}
