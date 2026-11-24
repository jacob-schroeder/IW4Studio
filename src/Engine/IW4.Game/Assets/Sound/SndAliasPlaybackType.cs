namespace IW4.Game.Assets.Sound;

/// <summary>
/// Two-bit route in SndAlias.flags. PS3 dispatcher 0x0033CC60 starts samples
/// for 1 and streams for 2. It starts neither for 0 or 3. Blend compatibility
/// at 0x00335D58 compares the encoded routes, so 0 and 3 remain distinct.
/// </summary>
public enum SndAliasPlaybackType : byte
{
    NoDirectStart0 = 0,
    Sample = 1,
    Stream = 2,
    NoDirectStart3 = 3
}
