namespace IW4.Game.Assets.Fx;

/// <summary>Packed PS3 effect-element flags and coordinate selectors at +0x00.</summary>
public readonly record struct FxElemFlags(int RawValue)
{
    // Bit 0 and bits 16 through 23 have no observed stock use or mapped consumer.
    public int UnmappedBits => RawValue & 0x00ff0001;
    public bool SpawnRelativeToEffect => (RawValue & 0x02) != 0;
    public bool SpawnFrustumCull => (RawValue & 0x04) != 0;
    public bool RunnerUsesRandomRotation => (RawValue & 0x08) != 0;
    public FxSpawnOffsetMode SpawnOffsetMode => (RawValue & 0x30) switch
    {
        0 => FxSpawnOffsetMode.None,
        0x10 => FxSpawnOffsetMode.Sphere,
        _ => FxSpawnOffsetMode.Cylinder
    };
    public FxRunRelativeTo RunRelativeTo => (FxRunRelativeTo)((RawValue >> 6) & 3);
    public bool HasLocalVelocityGraph => (RawValue & 0x01000000) != 0;
    public bool HasWorldVelocityGraph => (RawValue & 0x02000000) != 0;
    public bool UsesCollision => (RawValue & 0x0100) != 0;
    public bool DiesOnTouch => (RawValue & 0x0200) != 0;
    public bool DrawPastFog => (RawValue & 0x0400) != 0;
    public bool DrawWithViewmodel => (RawValue & 0x0800) != 0;
    public bool BlocksSight => (RawValue & 0x1000) != 0;
    public bool DrawInThermalViewOnly => (RawValue & 0x2000) != 0;
    public bool TrailOrientedByVelocity => (RawValue & 0x4000) != 0;
    public bool EmitOrientedByElement => (RawValue & 0x8000) != 0;
    public bool HasGravity => (RawValue & 0x04000000) != 0;
    public bool UsesModelPhysics => (RawValue & 0x08000000) != 0;
    public bool HasNonuniformScale => (RawValue & 0x10000000) != 0;
    public FxCloudShape CloudShape => (FxCloudShape)(((uint)RawValue >> 29) & 3);
    public bool FountainDisablesCollision => (RawValue & int.MinValue) != 0;
}
