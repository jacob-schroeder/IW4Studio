using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponAccuracyFields
{
    public XString GraphName0Pointer { get; init; }                               // 0x50C
    public string? GraphName0 { get; init; }
    public XString GraphName1Pointer { get; init; }                               // 0x510
    public string? GraphName1 { get; init; }
    public XPointer<Math.Vec2[]> GraphKnotsPointer { get; init; }                 // 0x514
    public IReadOnlyList<Math.Vec2> GraphKnots { get; init; } = [];
    public XPointer<Math.Vec2[]> OriginalGraphKnotsPointer { get; init; }         // 0x518
    public IReadOnlyList<Math.Vec2> OriginalGraphKnots { get; init; } = [];

    // 0x51C / 0x51E are not the loader counts for +0x514/+0x518.
    public ushort LocalGraphKnotCount { get; init; }
    public ushort LocalOriginalGraphKnotCount { get; init; }

    // 0x520: animation-notify comparison scalar.
    public int AnimationNotifyComparison { get; init; }
    public float LeftArc { get; init; }                                           // 0x524
    public float RightArc { get; init; }                                          // 0x528
    public float TopArc { get; init; }                                            // 0x52C
    public float BottomArc { get; init; }                                         // 0x530
    public float Accuracy { get; init; }                                          // 0x534
    public float AiSpread { get; init; }                                          // 0x538
    public float PlayerSpread { get; init; }                                      // 0x53C
}
