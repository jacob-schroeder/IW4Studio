using IW4.Game.Assets.Physics;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

/// <summary>Detached editable fields of one named preset. The two native flag bytes
/// and padding retain their source values when editing an unrelated field.</summary>
public sealed class PhysPresetDraft
{
    internal PhysPresetDraft(PhysPresetAsset asset)
    {
        Name = asset.Name;
        SoundClassIndex = asset.SoundClassIndex;
        Mass = asset.Mass;
        Bounce = asset.Bounce;
        Friction = asset.Friction;
        BulletForceScale = asset.BulletForceScale;
        ExplosiveForceScale = asset.ExplosiveForceScale;
        PiecesSpreadFraction = asset.PiecesSpreadFraction;
        PiecesUpwardVelocity = asset.PiecesUpwardVelocity;
        SndAliasPrefix = asset.SndAliasPrefix;
        TempDefaultToCylinder = asset.TempDefaultToCylinder;
        PerSurfaceSndAlias = asset.PerSurfaceSndAlias;
        Pad2A = asset.Pad2A;
    }

    public string? Name { get; }
    public int SoundClassIndex { get; set; }
    public float Mass { get; set; }
    public float Bounce { get; set; }
    public float Friction { get; set; }
    public float BulletForceScale { get; set; }
    public float ExplosiveForceScale { get; set; }
    public float PiecesSpreadFraction { get; set; }
    public float PiecesUpwardVelocity { get; set; }
    public string? SndAliasPrefix { get; set; }
    public byte TempDefaultToCylinder { get; set; }
    public byte PerSurfaceSndAlias { get; set; }
    private ushort Pad2A { get; }

    public PhysPresetDraft Clone() => (PhysPresetDraft)MemberwiseClone();

    public static string? ValidateSoundPrefix(string? value) =>
        value?.Any(ch => ch == '\0' || ch > byte.MaxValue) == true
            ? "Enter a Latin-1 sound alias prefix without NUL characters."
            : null;

    internal PhysPresetAsset ToAsset(PhysPresetAsset? imported = null) => new()
    {
        Name = Name, SoundClassIndex = SoundClassIndex,
        Mass = Mass, Bounce = Bounce, Friction = Friction,
        BulletForceScale = BulletForceScale, ExplosiveForceScale = ExplosiveForceScale,
        PiecesSpreadFraction = PiecesSpreadFraction, PiecesUpwardVelocity = PiecesUpwardVelocity,
        SndAliasPrefix = SndAliasPrefix,
        SndAliasPrefixPointer = imported is not null && imported.SndAliasPrefix == SndAliasPrefix
            ? imported.SndAliasPrefixPointer
            : default,
        PerSurfaceSndAlias = PerSurfaceSndAlias,
        TempDefaultToCylinder = TempDefaultToCylinder, Pad2A = Pad2A
    };

    public bool Matches(PhysPresetDraft other) =>
        Name == other.Name && SoundClassIndex == other.SoundClassIndex && SndAliasPrefix == other.SndAliasPrefix &&
        TempDefaultToCylinder == other.TempDefaultToCylinder && PerSurfaceSndAlias == other.PerSurfaceSndAlias &&
        Pad2A == other.Pad2A && SameBits(Mass, other.Mass) && SameBits(Bounce, other.Bounce) &&
        SameBits(Friction, other.Friction) && SameBits(BulletForceScale, other.BulletForceScale) &&
        SameBits(ExplosiveForceScale, other.ExplosiveForceScale) &&
        SameBits(PiecesSpreadFraction, other.PiecesSpreadFraction) && SameBits(PiecesUpwardVelocity, other.PiecesUpwardVelocity);

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    // Apply only fields edited since the tab was populated. Other workspace
    // changes, null strings, flag bytes and original NaN payloads survive.
    public void ApplyEdits(PhysPresetDraft before, PhysPresetDraft after)
    {
        if (before.SoundClassIndex != after.SoundClassIndex) SoundClassIndex = after.SoundClassIndex;
        if (!SameBits(before.Mass, after.Mass)) Mass = after.Mass;
        if (!SameBits(before.Bounce, after.Bounce)) Bounce = after.Bounce;
        if (!SameBits(before.Friction, after.Friction)) Friction = after.Friction;
        if (!SameBits(before.BulletForceScale, after.BulletForceScale)) BulletForceScale = after.BulletForceScale;
        if (!SameBits(before.ExplosiveForceScale, after.ExplosiveForceScale)) ExplosiveForceScale = after.ExplosiveForceScale;
        if (!SameBits(before.PiecesSpreadFraction, after.PiecesSpreadFraction)) PiecesSpreadFraction = after.PiecesSpreadFraction;
        if (!SameBits(before.PiecesUpwardVelocity, after.PiecesUpwardVelocity)) PiecesUpwardVelocity = after.PiecesUpwardVelocity;
        if (before.SndAliasPrefix != after.SndAliasPrefix) SndAliasPrefix = after.SndAliasPrefix;
        if (before.TempDefaultToCylinder != after.TempDefaultToCylinder) TempDefaultToCylinder = after.TempDefaultToCylinder;
        if (before.PerSurfaceSndAlias != after.PerSurfaceSndAlias) PerSurfaceSndAlias = after.PerSurfaceSndAlias;
    }
}

internal sealed class PhysPresetAdapter : AssetAuthoringAdapter<PhysPresetAsset, PhysPresetDraft>
{
    public override XAssetType AssetType => XAssetType.PhysPreset;
    public override PhysPresetDraft CreateDraft(PhysPresetAsset asset) => new(asset);
    public override PhysPresetDraft CloneDraft(PhysPresetDraft draft) => draft.Clone();
    public override PhysPresetAsset CreateDefinition(PhysPresetDraft draft) => draft.ToAsset();
    public override bool SemanticallyEquals(PhysPresetDraft left, PhysPresetDraft right) => left.Matches(right);

    public override IReadOnlyList<AssetValidationIssue> Validate(PhysPresetDraft draft) =>
        PhysPresetDraft.ValidateSoundPrefix(draft.SndAliasPrefix) is { } error
            ? [new AssetValidationIssue(nameof(PhysPresetAsset.SndAliasPrefix), error, AssetValidationSeverity.Error)]
            : [];
}
