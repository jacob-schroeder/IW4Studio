using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public enum SndAliasType : byte
{
    // Native SAT_UNKNOWN value; this is an enum label, not an unresolved field.
    Unknown = 0,
    Loaded = 1,
    Streamed = 2,
    Primed = 3,
    Count = 4
}
