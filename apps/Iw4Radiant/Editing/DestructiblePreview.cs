namespace Iw4Radiant.Editing;

internal enum DestructibleAppearance
{
    Intact,
    LightSmoke,
    HeavySmoke,
    Burning,
    Wreck
}

internal enum DestructibleWindowState
{
    Intact,
    Damaged,
    Broken
}

internal sealed record DestructiblePreviewSettings(
    DestructibleAppearance Appearance = DestructibleAppearance.Intact,
    DestructibleWindowState Windshield = DestructibleWindowState.Intact,
    bool FrontLeftTireFlat = false);
