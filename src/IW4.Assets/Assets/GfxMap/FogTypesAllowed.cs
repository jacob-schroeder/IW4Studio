namespace IW4.Assets.Assets.GfxMap;

/// <summary>
/// Fog families allowed by a PS3 IW4 GfxWorld.
/// </summary>
[Flags]
public enum FogTypesAllowed : byte
{
    None = 0,
    Normal = 0x01,
    Dfog = 0x02
}
