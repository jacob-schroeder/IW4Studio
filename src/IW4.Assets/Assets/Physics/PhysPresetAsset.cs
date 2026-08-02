using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Physics;

public sealed class PhysPresetAsset : BaseAsset
{
    public const int SerializedSize = 0x2C;

    // 0x00: XString name.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04..0x18: physical material parameters.
    public int Type { get; init; }
    public float Mass { get; init; }
    public float Bounce { get; init; }
    public float Friction { get; init; }
    public float BulletForceScale { get; init; }
    public float ExplosiveForceScale { get; init; }

    // 0x1C: XString sound-alias prefix.
    public XPointer<string> SndAliasPrefixPointer { get; init; }
    public string? SndAliasPrefix { get; init; }

    // 0x20..0x29: piece-spread and sound-selection parameters.
    public float PiecesSpreadFraction { get; init; }
    public float PiecesUpwardVelocity { get; init; }
    public byte TempDefaultToCylinder { get; init; }
    public byte PerSurfaceSndAlias { get; init; }

    // 0x2A: preserved PS3 serialized padding.
    public ushort Pad2A { get; init; }
}
