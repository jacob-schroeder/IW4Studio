namespace IW4.Render.Scheduling;

/// <summary>
/// Runtime draw-method inputs. The r_fullbright, r_debugShader and r_lodShaders
/// identities follow the PS3 layout. UseSunDirFog is the Xbox name for PS3 raw
/// field rg+0x2D5.
/// </summary>
public readonly record struct MapRenderDrawMethodSettings(
    bool FullbrightEnabled,
    int DebugShaderValue,
    bool UseSunDirFog,
    bool LodShadersEnabled);
