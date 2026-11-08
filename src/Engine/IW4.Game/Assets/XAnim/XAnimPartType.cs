namespace IW4.Game.Assets.XAnim;

/// <summary>
/// PS3 console bone-count bucket order used by the console stream decoder;
/// the ten middle buckets differ from the PC XAnim layout.
/// </summary>
public enum XAnimPartType
{
    NoQuat = 0,
    SimpleQuat = 1,
    NormalQuat = 2,
    PrecisionQuat = 3,
    SimpleQuatNoSize = 4,
    NormalQuatNoSize = 5,
    PrecisionQuatNoSize = 6,
    SmallTrans = 7,
    Trans = 8,
    TransNoSize = 9,
    NoTrans = 10,
    All = 11,
    Count = 12
}
