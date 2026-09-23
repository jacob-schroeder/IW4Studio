// PS3 IW4 ocean surface. ShaderConvert compiles ps_main and ps_sun separately.
#include "ocean-surface.hlsl"
samplerCUBE oceanReflection : register(s1);
sampler2D normalMap : register(s5);
sampler2D foamMap : register(s6);
float4 waterColor : register(c0);
float4 envMapParms : register(c1);
float4 gameTime : register(c2);
float4 oceanShape : register(c3);
float4 oceanFogColor : register(c4);
float4 oceanSunDirection : register(c5);
float4 oceanSunColor : register(c6);

struct PixelInput
{
    float4 uvSlope : TEXCOORD0;
    // World XY / 128, signed water depth / 12, signed rise / authored height.
    float4 surface : TEXCOORD1;
    float4 relativePositionFog : TEXCOORD2;
};

float WaterHeight(float2 uv)
{
    return tex2D(normalMap, uv).r + 0.60009766 * tex2D(normalMap, uv * 3.7).r
        + 0.36010742 * tex2D(normalMap, uv * 13.69).r;
}

float4 OceanPixel(PixelInput pixel, float face, bool sunlight)
{
    float3 view = normalize(pixel.relativePositionFog.xyz);
    float2 q = pixel.uvSlope.xy + view.xy *
        (0.5 - tex2D(normalMap, pixel.uvSlope.xy * 0.5).r) * 0.0234375;
    float center = WaterHeight(q);
    float2 detail = float2(WaterHeight(q + float2(0.00390625, 0.0)) - center,
        WaterHeight(q + float2(0.0, 0.00390625)) - center) * 4.0;
    float3 normal = normalize(float3(detail - pixel.uvSlope.zw, 1.0));
    // Native world-water winding is back-facing on the upper side.
    float top = face < 0.0 ? 1.0 : 0.0;
    normal *= top * 2.0 - 1.0;
    float3 direction = reflect(view, normal);
    direction.z = abs(direction.z) * (top * 2.0 - 1.0);
    float3 reflection = texCUBE(oceanReflection, direction).rgb;
    reflection *= reflection; // Native IW4 reflection-probe encoding.
    float facing = saturate(1.0 - abs(dot(view, normal)));
    float fresnel = saturate(envMapParms.x + (envMapParms.y - envMapParms.x) * pow(facing, envMapParms.z));
    float first = tex2D(foamMap, OceanFoamCoordinate(pixel.surface.xy, gameTime.w, oceanShape.xy)).a;
    float second = tex2D(foamMap, OceanFoamCoordinate2(pixel.surface.xy, gameTime.w, oceanShape.xy)).a;
    float foam = OceanFoam(pixel.surface.z, pixel.surface.w, first, second) * top;
    float3 color = OceanSurfaceColor(waterColor.rgb, reflection, fresnel, pixel.surface.z, foam);
    if (sunlight)
    {
        float3 halfVector = normalize(oceanSunDirection.xyz - view);
        float specular = pow(saturate(dot(normal, halfVector)), 96.0) * top * (1.0 - foam);
        color += oceanSunColor.rgb * specular * 0.45;
    }
    float opacity = lerp(1.0, OceanCoverage(pixel.surface.z, foam), top);
    clip(opacity - 0.0039215686);
    color = lerp(oceanFogColor.rgb, color, pixel.relativePositionFog.w);
    return float4(color, opacity);
}

float4 ps_main(PixelInput pixel, float face : VFACE) : COLOR0 { return OceanPixel(pixel, face, false); }
float4 ps_sun(PixelInput pixel, float face : VFACE) : COLOR0 { return OceanPixel(pixel, face, true); }
