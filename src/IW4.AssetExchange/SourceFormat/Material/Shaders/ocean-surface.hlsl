// Shore/crest coverage follows CoD4 Ocean's FoamValueAlphaShore/FoamValue:
// two scrolling foam samples, noisy depth thresholds, and independent opacity.
// Adapted to IW4's authored seabed depth and bounded vertex displacement.
// https://github.com/xoxor4d/cod4-shader-ocean
#ifdef IW4_OPENGL
#define float2 vec2
#define float3 vec3
#define float4 vec4
#define lerp mix
#define saturate(x) clamp(x, 0.0, 1.0)
#endif

float2 OceanFoamCoordinate(float2 position, float time, float2 wind)
{
    return position + time * 0.035 * wind;
}

float2 OceanFoamCoordinate2(float2 position, float time, float2 wind)
{
    return position * -0.5 + time * 0.021 * float2(-wind.y, wind.x);
}

float OceanFoam(float depth, float crest, float first, float second)
{
    // Depth arrives unclamped; the pixel shader evaluates the moving shoreline
    // after interpolation, avoiding bands shaped like the underlying triangles.
    float shore = 1.0 - smoothstep(0.08 + first * 0.16, 0.55 + second * 0.45, depth);
    float shoreFoam = saturate((first + second * 4.5) * 0.55) * shore;
    float crestFoam = smoothstep(0.38, 0.8, crest) * saturate(first + second * 0.5) * 0.35;
    float wet = smoothstep(0.0, 0.16, depth);
    return min(shoreFoam + crestFoam, 0.9) * wet;
}

float OceanOpacity(float depth)
{
    // Beer-Lambert transmission over the authored water column (depth / 12).
    return 1.0 - exp2(-max(depth, 0.0) * 0.55);
}

float OceanCoverage(float depth, float foam)
{
    float opacity = OceanOpacity(depth);
    // Aerated water remains visible while the liquid column fades at contact.
    return opacity + foam * (1.0 - opacity);
}

float3 OceanSurfaceColor(float3 water, float3 reflection, float fresnel, float depth, float foam)
{
    float density = OceanOpacity(depth);
    float coverage = OceanCoverage(depth, foam);
    float3 body = water * lerp(0.65, 1.0, density);
    float3 color = lerp(body, reflection, fresnel);
    // Convert the foam-over-water premultiplied layers back to straight RGB.
    return lerp(color, float3(0.83, 0.89, 0.87), foam / max(coverage, 0.0001));
}

#ifdef IW4_OPENGL
#undef float2
#undef float3
#undef float4
#undef lerp
#undef saturate
#endif
