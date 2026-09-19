namespace Iw4Radiant.Materials;

internal static class ClipBrushMaterial
{
    internal const string PlayerClip = "clip_player";

    // Native PS3 clip_player detail brushes: PLAYERCLIP|DETAIL and
    // NOMARKS|NODRAW|NONSOLID|NOCASTSHADOW, without shot or solid contents.
    internal const int Contents = 0x08010000;
    internal const int SurfaceFlags = 0x000440A0;

    internal static bool IsPlayerClip(string materialName) =>
        materialName.Equals(PlayerClip, StringComparison.Ordinal);
}
