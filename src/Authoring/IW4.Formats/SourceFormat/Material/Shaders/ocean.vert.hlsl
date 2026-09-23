// Native IW4 world-water interface for the CoD4 Ocean wave implementation.
#include "ocean-waves.hlsl"
float4x4 viewProjectionMatrix : register(c0);
float4x4 worldMatrix : register(c4);
float4 gameTime : register(c8);
// XY wind direction, Z reference coordinate scale, W displacement scale.
float4 oceanShape : register(c9);
// X phase speed, Y envelope gradient scale, ZW signed-depth decoding / 12.
float4 oceanMotion : register(c10);
float4 nativeFog : register(c21);
struct VertexInput { float4 position : POSITION; float4 color : COLOR0; float2 uv : TEXCOORD0; };
struct PixelInput { float4 position : POSITION; float4 color : COLOR0; float4 uvSlope : TEXCOORD0; float4 surface : TEXCOORD1; float4 relativePositionFog : TEXCOORD2; };
PixelInput vs_main(VertexInput vertex)
{
    PixelInput pixel;
    float3 position = vertex.position.xyz;
    float4 relative = mul(float4(position, 1.0), worldMatrix);
    float2 slope = float2(0.0, 0.0);
    pixel.color = vertex.color;
    float rise = 0.0;
    OCEAN_FLATTEN if (oceanShape.w > 0.0)
    {
        float2 waveSlope;
        float height = OceanHeight(position.xy * oceanShape.z, gameTime.w * oceanMotion.x,
            oceanShape.xy, waveSlope) * oceanShape.w;
        float2 gradient = (vertex.color.gb * 255.0 - 128.0) / 127.0 * oceanMotion.y;
        slope = vertex.color.r * waveSlope * (oceanShape.z * oceanShape.w) + gradient * height;
        rise = vertex.color.r * height;
        position.z += rise;
    }
    // A texture-coordinate varying retains signed depth until pixel evaluation.
    // COLOR alpha would clamp before interpolation and produce polygon-shaped edges.
    pixel.surface = float4(position.xy / 128.0,
        vertex.color.a * oceanMotion.z + oceanMotion.w + rise / 12.0,
        rise / max(oceanShape.w * 2.4, 0.0001));
    pixel.uvSlope = float4(vertex.uv, slope);
    relative = mul(float4(position, 1.0), worldMatrix);
    pixel.position = mul(relative, viewProjectionMatrix);
    pixel.relativePositionFog = float4(relative.xyz,
        clamp(exp2((length(relative.xyz) * nativeFog.z + nativeFog.w) * 1.44269504089), nativeFog.y, 1.0));
    return pixel;
}
