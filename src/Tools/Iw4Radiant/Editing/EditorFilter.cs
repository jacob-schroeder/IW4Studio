namespace Iw4Radiant.Editing;

[Flags]
internal enum EditorFilter
{
    None = 0,
    Structural = 1,
    Detail = 2,
    NonColliding = 4,
    WeaponClip = 8,
    PlayerClip = 16,
    Terrain = 32,
    Curves = 64,
    Models = 128,
    Prefabs = 256,
    Lights = 512,
    BrushEntities = 1024,
    OtherEntities = 2048
}
