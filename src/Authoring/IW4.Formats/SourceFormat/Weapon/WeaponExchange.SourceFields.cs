using IW4.Game.Assets.Weapon;

namespace IW4.Formats.SourceFormat.Weapon;

public sealed partial class WeaponExchange
{
    public static bool TryGetAnimationSlot(string key, out WeaponAnimationSlot slot)
        => WeaponInfoStringSource.TryGetAnimationSlot(key, out slot);

    public static bool TryGetPrimarySoundSlot(string key, out WeaponPrimarySoundSlot slot)
        => WeaponInfoStringSource.TryGetPrimarySoundSlot(key, out slot);

    public static bool TryGetSurfaceIndex(string name, out int index)
    {
        return WeaponInfoStringSource.TryGetSurfaceIndex(name, out index);
    }

    public static bool TryGetHitLocation(string key, out HitLocation location)
        => WeaponInfoStringSource.TryGetHitLocation(key, out location);

    public static string GetBounceSoundSuffix(int index) =>
        WeaponInfoStringSource.GetBounceSoundSuffix(index);
}

internal static partial class WeaponInfoStringSource
{
    internal static bool TryGetAnimationSlot(string key, out WeaponAnimationSlot slot)
    {
        foreach ((string candidate, WeaponAnimationSlot value) in AnimationFields)
        {
            if (key != candidate)
                continue;
            slot = value;
            return true;
        }
        slot = default;
        return false;
    }

    internal static bool TryGetPrimarySoundSlot(string key, out WeaponPrimarySoundSlot slot)
    {
        foreach ((string candidate, WeaponPrimarySoundSlot value) in PrimarySoundFields)
        {
            if (key != candidate)
                continue;
            slot = value;
            return true;
        }
        slot = default;
        return false;
    }

    internal static bool TryGetSurfaceIndex(string name, out int index)
    {
        index = Array.IndexOf(SurfaceNames, name);
        return index >= 0;
    }

    internal static bool TryGetHitLocation(string key, out HitLocation location)
    {
        foreach ((string candidate, HitLocation value) in HitLocationFields)
        {
            if (key != candidate)
                continue;
            location = value;
            return true;
        }
        location = default;
        return false;
    }

    internal static string GetBounceSoundSuffix(int index) =>
        BounceSoundSuffixes[index];
}
