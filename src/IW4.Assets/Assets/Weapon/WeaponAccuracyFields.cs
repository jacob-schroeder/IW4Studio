using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponAccuracyFields
{
    public XString AiVsAiGraphNamePointer { get; init; }                          // 0x50C
    public string? AiVsAiGraphName { get; init; }
    public XString AiVsPlayerGraphNamePointer { get; init; }                      // 0x510
    public string? AiVsPlayerGraphName { get; init; }
    public XPointer<Math.Vec2[]> OriginalAiVsAiGraphKnotsPointer { get; init; }   // 0x514
    public IReadOnlyList<Math.Vec2> OriginalAiVsAiGraphKnots { get; init; } = [];
    public XPointer<Math.Vec2[]> OriginalAiVsPlayerGraphKnotsPointer { get; init; }// 0x518
    public IReadOnlyList<Math.Vec2> OriginalAiVsPlayerGraphKnots { get; init; } = [];
    public ushort OriginalAiVsAiGraphKnotCount { get; init; }                     // 0x51C
    public ushort OriginalAiVsPlayerGraphKnotCount { get; init; }                 // 0x51E
    public int PositionReloadTransitionTime { get; init; }                        // 0x520
    public float LeftArc { get; init; }                                           // 0x524
    public float RightArc { get; init; }                                          // 0x528
    public float TopArc { get; init; }                                            // 0x52C
    public float BottomArc { get; init; }                                         // 0x530
    public float Accuracy { get; init; }                                          // 0x534
    public float AiSpread { get; init; }                                          // 0x538
    public float PlayerSpread { get; init; }                                      // 0x53C
}
