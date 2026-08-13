namespace IW4.Assets.Assets.XModel;

/// <summary>
/// PS3 XSurface payload-block routing flags. A set bit routes the named
/// payload through the LARGE block instead of its default block.
/// </summary>
[Flags]
public enum XSurfaceStreamFlags : byte
{
    None = 0,
    Verts0InLarge = 0x01,
    Verts1InLarge = 0x02,
    TriIndicesInLarge = 0x04
}
