using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponTimingFields
{
    public int FireDelay { get; init; }                                           // 0x240
    public int MeleeDelay { get; init; }                                          // 0x244
    public int MeleeChargeDelay { get; init; }                                    // 0x248
    public int DetonateDelay { get; init; }                                       // 0x24C
    public int RechamberTime { get; init; }                                       // 0x250
    public int RechamberTimeOneHanded { get; init; }                              // 0x254
    public int RechamberBoltTime { get; init; }                                   // 0x258
    public int HoldFireTime { get; init; }                                        // 0x25C
    public int DetonateTime { get; init; }                                        // 0x260
    public int MeleeTime { get; init; }                                           // 0x264
    public int MeleeChargeTime { get; init; }                                     // 0x268
    public int ReloadTime { get; init; }                                          // 0x26C
    public int ReloadShowRocketTime { get; init; }                                // 0x270
    public int ReloadEmptyTime { get; init; }                                     // 0x274
    public int ReloadAddTime { get; init; }                                       // 0x278
    public int ReloadStartTime { get; init; }                                     // 0x27C
    public int ReloadStartAddTime { get; init; }                                  // 0x280
    public int ReloadEndTime { get; init; }                                       // 0x284
    public int DropTime { get; init; }                                            // 0x288
    public int RaiseTime { get; init; }                                           // 0x28C
    public int AltDropTime { get; init; }                                         // 0x290
    public int QuickDropTime { get; init; }                                       // 0x294
    public int QuickRaiseTime { get; init; }                                      // 0x298
    public int BreachRaiseTime { get; init; }                                     // 0x29C
    public int EmptyRaiseTime { get; init; }                                      // 0x2A0
    public int EmptyDropTime { get; init; }                                       // 0x2A4
    public int SprintInTime { get; init; }                                        // 0x2A8
    public int SprintLoopTime { get; init; }                                      // 0x2AC
    public int SprintOutTime { get; init; }                                       // 0x2B0
    public int StunnedTimeBegin { get; init; }                                    // 0x2B4
    public int StunnedTimeLoop { get; init; }                                     // 0x2B8
    public int StunnedTimeEnd { get; init; }                                      // 0x2BC
    public int NightVisionWearTime { get; init; }                                 // 0x2C0
    public int NightVisionWearTimeFadeOutEnd { get; init; }                       // 0x2C4
    public int NightVisionWearTimePowerUp { get; init; }                          // 0x2C8
    public int NightVisionRemoveTime { get; init; }                               // 0x2CC
    public int NightVisionRemoveTimePowerDown { get; init; }                      // 0x2D0
    public int NightVisionRemoveTimeFadeInStart { get; init; }                    // 0x2D4
    public int FuseTime { get; init; }                                            // 0x2D8
    public int AiFuseTime { get; init; }                                          // 0x2DC
}
