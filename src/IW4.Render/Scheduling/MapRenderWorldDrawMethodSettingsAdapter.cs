using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling;

/// <summary>
/// Applies the PS3 world fog-mode clamp before draw-method initialization.
/// </summary>
public static class MapRenderWorldDrawMethodSettingsAdapter
{
    private const byte FogModeZeroAllowed = 0x01;
    private const byte FogModeOneAllowed = 0x02;

    public static MapRenderDrawMethodSettings Adapt(
        GfxWorldAsset world,
        MapRenderDrawMethodSettings baseSettings,
        bool activeSunFogEnabled)
    {
        ArgumentNullException.ThrowIfNull(world);
        return baseSettings with
        {
            UseSunDirFog = ResolveUseSunDirFog(
                world.FogTypesAllowed,
                activeSunFogEnabled)
        };
    }

    public static bool ResolveUseSunDirFog(
        byte fogTypesAllowed,
        bool activeSunFogEnabled)
    {
        if (activeSunFogEnabled &&
            (fogTypesAllowed & FogModeOneAllowed) != 0)
        {
            return true;
        }

        return (fogTypesAllowed & FogModeZeroAllowed) == 0;
    }

    public static bool TryResolveUseSunDirFogWithoutActiveFogState(
        byte fogTypesAllowed,
        out bool useSunDirFog)
    {
        bool whenDisabled = ResolveUseSunDirFog(
            fogTypesAllowed,
            activeSunFogEnabled: false);
        bool whenEnabled = ResolveUseSunDirFog(
            fogTypesAllowed,
            activeSunFogEnabled: true);
        useSunDirFog = whenDisabled;
        return whenDisabled == whenEnabled;
    }
}
