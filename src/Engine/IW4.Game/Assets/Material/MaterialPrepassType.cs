namespace IW4.Game.Assets.Material;

/// <summary>
/// Two-bit prepass selector packed into <see cref="GfxDrawSurf"/>.
/// </summary>
public enum MaterialPrepassType : byte
{
    Standard = 0,
    Alpha = 1,
    FloatZ = 2,
    None = 3,
    Count = 4
}
