namespace Iw4Radiant.Materials;

internal static class ClipBrushMaterial
{
    internal const string PlayerClip = "clip_player";
    internal const string Ladder = "ladder";

    // Native PS3 clip_player detail brushes: PLAYERCLIP|DETAIL and
    // NOMARKS|NODRAW|NONSOLID|NOCASTSHADOW, without shot or solid contents.
    internal const int Contents = 0x08010000;
    internal const int SurfaceFlags = 0x000440A0;
    // Native PS3 Highrise placed ladder brushes use this complete surface
    // profile together with PLAYERCLIP|DETAIL contents.
    internal const int LadderSurfaceFlags = 0x000440B8;

    internal static bool IsPlayerClip(string materialName) =>
        materialName.Equals(PlayerClip, StringComparison.Ordinal);

    internal static bool IsLadder(string materialName) =>
        materialName.Equals(Ladder, StringComparison.Ordinal);

    internal static bool IsCollisionOnly(string materialName) =>
        IsPlayerClip(materialName) || IsLadder(materialName);
}
