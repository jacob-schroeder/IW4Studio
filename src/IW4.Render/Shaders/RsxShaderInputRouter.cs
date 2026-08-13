using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

public static class RsxShaderInputRouter
{
    // Enough room for normal and diagnostic routes across every RSX sampler
    // destination, while bounding full byte-snapshot/decoded-IR retention for
    // editor sessions that repeatedly mutate a pass in place.
    internal const int SamplerRouteCacheEntryCapacity = 32;

    private static readonly ConditionalWeakTable<MaterialPassAsset,
        SamplerRouteCacheState> SamplerRouteCache = new();

    // Exact rows for MatchedFragmentInput=texcoord0,
    // MatchedVertexInputs=v8/TEX0, and MatchedRouteSources=0x02.
    private static readonly HashSet<(string VertexShaderName, string PixelShaderName, ushort SamplerDest)> ProvenPrecompiledTexcoord0Routes = new()
    {
        ("lm_dfog_tc0_nc_sm3.hlsl", "lm_dfog_r0c0_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0_nc_sm3.hlsl", "lm_dfog_r0c0s0_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0_nc_sm3.hlsl", "lm_dfog_r0c0s0_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0s0m1c1_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0s0m1c1_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0s0t1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0s0t1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0n0_nc_sm3.hlsl", "lm_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0n0_nc_sm3.hlsl", "lm_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0n0_nc_sm3.hlsl", "lm_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0n0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0n0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0n0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1b2c2n2s2m3c3_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1b2c2n2s2m3c3_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1b2c2n2s2m3c3_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0n0_tc1n1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2m3c3_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0n0_tc1n1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2m3c3_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0n0_tc1n1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2m3c3_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0q0n0_sm3.hlsl", "lm_dfog_r0c0q0n0s0px_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_sm3.hlsl", "lm_dfog_r0c0q0n0s0px_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0q0n0_sm3.hlsl", "lm_dfog_r0c0q0n0s0px_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0q0n0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1px_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1px_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0q0n0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1px_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)6),
        ("lm_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)6),
        ("lm_omni_dfog_fs_tc0_nc_sm3.hlsl", "lm_spot_dfog_r0c0sf0_nc_sm3.hlsl", (ushort)0),
        ("lm_omni_dfog_fs_tc0_nc_sm3.hlsl", "lm_spot_dfog_r0c0sf0_nc_sm3.hlsl", (ushort)6),
        ("lm_omni_dfog_s_tc0_nc_sm3.hlsl", "lm_spot_dfog_r0c0s0_nc_sm3.hlsl", (ushort)0),
        ("lm_omni_dfog_s_tc0_nc_sm3.hlsl", "lm_spot_dfog_r0c0s0_nc_sm3.hlsl", (ushort)6),
        ("lm_omni_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_spot_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("lm_omni_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_spot_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("lm_omni_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_spot_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_omni_dfog_s_tc0_nc_sm3.hlsl", "lm_sm_spot_dfog_r0c0s0_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_omni_dfog_s_tc0_nc_sm3.hlsl", "lm_sm_spot_dfog_r0c0s0_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_omni_dfog_s_tc0n0_nc_sm3.hlsl", "lm_sm_spot_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_omni_dfog_s_tc0n0_nc_sm3.hlsl", "lm_sm_spot_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_omni_dfog_s_tc0n0_nc_sm3.hlsl", "lm_sm_spot_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_omni_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_sm_spot_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_omni_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_sm_spot_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_omni_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_sm_spot_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0s0_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0s0_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0s0m1c1_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0s0m1c1_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0n0_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0n0_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0n0_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0q0n0_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0q0n0_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0px_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0q0n0_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0px_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0q0n0_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0px_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_tc0_nc_sm3.hlsl", "lm_sm_sun_dfog_b0c0_nc_sm3.hlsl", (ushort)0),
        ("lm_sun_dfog_fs_tc0_nc_sm3.hlsl", "lm_sun_dfog_r0c0sf0_nc_sm3.hlsl", (ushort)0),
        ("lm_sun_dfog_fs_tc0_nc_sm3.hlsl", "lm_sun_dfog_r0c0sf0_nc_sm3.hlsl", (ushort)6),
        ("lm_sun_dfog_fs_tc0_tc1_nc_sm3.hlsl", "lm_sun_dfog_r0c0sf0b1c1_nc_sm3.hlsl", (ushort)0),
        ("lm_sun_dfog_fs_tc0_tc1_nc_sm3.hlsl", "lm_sun_dfog_r0c0sf0b1c1_nc_sm3.hlsl", (ushort)6),
        ("lm_sun_dfog_fs_tc0_tc1_nc_sm3.hlsl", "lm_sun_dfog_r0c0sf0b1c1_nc_sm3.hlsl", (ushort)7),
        ("mul.hlsl", "lm_dfog_b0c0n0s0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_b0c0n0s0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_b0c0n0s0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_b0c0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_b0c0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_b0c0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_b0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_b0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_b0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0b1c1n1s1t2c2n2s2_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0b1c1n1s1t2c2n2s2_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0b1c1n1s1t2c2n2s2_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0d0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2m3c3_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2m3c3_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2m3c3_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1b2c2n2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1b2c2n2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1b2c2n2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1b2c2n2s2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1b2c2n2s2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1b2c2n2s2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1b2c2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1b2c2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1b2c2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2m3c3_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2m3c3_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2m3c3_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0m1c1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0n0s0t1c1n1s1m2c2_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0n0s0t1c1n1s1m2c2_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0n0s0t1c1n1s1m2c2_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0b1c1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0b1c1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0b1c1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1b2c2s2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1b2c2s2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1b2c2s2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_r0c0s0b1c1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_r0c0s0b1c1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_t0c0n0s0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_t0c0n0s0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_t0c0n0s0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_t0c0n0s0_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_t0c0n0s0_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_t0c0n0s0_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_dfog_t0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_dfog_t0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_dfog_t0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0b1c1n1b2c2n2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0b1c1n1b2c2n2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0b1c1n1b2c2n2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_spot_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_b0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_b0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_b0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0d0n0s0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0d0n0s0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0d0n0s0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0d0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0d0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0d0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2m3c3_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2m3c3_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2m3c3_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1s1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1s1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1s1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2n2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2n2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2n2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2m3c3_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2m3c3_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2m3c3_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1b2c2s2px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1b2c2s2px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1b2c2s2px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_t0c0n0s0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_t0c0n0s0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_t0c0n0s0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_sm_sun_dfog_t0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_sm_sun_dfog_t0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_sm_sun_dfog_t0c0n0s0t1c1n1s1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_r0c0d0sf0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0d0sf0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_r0c0d0sf0b1c1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0d0sf0b1c1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_r0c0d0sf0b1c1t2c2_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0d0sf0b1c1t2c2_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_r0c0d0sf0t1c1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0d0sf0t1c1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_r0c0n0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0n0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_spot_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_spot_dfog_r0c0n0s0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("mul.hlsl", "lm_spot_dfog_r0c0n0s0px_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_r0c0sf0m1c1_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0sf0m1c1_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_r0c0sf0m1c1m2c2_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_r0c0sf0m1c1m2c2_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_t0c0sf0_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_t0c0sf0_nc_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lm_spot_dfog_t0c0sf0_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lm_spot_dfog_t0c0sf0_sm3.hlsl", (ushort)6),
        ("mul.hlsl", "lp_dfog_t0c0n0s0px_nc_sm3.hlsl", (ushort)0),
        ("mul.hlsl", "lp_dfog_t0c0n0s0px_nc_sm3.hlsl", (ushort)5),
        ("vertcol_simple_dfog_nc.hlsl", "vertcol_shaded_dfog_lin_nc.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("lm_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("lm_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)6),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)0),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)5),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)6),
    };

    // Exact selected rows from mp_boneyard_shader_vertex_input_bridge.csv with
    // MatchedFragmentInput=texcoord6, MatchedVertexInputs=v11/TEX3, and
    // MatchedRouteSources=0x05. Rows may still resolve to a disabled/default
    // backend attribute for a given world vertex format; validation marks
    // that separately instead of treating the default value as resolved.
    private static readonly HashSet<(string VertexShaderName, string PixelShaderName, ushort SamplerDest)> ProvenPrecompiledTexcoord6FromTex3Routes = new()
    {
        ("lm_dfog_s_tc0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0s0m1c1_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0s0t1c1n1s1_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0s0t1c1n1s1_nc_sm3.hlsl", (ushort)8),
        ("lm_dfog_s_tc0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0s0t1c1n1s1_nc_sm3.hlsl", (ushort)9),
        ("lm_dfog_s_tc0n0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)10),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1t2c2n2s2_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1t2c2n2s2_nc_sm3.hlsl", (ushort)10),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1t2c2n2s2_nc_sm3.hlsl", (ushort)11),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1t2c2n2s2_nc_sm3.hlsl", (ushort)12),
        ("lm_dfog_s_tc0n0_tc1n1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2m3c3_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0n0_tc1n1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2m3c3_nc_sm3.hlsl", (ushort)8),
        ("lm_dfog_s_tc0n0_tc1n1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2m3c3_nc_sm3.hlsl", (ushort)9),
        ("lm_dfog_s_tc0n0_tc1n1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1m2c2m3c3_nc_sm3.hlsl", (ushort)10),
        ("lm_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)8),
        ("lm_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)9),
        ("lm_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)10),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1b2c2n2s2m3c3_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1b2c2n2s2m3c3_nc_sm3.hlsl", (ushort)10),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1b2c2n2s2m3c3_nc_sm3.hlsl", (ushort)11),
        ("lm_dfog_s_tc0n0_tc1_tc2n2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0n0s0b1c1b2c2n2s2m3c3_nc_sm3.hlsl", (ushort)12),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)10),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)10),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)10),
        ("lm_dfog_s_tc0q0n0_tc1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1px_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)8),
        ("lm_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)9),
        ("lm_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)7),
        ("lm_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)8),
        ("lm_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)9),
        ("lm_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)10),
        ("lm_sm_sun_dfog_s_tc0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0s0m1c1_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1b2c2b3c3px_nc_sm3.hlsl", (ushort)10),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1m2c2_nc_sm3.hlsl", (ushort)10),
        ("lm_sm_sun_dfog_s_tc0n0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0m1c1_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)8),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1_nc_sm3.hlsl", (ushort)9),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2s2px_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2s2px_nc_sm3.hlsl", (ushort)8),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2s2px_nc_sm3.hlsl", (ushort)9),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2s2px_nc_sm3.hlsl", (ushort)10),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2s2px_nc_sm3.hlsl", (ushort)12),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)8),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)9),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)10),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)11),
        ("lm_sm_sun_dfog_s_tc0n0_tc1n1_tc2n2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0n0s0b1c1n1s1b2c2n2s2_nc_sm3.hlsl", (ushort)12),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)10),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)10),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2px_nc_sm3.hlsl", (ushort)10),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1px_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)8),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1px_nc_sm3.hlsl", (ushort)9),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)7),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)8),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)9),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1n1_tc2_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1n1s1m2c2px_nc_sm3.hlsl", (ushort)10),
    };

    private static readonly HashSet<(string VertexShaderName, string PixelShaderName, ushort SamplerDest)> ProvenPrecompiledTexcoord7FromTex4Routes = new()
    {
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)13),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)4),
        ("lm_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)13),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0m1c1m2c2m3c3px_nc_sm3.hlsl", (ushort)13),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)9),
        ("lm_sm_sun_dfog_s_tc0q0n0_tc1_tc2_tc3_tc4_nc_sm3.hlsl", "lm_sm_sun_dfog_r0c0q0n0s0b1c1m2c2m3c3m4c4px_nc_sm3.hlsl", (ushort)13),
    };

    public static bool TrySelectSamplerSource(
        MaterialPassAsset pass,
        MaterialShaderArgumentAsset samplerArg,
        MaterialVertexDeclarationAsset? vertexDecl,
        out MaterialStreamSource source)
    {
        return TrySelectSamplerSourceCached(pass, samplerArg, vertexDecl, textureSemantic: -1, out source);
    }

    private static bool TrySelectDecodedSamplerSource(
        SamplerRouteInputSnapshot snapshot,
        IReadOnlyList<PixelTextureOp> textureOps,
        RsxVertexOutputDependencyAnalysis vertexAnalysis,
        out MaterialStreamSource source)
    {
        source = default;
        IReadOnlyDictionary<string, IReadOnlySet<string>> outputDeps =
            vertexAnalysis.OutputDependencies;
        IReadOnlyDictionary<string, ImmutableArray<IReadOnlySet<string>>>
            outputComponentDeps =
            vertexAnalysis.OutputComponentDependencies;
        foreach (PixelTextureOp op in textureOps)
        {
            string fragmentInput = FragmentInputName(op.SourceAttribute);
            string vertexOutput = VertexOutputForFragmentInput(fragmentInput);
            string expectedVertexInput = VertexInputForFragmentInput(fragmentInput);
            if (vertexOutput.Length == 0 ||
                expectedVertexInput.Length == 0 ||
                !outputDeps.TryGetValue(
                    vertexOutput,
                    out IReadOnlySet<string>? deps))
            {
                continue;
            }

            if (!deps.Contains(expectedVertexInput))
            {
                if (!TryGetTextureInputForOutputXY(outputComponentDeps, vertexOutput, out expectedVertexInput) &&
                    !TryGetSingleTextureInputDependency(deps, out expectedVertexInput))
                {
                    continue;
                }
            }

            foreach (MaterialVertexStreamRouting route in
                     ActiveRoutes(snapshot.VertexDeclaration))
            {
                if (VertexDeclarationDestinationInputName(route.Dest) == expectedVertexInput)
                {
                    source = route.Source;
                    return true;
                }
            }

            if (TrySelectComponentProvenSamplerSource(
                    outputComponentDeps,
                    vertexOutput,
                    snapshot.VertexDeclaration,
                    out source))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TrySelectSamplerSource(
        MaterialPassAsset pass,
        MaterialShaderArgumentAsset samplerArg,
        MaterialVertexDeclarationAsset? vertexDecl,
        TextureSemantic textureSemantic,
        out MaterialStreamSource source)
    {
        return TrySelectSamplerSourceCached(
            pass,
            samplerArg,
            vertexDecl,
            (int)textureSemantic,
            out source);
    }

    private static bool TrySelectSamplerSourceCached(
        MaterialPassAsset pass,
        MaterialShaderArgumentAsset samplerArg,
        MaterialVertexDeclarationAsset? vertexDecl,
        int textureSemantic,
        out MaterialStreamSource source)
    {
        if (vertexDecl is null)
        {
            source = default;
            return false;
        }

        SamplerRouteCacheState state = SamplerRouteCache.GetOrCreateValue(pass);
        SamplerRouteInputSnapshot snapshot =
            SamplerRouteInputSnapshot.Capture(
                pass,
                samplerArg.Dest,
                vertexDecl,
                RsxProgramSemanticCache.Shared);
        SamplerRouteResult result = ResolveSamplerSourceCached(
            state,
            snapshot,
            textureSemantic);
        source = result.Source;
        return result.Success;
    }

    private static SamplerRouteResult ResolveSamplerSourceCached(
        SamplerRouteCacheState state,
        SamplerRouteInputSnapshot snapshot,
        int textureSemantic)
    {
        SamplerRouteCacheKey key = snapshot.CreateKey(textureSemantic);
        SamplerRouteCacheEntry entry = state.GetOrAdd(
            key,
            snapshot,
            textureSemantic);
        try
        {
            return entry.GetValue();
        }
        finally
        {
            state.CompleteLookup(key, entry);
        }
    }

    private static SamplerRouteResult ComputeSamplerRoute(
        SamplerRouteInputSnapshot snapshot,
        int textureSemantic)
    {
        PixelTextureOp[] textureOps = snapshot.ProgramSemantics
            .FragmentProgram
            .TextureOps
            .Where(op => op.TextureUnit == snapshot.SamplerDestination)
            .ToArray();
        MaterialStreamSource source = default;

        // Preserve the original fast path: name-based routes do not
        // need a vertex decode when no matching pixel texture op exists.
        if (textureOps.Length == 0 &&
            TrySelectPrecompiledSamplerSource(snapshot, out source))
        {
            return new SamplerRouteResult(
                true,
                source,
                RsxVertexOutputDependencyAnalysis.Empty,
                textureOps);
        }

        RsxVertexOutputDependencyAnalysis vertexAnalysis =
            textureOps.Length > 0 ||
            (textureSemantic == (int)TextureSemantic.ColorMap &&
             snapshot.SamplerDestination == 0)
                ? ResolveVertexOutputDependencyAnalysis(snapshot)
                : RsxVertexOutputDependencyAnalysis.Empty;
        bool success = textureOps.Length > 0 &&
            TrySelectDecodedSamplerSource(
                snapshot,
                textureOps,
                vertexAnalysis,
                out source);
        if (!success)
            success = TrySelectPrecompiledSamplerSource(snapshot, out source);
        if (!success &&
            textureSemantic == (int)TextureSemantic.ColorMap &&
            snapshot.SamplerDestination == 0)
        {
            success = TrySelectMaterialColorTexcoord0Source(
                vertexAnalysis,
                snapshot.VertexDeclaration,
                out source);
        }

        return new SamplerRouteResult(
            success,
            source,
            vertexAnalysis,
            textureOps);
    }

    public static string DescribeSamplerSourceBlocker(
        MaterialPassAsset pass,
        MaterialShaderArgumentAsset samplerArg,
        MaterialVertexDeclarationAsset? vertexDecl)
    {
        if (vertexDecl is null)
            return "Vertex declaration unresolved.";

        SamplerRouteCacheState state = SamplerRouteCache.GetOrCreateValue(pass);
        SamplerRouteInputSnapshot snapshot =
            SamplerRouteInputSnapshot.Capture(
                pass,
                samplerArg.Dest,
                vertexDecl,
                RsxProgramSemanticCache.Shared);
        SamplerRouteResult routeResult = ResolveSamplerSourceCached(
            state,
            snapshot,
            textureSemantic: -1);
        if (routeResult.Success)
            return string.Empty;

        IReadOnlyList<PixelTextureOp> textureOps =
            routeResult.MatchingPixelTextureOps;
        if (textureOps.Count == 0)
            return "Pixel texture op missing for sampler destination and no supported precompiled route.";

        RsxVertexOutputDependencyAnalysis vertexAnalysis =
            routeResult.VertexAnalysis;
        IReadOnlyDictionary<string, IReadOnlySet<string>> outputDeps =
            vertexAnalysis.OutputDependencies;
        IReadOnlyDictionary<string, ImmutableArray<IReadOnlySet<string>>>
            outputComponentDeps =
            vertexAnalysis.OutputComponentDependencies;
        foreach (PixelTextureOp op in textureOps)
        {
            string fragmentInput = FragmentInputName(op.SourceAttribute);
            if (fragmentInput.Length == 0)
                return "Pixel texture op uses an unmapped fragment input.";

            string vertexOutput = VertexOutputForFragmentInput(fragmentInput);
            if (vertexOutput.Length == 0)
            {
                return fragmentInput == "position"
                    ? "Pixel sampler uses RSX fragment position; no vertex texcoord route is available."
                    : "Pixel sampler fragment input has no supported vertex output route.";
            }

            string expectedVertexInput = VertexInputForFragmentInput(fragmentInput);
            if (expectedVertexInput.Length == 0)
                return "Pixel sampler fragment input has no supported vertex input route.";

            if (!outputDeps.TryGetValue(
                    vertexOutput,
                    out IReadOnlySet<string>? deps))
                return "Pixel sampler requires a vertex output that the vertex shader does not prove.";

            if (!deps.Contains(expectedVertexInput) &&
                !TryGetTextureInputForOutputXY(outputComponentDeps, vertexOutput, out _) &&
                !TryGetSingleTextureInputDependency(deps, out _))
            {
                return "Pixel sampler vertex output dependencies do not prove a texture input route.";
            }

            bool routeExists = false;
            foreach (MaterialVertexStreamRouting route in
                     ActiveRoutes(snapshot.VertexDeclaration))
            {
                if (VertexDeclarationDestinationInputName(route.Dest) ==
                    expectedVertexInput)
                {
                    routeExists = true;
                    break;
                }
            }
            if (!routeExists)
                return "Vertex declaration has no route for the shader texture input.";
        }

        return "No pixel texture op could be resolved to a supported vertex route.";
    }

    private static bool TrySelectPrecompiledSamplerSource(
        SamplerRouteInputSnapshot snapshot,
        out MaterialStreamSource source)
    {
        source = default;

        // The bytecode decoder gets first chance. This exact table is only a fallback
        // for selected shader/dest routes already resolved outside this renderer.
        string? expectedVertexInput = TryGetLiveProvenPrecompiledTexcoord0Route(
            snapshot.VertexShaderName,
            snapshot.PixelShaderName,
            snapshot.SamplerDestination)
            ? VertexInputForFragmentInput("texcoord0")
            : TryGetLiveProvenPrecompiledTexcoord6FromTex3Route(
                snapshot.VertexShaderName,
                snapshot.PixelShaderName,
                snapshot.SamplerDestination)
                ? VertexInputName(
                    RsxVertexInputAttribute.TextureCoordinate3)
                : TryGetLiveProvenPrecompiledTexcoord7FromTex4Route(
                    snapshot.VertexShaderName,
                    snapshot.PixelShaderName,
                    snapshot.SamplerDestination)
                    ? VertexInputName(
                        RsxVertexInputAttribute.TextureCoordinate4)
                    : null;

        if (expectedVertexInput is null)
            return false;

        foreach (MaterialVertexStreamRouting route in
                 ActiveRoutes(snapshot.VertexDeclaration))
        {
            if (VertexDeclarationDestinationInputName(route.Dest) == expectedVertexInput)
            {
                source = route.Source;
                return true;
            }
        }

        return false;
    }

    private static bool TrySelectMaterialColorTexcoord0Source(
        RsxVertexOutputDependencyAnalysis vertexAnalysis,
        VertexDeclarationCacheIdentity vertexDeclaration,
        out MaterialStreamSource source)
    {
        source = default;
        IReadOnlyDictionary<string, ImmutableArray<IReadOnlySet<string>>>
            outputComponentDeps =
            vertexAnalysis.OutputComponentDependencies;
        if (!TryGetTextureInputForOutputXY(outputComponentDeps, VertexOutputForFragmentInput("texcoord0"), out string vertexInput))
            return false;

        foreach (MaterialVertexStreamRouting route in
                 ActiveRoutes(vertexDeclaration))
        {
            if (VertexDeclarationDestinationInputName(route.Dest) == vertexInput)
            {
                source = route.Source;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetLiveProvenPrecompiledTexcoord0Route(string? vertexShaderName, string? pixelShaderName, ushort samplerDest)
    {
        if (vertexShaderName is null || pixelShaderName is null)
            return false;

        return ProvenPrecompiledTexcoord0Routes.Contains((vertexShaderName, pixelShaderName, samplerDest));
    }

    private static bool TryGetLiveProvenPrecompiledTexcoord6FromTex3Route(string? vertexShaderName, string? pixelShaderName, ushort samplerDest)
    {
        if (vertexShaderName is null || pixelShaderName is null)
            return false;

        return ProvenPrecompiledTexcoord6FromTex3Routes.Contains((vertexShaderName, pixelShaderName, samplerDest));
    }

    private static bool TryGetLiveProvenPrecompiledTexcoord7FromTex4Route(string? vertexShaderName, string? pixelShaderName, ushort samplerDest)
    {
        if (vertexShaderName is null || pixelShaderName is null)
            return false;

        return ProvenPrecompiledTexcoord7FromTex4Routes.Contains((vertexShaderName, pixelShaderName, samplerDest));
    }

    private static RsxVertexOutputDependencyAnalysis
        ResolveVertexOutputDependencyAnalysis(
            SamplerRouteInputSnapshot snapshot)
    {
        if (!snapshot.VertexProgram.HasData ||
            snapshot.VertexProgram.ByteCount == 0)
        {
            return RsxVertexOutputDependencyAnalysis.Empty;
        }

        return snapshot.ProgramSemantics.VertexProgram
            .OutputDependencyAnalysis;
    }

    internal static RsxVertexOutputDependencyAnalysis
        AnalyzeVertexOutputDependencies(RsxVertexProgramIr vertexProgram)
    {
        ArgumentNullException.ThrowIfNull(vertexProgram);
        var outputDeps = new Dictionary<string, SortedSet<string>>(
            StringComparer.Ordinal);
        var outputComponentDeps =
            new Dictionary<string, SortedSet<string>[]>(
                StringComparer.Ordinal);
        var tempDeps = new Dictionary<int, SortedSet<string>>();
        var tempComponentDeps =
            new Dictionary<int, SortedSet<string>[]>();

        foreach (RsxVertexInstruction instruction in vertexProgram.Instructions)
        {
            ApplyVertexSlotDependency(
                instruction,
                scalar: false,
                tempDeps,
                outputDeps);
            ApplyVertexSlotDependency(
                instruction,
                scalar: true,
                tempDeps,
                outputDeps);
            ApplyVertexSlotComponentDependency(
                instruction,
                scalar: false,
                tempComponentDeps,
                outputComponentDeps);
            ApplyVertexSlotComponentDependency(
                instruction,
                scalar: true,
                tempComponentDeps,
                outputComponentDeps);
        }

        return new RsxVertexOutputDependencyAnalysis(
            vertexProgram,
            outputDeps,
            outputComponentDeps);
    }

    private static bool TrySelectComponentProvenSamplerSource(
        IReadOnlyDictionary<string, ImmutableArray<IReadOnlySet<string>>>
            outputComponentDeps,
        string vertexOutput,
        VertexDeclarationCacheIdentity vertexDeclaration,
        out MaterialStreamSource source)
    {
        source = default;
        if (!TryGetTextureInputForOutputXY(outputComponentDeps, vertexOutput, out string vertexInput))
            return false;

        foreach (MaterialVertexStreamRouting route in
                 ActiveRoutes(vertexDeclaration))
        {
            if (VertexDeclarationDestinationInputName(route.Dest) == vertexInput)
            {
                source = route.Source;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetTextureInputForOutputXY(
        IReadOnlyDictionary<string, ImmutableArray<IReadOnlySet<string>>>
            outputComponentDeps,
        string vertexOutput,
        out string vertexInput)
    {
        vertexInput = "";
        if (vertexOutput.Length == 0 ||
            !outputComponentDeps.TryGetValue(
                vertexOutput,
                out ImmutableArray<IReadOnlySet<string>> deps) ||
            deps.Length < 2)
        {
            return false;
        }

        if (!TryGetSingleTextureInputComponent(deps[0], "x", out string inputX) ||
            !TryGetSingleTextureInputComponent(deps[1], "y", out string inputY) ||
            inputX != inputY)
        {
            return false;
        }

        vertexInput = inputX;
        return true;
    }

    private static bool TryGetSingleTextureInputComponent(
        IReadOnlySet<string> deps,
        string component,
        out string vertexInput)
    {
        vertexInput = "";
        if (deps.Count == 0)
            return false;

        string suffix = "." + component;
        foreach (string dep in deps)
        {
            if (!dep.EndsWith(suffix, StringComparison.Ordinal))
                return false;

            string input = dep[..^suffix.Length];
            if (!IsTextureVertexInput(input))
                return false;

            if (vertexInput.Length == 0)
            {
                vertexInput = input;
                continue;
            }

            if (vertexInput != input)
                return false;
        }

        return vertexInput.Length != 0;
    }

    private static bool TryGetSingleTextureInputDependency(
        IReadOnlySet<string> deps,
        out string vertexInput)
    {
        vertexInput = "";
        foreach (string dep in deps)
        {
            if (!IsTextureVertexInput(dep))
                continue;

            if (vertexInput.Length == 0)
            {
                vertexInput = dep;
                continue;
            }

            if (vertexInput != dep)
                return false;
        }

        return vertexInput.Length != 0;
    }

    private static bool IsTextureVertexInput(string input) =>
        input.StartsWith("v", StringComparison.Ordinal) &&
        input.Contains("/TEX", StringComparison.Ordinal);

    private static void ApplyVertexSlotComponentDependency(
        RsxVertexInstruction instruction,
        bool scalar,
        Dictionary<int, SortedSet<string>[]> tempDeps,
        Dictionary<string, SortedSet<string>[]> outputDeps)
    {
        byte opcode = scalar ? instruction.ScaOpcode : instruction.VecOpcode;
        RsxVertexWriteMask writeMask = scalar
            ? instruction.ScalarWriteMask
            : instruction.VectorWriteMask;
        if (opcode == 0 || writeMask == RsxVertexWriteMask.None)
            return;

        SortedSet<string>[] deps = CollectVertexSlotComponentDependencies(instruction, scalar, tempDeps);
        bool writesResult = scalar
            ? instruction.ScaResult
            : instruction.VecResult;
        if (writesResult && instruction.Result != RsxVertexResult.None)
        {
            WriteMaskedComponentDeps(
                GetOrAddComponentDeps(
                    outputDeps,
                    VertexOutputName(instruction.Result)),
                deps,
                writeMask);
            return;
        }

        int temp = scalar
            ? instruction.ScaDestTemp
            : instruction.VecDestTemp;
        if (!tempDeps.TryGetValue(temp, out SortedSet<string>[]? target))
        {
            target = EmptyComponentDeps();
            tempDeps.Add(temp, target);
        }

        WriteMaskedComponentDeps(target, deps, writeMask);
    }

    private static SortedSet<string>[] CollectVertexSlotComponentDependencies(
        RsxVertexInstruction instruction,
        bool scalar,
        Dictionary<int, SortedSet<string>[]> tempDeps)
    {
        if (scalar)
        {
            return RsxShaderInstructionSet.VertexScalarOperandCount(
                       instruction.ScalarOpcode) > 0
                ? ResolveVertexSourceComponentDependencies(instruction, instruction.Source2, tempDeps)
                : EmptyComponentDeps();
        }

        RsxSourceSlotMask sourceMask =
            RsxShaderInstructionSet.VertexSourceMask(
                instruction.VectorOpcode);
        SortedSet<string>[] source0 =
            (sourceMask & RsxSourceSlotMask.Source0) != RsxSourceSlotMask.None
            ? ResolveVertexSourceComponentDependencies(instruction, instruction.Source0, tempDeps)
            : EmptyComponentDeps();
        SortedSet<string>[] source1 =
            (sourceMask & RsxSourceSlotMask.Source1) != RsxSourceSlotMask.None
            ? ResolveVertexSourceComponentDependencies(instruction, instruction.Source1, tempDeps)
            : EmptyComponentDeps();
        SortedSet<string>[] source2 =
            (sourceMask & RsxSourceSlotMask.Source2) != RsxSourceSlotMask.None
            ? ResolveVertexSourceComponentDependencies(instruction, instruction.Source2, tempDeps)
            : EmptyComponentDeps();

        return instruction.VectorOpcode switch
        {
            RsxVertexVectorOpcode.Dot3 =>
                DotComponentDeps(source0, source1, componentCount: 3),
            RsxVertexVectorOpcode.DotHomogeneous or
                RsxVertexVectorOpcode.Dot4 =>
                DotComponentDeps(source0, source1, componentCount: 4),
            RsxVertexVectorOpcode.Distance =>
                DstComponentDeps(source0, source1),
            _ => UnionComponentDeps(source0, source1, source2)
        };
    }

    private static SortedSet<string>[] ResolveVertexSourceComponentDependencies(
        RsxVertexInstruction instruction,
        uint source,
        Dictionary<int, SortedSet<string>[]> tempDeps)
    {
        SortedSet<string>[] baseDeps =
            RsxVertexInstruction.SourceRegisterKind(source) switch
        {
            RsxVertexRegisterType.Temporary =>
                tempDeps.TryGetValue(
                    (int)((source >> 2) & 0x3f),
                    out SortedSet<string>[]? deps)
                ? CloneComponentDeps(deps)
                : EmptyComponentDeps(),
            RsxVertexRegisterType.Input =>
                VertexInputComponentDeps(
                    VertexInputName(instruction.InputAttribute)),
            RsxVertexRegisterType.Constant =>
                VertexInputComponentDeps($"C{instruction.ConstSource}"),
            _ => EmptyComponentDeps()
        };

        RsxSwizzleComponent[] swizzle = RsxVertexSourceSwizzle(source);
        SortedSet<string>[] resolved = EmptyComponentDeps();
        for (int component = 0; component < 4; component++)
        {
            AddRange(
                resolved[component],
                baseDeps[(int)swizzle[component]]);
        }

        return resolved;
    }

    private static SortedSet<string>[] VertexInputComponentDeps(string input)
    {
        SortedSet<string>[] deps = EmptyComponentDeps();
        for (int component = 0; component < 4; component++)
            deps[component].Add($"{input}.{ComponentSuffix(component)}");
        return deps;
    }

    private static SortedSet<string>[] UnionComponentDeps(params SortedSet<string>[][] sources)
    {
        SortedSet<string>[] deps = EmptyComponentDeps();
        foreach (SortedSet<string>[] source in sources)
        {
            for (int component = 0; component < 4; component++)
                AddRange(deps[component], source[component]);
        }

        return deps;
    }

    private static SortedSet<string>[] DotComponentDeps(
        SortedSet<string>[] source0,
        SortedSet<string>[] source1,
        int componentCount)
    {
        SortedSet<string>[] deps = EmptyComponentDeps();
        var dotDeps = new SortedSet<string>(StringComparer.Ordinal);
        for (int component = 0; component < componentCount; component++)
        {
            AddRange(dotDeps, source0[component]);
            AddRange(dotDeps, source1[component]);
        }

        for (int component = 0; component < 4; component++)
            AddRange(deps[component], dotDeps);

        return deps;
    }

    private static SortedSet<string>[] DstComponentDeps(SortedSet<string>[] source0, SortedSet<string>[] source1)
    {
        SortedSet<string>[] deps = EmptyComponentDeps();
        AddRange(deps[1], source0[1]);
        AddRange(deps[1], source1[1]);
        AddRange(deps[2], source0[2]);
        AddRange(deps[3], source1[3]);
        return deps;
    }

    private static SortedSet<string>[] GetOrAddComponentDeps(
        Dictionary<string, SortedSet<string>[]> map,
        string key)
    {
        if (!map.TryGetValue(key, out SortedSet<string>[]? value))
        {
            value = EmptyComponentDeps();
            map.Add(key, value);
        }

        return value;
    }

    private static SortedSet<string>[] EmptyComponentDeps()
    {
        return
        [
            new SortedSet<string>(StringComparer.Ordinal),
            new SortedSet<string>(StringComparer.Ordinal),
            new SortedSet<string>(StringComparer.Ordinal),
            new SortedSet<string>(StringComparer.Ordinal)
        ];
    }

    private static SortedSet<string>[] CloneComponentDeps(SortedSet<string>[] deps)
    {
        SortedSet<string>[] clone = EmptyComponentDeps();
        for (int component = 0; component < 4; component++)
            AddRange(clone[component], deps[component]);
        return clone;
    }

    private static void WriteMaskedComponentDeps(
        SortedSet<string>[] target,
        SortedSet<string>[] source,
        RsxVertexWriteMask writeMask)
    {
        for (int component = 0; component < 4; component++)
        {
            if ((writeMask & ComponentWriteMask(component)) ==
                RsxVertexWriteMask.None)
                continue;

            target[component].Clear();
            AddRange(target[component], source[component]);
        }
    }

    private static RsxVertexWriteMask ComponentWriteMask(int component) =>
        (RsxVertexWriteMask)(0x8 >> component);

    private static string ComponentSuffix(int component)
    {
        return component switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "w",
            _ => component.ToString()
        };
    }

    private static RsxSwizzleComponent[] RsxVertexSourceSwizzle(uint source)
    {
        return
        [
            (RsxSwizzleComponent)((source >> 14) & 0x3),
            (RsxSwizzleComponent)((source >> 12) & 0x3),
            (RsxSwizzleComponent)((source >> 10) & 0x3),
            (RsxSwizzleComponent)((source >> 8) & 0x3)
        ];
    }

    private static void ApplyVertexSlotDependency(
        RsxVertexInstruction instruction,
        bool scalar,
        Dictionary<int, SortedSet<string>> tempDeps,
        Dictionary<string, SortedSet<string>> outputDeps)
    {
        byte opcode = scalar ? instruction.ScaOpcode : instruction.VecOpcode;
        RsxVertexWriteMask writeMask = scalar
            ? instruction.ScalarWriteMask
            : instruction.VectorWriteMask;
        if (opcode == 0 || writeMask == RsxVertexWriteMask.None)
            return;

        SortedSet<string> deps = CollectVertexSlotDependencies(instruction, scalar, tempDeps);
        bool writesResult = scalar
            ? instruction.ScaResult
            : instruction.VecResult;
        if (writesResult && instruction.Result != RsxVertexResult.None)
        {
            AddRange(
                GetOrAdd(outputDeps, VertexOutputName(instruction.Result)),
                deps);
            return;
        }

        int temp = scalar
            ? instruction.ScaDestTemp
            : instruction.VecDestTemp;
        tempDeps[temp] = deps;
    }

    private static SortedSet<string> CollectVertexSlotDependencies(
        RsxVertexInstruction instruction,
        bool scalar,
        Dictionary<int, SortedSet<string>> tempDeps)
    {
        var deps = new SortedSet<string>(StringComparer.Ordinal);
        foreach (uint source in ActiveVertexSlotSources(instruction, scalar))
        {
            switch (RsxVertexInstruction.SourceRegisterKind(source))
            {
                case RsxVertexRegisterType.Temporary:
                    if (tempDeps.TryGetValue(
                            (int)((source >> 2) & 0x3f),
                            out SortedSet<string>? sourceDeps))
                        AddRange(deps, sourceDeps);
                    break;
                case RsxVertexRegisterType.Input:
                    deps.Add(VertexInputName(instruction.InputAttribute));
                    break;
            }
        }

        return deps;
    }

    private static IEnumerable<uint> ActiveVertexSlotSources(
        RsxVertexInstruction instruction,
        bool scalar)
    {
        if (scalar)
        {
            if (RsxShaderInstructionSet.VertexScalarOperandCount(
                    instruction.ScalarOpcode) > 0)
                yield return instruction.Source2;
            yield break;
        }

        RsxSourceSlotMask sourceMask =
            RsxShaderInstructionSet.VertexSourceMask(
                instruction.VectorOpcode);
        if ((sourceMask & RsxSourceSlotMask.Source0) != RsxSourceSlotMask.None)
            yield return instruction.Source0;
        if ((sourceMask & RsxSourceSlotMask.Source1) != RsxSourceSlotMask.None)
            yield return instruction.Source1;
        if ((sourceMask & RsxSourceSlotMask.Source2) != RsxSourceSlotMask.None)
            yield return instruction.Source2;
    }

    private static string FragmentInputName(RsxFragmentInputAttribute input)
    {
        return input switch
        {
            RsxFragmentInputAttribute.WindowPosition => "position",
            RsxFragmentInputAttribute.Color0 => "color0",
            RsxFragmentInputAttribute.Color1 => "color1",
            RsxFragmentInputAttribute.Fog => "fog",
            >= RsxFragmentInputAttribute.TextureCoordinate0 and
                <= RsxFragmentInputAttribute.TextureCoordinate7 =>
                $"texcoord{(byte)input - (byte)RsxFragmentInputAttribute.TextureCoordinate0}",
            _ => ""
        };
    }

    private static string VertexOutputForFragmentInput(string input)
    {
        if (input.StartsWith("texcoord", StringComparison.Ordinal) &&
            int.TryParse(input["texcoord".Length..], out int texCoord) &&
            texCoord is >= 0 and <= 7)
        {
            return VertexOutputName(
                (RsxVertexResult)(
                    (byte)RsxVertexResult.TextureCoordinate0 + texCoord));
        }

        return input switch
        {
            "color0" => VertexOutputName(RsxVertexResult.FrontColor0),
            "color1" => VertexOutputName(RsxVertexResult.FrontColor1),
            "fog" => VertexOutputName(
                RsxVertexResult.FogAndUserClip0To2),
            _ => ""
        };
    }

    private static string VertexInputForFragmentInput(string input)
    {
        if (input.StartsWith("texcoord", StringComparison.Ordinal) &&
            int.TryParse(input["texcoord".Length..], out int texCoord) &&
            texCoord is >= 0 and <= 7)
        {
            return VertexInputName(
                (RsxVertexInputAttribute)(
                    (byte)RsxVertexInputAttribute.TextureCoordinate0 +
                    texCoord));
        }

        return input switch
        {
            "color0" => VertexInputName(RsxVertexInputAttribute.Color0),
            "color1" => VertexInputName(RsxVertexInputAttribute.Color1),
            "fog" => VertexInputName(RsxVertexInputAttribute.Fog),
            _ => ""
        };
    }

    private static string VertexInputName(RsxVertexInputAttribute input)
    {
        return input switch
        {
            RsxVertexInputAttribute.Position => "v0/POS",
            RsxVertexInputAttribute.Weight => "v1/WEIGHT",
            RsxVertexInputAttribute.Normal => "v2/NORMAL",
            RsxVertexInputAttribute.Color0 => "v3/COL0",
            RsxVertexInputAttribute.Color1 => "v4/COL1",
            RsxVertexInputAttribute.Fog => "v5/FOGC",
            >= RsxVertexInputAttribute.TextureCoordinate0 and
                <= RsxVertexInputAttribute.TextureCoordinate7 =>
                $"v{(byte)input}/TEX{(byte)input - (byte)RsxVertexInputAttribute.TextureCoordinate0}",
            _ => $"v{(byte)input}/input"
        };
    }

    private static string VertexOutputName(RsxVertexResult output)
    {
        return output switch
        {
            RsxVertexResult.FrontColor0 => "o1/COL0",
            RsxVertexResult.FrontColor1 => "o2/COL1",
            RsxVertexResult.FogAndUserClip0To2 => "o5/FOGC",
            >= RsxVertexResult.TextureCoordinate0 and
                <= RsxVertexResult.TextureCoordinate7 =>
                $"o{(byte)output}/TEX{(byte)output - (byte)RsxVertexResult.TextureCoordinate0}",
            _ => $"o{(byte)output}/output"
        };
    }

    private static string VertexDeclarationDestinationInputName(
        MaterialStreamDestination dest)
    {
        return dest switch
        {
            MaterialStreamDestination.Position => "v0/POS",
            MaterialStreamDestination.Weight => "v1/WEIGHT",
            MaterialStreamDestination.Normal => "v2/NORMAL",
            MaterialStreamDestination.Color0 => "v3/COL0",
            MaterialStreamDestination.Color1 => "v4/COL1",
            MaterialStreamDestination.Fog => "v5/FOGC",
            >= MaterialStreamDestination.TexCoord0 and
                <= MaterialStreamDestination.TexCoord7 =>
                $"v{(byte)dest}/TEX{(byte)dest - (byte)MaterialStreamDestination.TexCoord0}",
            _ => $"dest[{(byte)dest:X2}]"
        };
    }

    private static SortedSet<string> GetOrAdd(Dictionary<string, SortedSet<string>> map, string key)
    {
        if (!map.TryGetValue(key, out SortedSet<string>? value))
        {
            value = new SortedSet<string>(StringComparer.Ordinal);
            map.Add(key, value);
        }

        return value;
    }

    private static void AddRange(SortedSet<string> target, IEnumerable<string> values)
    {
        foreach (string value in values)
            target.Add(value);
    }

    private static ReadOnlySpan<MaterialVertexStreamRouting> ActiveRoutes(
        VertexDeclarationCacheIdentity declaration) =>
        declaration.Routes.AsSpan(0, declaration.ActiveRouteCount);

    /// <summary>
    /// One owned view of every mutable input read by a route operation. The
    /// selected shader objects and names are captured once, their current data
    /// cells are copied once, and declaration routes are copied once. No cache
    /// downstream path reads the live pass again.
    /// </summary>
    private sealed class SamplerRouteInputSnapshot
    {
        private readonly RsxProgramSemanticCache _programSemanticCache;
        private RsxProgramSemanticSnapshot? _programSemantics;

        private SamplerRouteInputSnapshot(
            MaterialVertexDeclarationAsset vertexDeclarationReference,
            VertexDeclarationCacheIdentity vertexDeclaration,
            MaterialShaderAsset? vertexShaderReference,
            string? vertexShaderName,
            ProgramDataCacheIdentity vertexProgram,
            MaterialShaderAsset? pixelShaderReference,
            string? pixelShaderName,
            ProgramDataCacheIdentity pixelProgram,
            RsxProgramSemanticCache programSemanticCache,
            ushort samplerDestination)
        {
            VertexDeclarationReference = vertexDeclarationReference;
            VertexDeclaration = vertexDeclaration;
            VertexShaderReference = vertexShaderReference;
            VertexShaderName = vertexShaderName;
            VertexProgram = vertexProgram;
            PixelShaderReference = pixelShaderReference;
            PixelShaderName = pixelShaderName;
            PixelProgram = pixelProgram;
            _programSemanticCache = programSemanticCache;
            SamplerDestination = samplerDestination;
        }

        internal MaterialVertexDeclarationAsset VertexDeclarationReference
        {
            get;
        }

        internal VertexDeclarationCacheIdentity VertexDeclaration { get; }

        internal MaterialShaderAsset? VertexShaderReference { get; }

        internal string? VertexShaderName { get; }

        internal ProgramDataCacheIdentity VertexProgram { get; }

        internal MaterialShaderAsset? PixelShaderReference { get; }

        internal string? PixelShaderName { get; }

        internal ProgramDataCacheIdentity PixelProgram { get; }

        internal RsxProgramSemanticSnapshot ProgramSemantics =>
            _programSemantics ??= _programSemanticCache.Resolve(
                VertexProgram,
                PixelProgram);

        internal ushort SamplerDestination { get; }

        internal static SamplerRouteInputSnapshot Capture(
            MaterialPassAsset pass,
            ushort samplerDestination,
            MaterialVertexDeclarationAsset vertexDeclaration,
            RsxProgramSemanticCache programSemanticCache)
        {
            ArgumentNullException.ThrowIfNull(pass);
            ArgumentNullException.ThrowIfNull(vertexDeclaration);
            ArgumentNullException.ThrowIfNull(programSemanticCache);

            MaterialShaderAsset? vertexShader = pass.VertexShader;
            string? vertexShaderName = vertexShader?.Name;
            byte[]? vertexDataCell = vertexShader?.Data;
            MaterialShaderAsset? pixelShader = pass.PixelShader;
            string? pixelShaderName = pixelShader?.Name;
            byte[]? pixelDataCell = pixelShader?.Data;
            ProgramDataCacheIdentity vertexProgram =
                programSemanticCache.CaptureProgramIdentity(vertexDataCell);
            ProgramDataCacheIdentity pixelProgram =
                programSemanticCache.CaptureProgramIdentity(pixelDataCell);
            return new SamplerRouteInputSnapshot(
                vertexDeclaration,
                VertexDeclarationCacheIdentity.Capture(vertexDeclaration),
                vertexShader,
                vertexShaderName,
                vertexProgram,
                pixelShader,
                pixelShaderName,
                pixelProgram,
                programSemanticCache,
                samplerDestination);
        }

        internal SamplerRouteCacheKey CreateKey(int textureSemantic) => new(
            VertexDeclarationReference,
            VertexDeclaration,
            VertexShaderReference,
            VertexShaderName,
            VertexProgram,
            PixelShaderReference,
            PixelShaderName,
            PixelProgram,
            SamplerDestination,
            textureSemantic);
    }

    private sealed class SamplerRouteCacheEntry
    {
        private const int Pending = 0;
        private const int Executing = 1;
        private const int Completed = 2;
        private const int Faulted = 3;

        private readonly Lazy<SamplerRouteResult> _value;
        private int _executionState = Pending;

        internal SamplerRouteCacheEntry(Func<SamplerRouteResult> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _value = new Lazy<SamplerRouteResult>(
                () => Execute(factory),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal bool IsEvictable =>
            Volatile.Read(ref _executionState) >= Completed;

        internal bool IsFaulted =>
            Volatile.Read(ref _executionState) == Faulted;

        internal SamplerRouteResult GetValue() => _value.Value;

        private SamplerRouteResult Execute(Func<SamplerRouteResult> factory)
        {
            Volatile.Write(ref _executionState, Executing);
            try
            {
                SamplerRouteResult result = factory();
                Volatile.Write(ref _executionState, Completed);
                return result;
            }
            catch
            {
                Volatile.Write(ref _executionState, Faulted);
                throw;
            }
        }
    }

    private sealed class SamplerRouteCacheState
    {
        private readonly object _retentionGate = new();
        private readonly LinkedList<SamplerRouteCacheRegistration>
            _retentionOrder = new();
        private readonly ConcurrentDictionary<SamplerRouteCacheKey,
            SamplerRouteCacheEntry> _entries = new();
        private int _retainedEntryCount;

        internal SamplerRouteCacheEntry GetOrAdd(
            SamplerRouteCacheKey key,
            SamplerRouteInputSnapshot snapshot,
            int textureSemantic)
        {
            if (_entries.TryGetValue(key, out SamplerRouteCacheEntry? cached))
                return cached;

            // Serialize only retention metadata and cold insertion. The
            // entry's Lazy.Value runs after this lock, so distinct destinations
            // can decode in parallel while same-key callers share one winner.
            lock (_retentionGate)
            {
                if (_entries.TryGetValue(key, out cached))
                    return cached;

                var candidate = new SamplerRouteCacheEntry(
                    () => ComputeSamplerRoute(
                        snapshot,
                        textureSemantic));
                if (!_entries.TryAdd(key, candidate))
                {
                    // All writes use this gate, but retain a fail-closed path
                    // if that invariant changes in a future refactor.
                    return _entries[key];
                }

                Interlocked.Increment(ref _retainedEntryCount);
                _retentionOrder.AddLast(
                    new SamplerRouteCacheRegistration(key, candidate));
                TrimToCapacityLocked();
                return candidate;
            }
        }

        internal void CompleteLookup(
            SamplerRouteCacheKey key,
            SamplerRouteCacheEntry entry)
        {
            if (!entry.IsFaulted &&
                Volatile.Read(ref _retainedEntryCount) <=
                SamplerRouteCacheEntryCapacity)
            {
                return;
            }

            lock (_retentionGate)
            {
                if (entry.IsFaulted &&
                    _entries.TryRemove(
                        new KeyValuePair<SamplerRouteCacheKey,
                            SamplerRouteCacheEntry>(key, entry)))
                {
                    Interlocked.Decrement(ref _retainedEntryCount);
                    RemoveRegistrationLocked(entry);
                }

                // In-flight entries are allowed to exceed the cap temporarily.
                // Every successful or faulted Value completion retries the
                // trim, guaranteeing the quiescent cache returns to its bound.
                TrimToCapacityLocked();
            }
        }

        private void TrimToCapacityLocked()
        {
            int candidatesToInspect = _retentionOrder.Count;
            while (Volatile.Read(ref _retainedEntryCount) >
                       SamplerRouteCacheEntryCapacity &&
                   candidatesToInspect-- > 0 &&
                   _retentionOrder.First is { } oldestNode)
            {
                _retentionOrder.RemoveFirst();
                SamplerRouteCacheRegistration oldest = oldestNode.Value;
                if (!oldest.Value.IsEvictable)
                {
                    // Preserve single-flight for pending/executing entries.
                    // Completion calls this method again.
                    _retentionOrder.AddLast(oldestNode);
                    continue;
                }

                if (!_entries.TryRemove(
                        new KeyValuePair<SamplerRouteCacheKey,
                            SamplerRouteCacheEntry>(
                            oldest.Key,
                            oldest.Value)))
                {
                    continue;
                }

                Interlocked.Decrement(ref _retainedEntryCount);
            }
        }

        private void RemoveRegistrationLocked(SamplerRouteCacheEntry entry)
        {
            LinkedListNode<SamplerRouteCacheRegistration>? node =
                _retentionOrder.First;
            while (node is not null)
            {
                LinkedListNode<SamplerRouteCacheRegistration>? next =
                    node.Next;
                if (ReferenceEquals(node.Value.Value, entry))
                {
                    _retentionOrder.Remove(node);
                    return;
                }

                node = next;
            }
        }

        private readonly record struct SamplerRouteCacheRegistration(
            SamplerRouteCacheKey Key,
            SamplerRouteCacheEntry Value);
    }
}

internal sealed class RsxVertexOutputDependencyAnalysis
{
    internal RsxVertexOutputDependencyAnalysis(
        RsxVertexProgramIr? vertexProgramIr,
        IReadOnlyDictionary<string, SortedSet<string>> outputDependencies,
        IReadOnlyDictionary<string, SortedSet<string>[]>
            outputComponentDependencies)
    {
        ArgumentNullException.ThrowIfNull(outputDependencies);
        ArgumentNullException.ThrowIfNull(outputComponentDependencies);
        VertexProgramIr = vertexProgramIr;
        OutputDependencies = outputDependencies.ToImmutableDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<string>)pair.Value.ToImmutableSortedSet(
                StringComparer.Ordinal),
            StringComparer.Ordinal);
        OutputComponentDependencies = outputComponentDependencies
            .ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .Select(component =>
                        (IReadOnlySet<string>)component.ToImmutableSortedSet(
                            StringComparer.Ordinal))
                    .ToImmutableArray(),
                StringComparer.Ordinal);
    }

    internal RsxVertexProgramIr? VertexProgramIr { get; }

    internal IReadOnlyDictionary<string, IReadOnlySet<string>>
        OutputDependencies { get; }

    internal IReadOnlyDictionary<string, ImmutableArray<IReadOnlySet<string>>>
        OutputComponentDependencies { get; }

    internal static RsxVertexOutputDependencyAnalysis Empty { get; } = new(
        null,
        new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal),
        new Dictionary<string, SortedSet<string>[]>(StringComparer.Ordinal));
}
