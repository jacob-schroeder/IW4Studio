namespace IW4.Game.Assets.ImpactFx;

/// <summary>
/// PC-derived flesh effect ordering; PS3 semantic confirmation is pending.
/// </summary>
/// <remarks>
/// Source: OpenAssetTools commit 30d2d498bd8e3f2401037b24f3db6f96d39554c4,
/// src/Common/Game/IW4/IW4_Assets.h, FxFleshType.
/// </remarks>
public enum FxFleshType
{
    BodyNonFatal = 0,
    BodyFatal = 1,
    HeadNonFatal = 2,
    HeadFatal = 3,
    Count = 4
}
