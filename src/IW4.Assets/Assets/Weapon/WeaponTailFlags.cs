namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponTailFlags
{
    public byte SharedAmmo { get; init; }                         // 0x654
    public byte LockonSupported { get; init; }                    // 0x655
    public byte RequireLockonToFire { get; init; }                // 0x656
    public byte BigExplosion { get; init; }                       // 0x657
    public byte NoAdsWhenMagEmpty { get; init; }                  // 0x658
    public byte AvoidDropCleanup { get; init; }                   // 0x659
    public byte InheritsPerks { get; init; }                      // 0x65A
    public byte CrosshairColorChange { get; init; }               // 0x65B
    public byte RifleBullet { get; init; }                        // 0x65C
    public byte ArmorPiercing { get; init; }                      // 0x65D
    public byte BoltAction { get; init; }                         // 0x65E
    public byte AimDownSight { get; init; }                       // 0x65F
    public byte RechamberWhileAds { get; init; }                  // 0x660
    public byte BulletExplosiveDamage { get; init; }              // 0x661
    public byte CookOffHold { get; init; }                        // 0x662
    public byte ClipOnly { get; init; }                           // 0x663
    public byte NoAmmoPickup { get; init; }                       // 0x664
    public byte AdsFireOnly { get; init; }                        // 0x665
    public byte CancelAutoHolsterWhenEmpty { get; init; }         // 0x666
    public byte DisableSwitchToWhenEmpty { get; init; }           // 0x667
    public byte SuppressAmmoReserveDisplay { get; init; }         // 0x668
    public byte LaserSightDuringNightvision { get; init; }        // 0x669
    public byte MarkableViewmodel { get; init; }                  // 0x66A
    public byte NoDualWield { get; init; }                        // 0x66B
    public byte FlipKillIcon { get; init; }                       // 0x66C
    public byte NoPartialReload { get; init; }                    // 0x66D
    public byte SegmentedReload { get; init; }                    // 0x66E
    public byte BlocksProne { get; init; }                        // 0x66F
    public byte Silenced { get; init; }                           // 0x670
    public byte IsRollingGrenade { get; init; }                   // 0x671
    public byte ProjectileExplosionEffectForceNormalUp { get; init; } // 0x672
    public byte ProjectileImpactExplode { get; init; }            // 0x673
    public byte StickToPlayers { get; init; }                     // 0x674
    public byte HasDetonator { get; init; }                       // 0x675
    public byte DisableFiring { get; init; }                      // 0x676
    public byte TimedDetonation { get; init; }                    // 0x677
    public byte Rotate { get; init; }                             // 0x678
    public byte HoldButtonToThrow { get; init; }                  // 0x679
    public byte FreezeMovementWhenFiring { get; init; }           // 0x67A
    public byte ThermalScope { get; init; }                       // 0x67B
    public byte AltModeSameWeapon { get; init; }                  // 0x67C
    public byte TurretBarrelSpinEnabled { get; init; }            // 0x67D
    public byte MissileConeSoundEnabled { get; init; }            // 0x67E
    public byte MissileConeSoundPitchShiftEnabled { get; init; }  // 0x67F
    public byte MissileConeSoundCrossfadeEnabled { get; init; }   // 0x680
    public byte OffhandHoldIsCancelable { get; init; }            // 0x681
    public ushort ReservedPadding { get; init; }                  // 0x682..0x683
}
