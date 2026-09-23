// Vertical wave spectrum adapted from xoxor4d/cod4-shader-ocean, commit
// 122716e5d96066c44a265fba6e3789a405a17d29; based on tuxalin/water-shader.
// Shared by ShaderConvert and Radiant's GPU preview. A height field keeps each
// vertex above its sampled seabed, so depth, contact foam and normals agree.
#ifdef IW4_OPENGL
#define float2 vec2
#define float3 vec3
#define float4 vec4
#define saturate(x) clamp(x, 0.0, 1.0)
#define OCEAN_FLATTEN
#endif
#ifndef IW4_OPENGL
#define OCEAN_FLATTEN [flatten]
#endif

float OceanHeight(float2 position, float timer, float2 windDir, out float2 slope)
{
    const float4 frequency = 6.2831853 / float4(1.0, 4.0, 3.0, 6.0);
    const float4 amplitude = float4(0.972, 0.54, 0.621, 2.6055)
        * normalize(float4(0.28, 1.95, 1.0, 1.9));
    // Crossing secondary waves retain the authored heading for the main swell.
    float2 across = float2(-windDir.y, windDir.x);
    float2 d0 = windDir * 0.819152 + across * 0.573576;
    float2 d1 = windDir * 0.906308 - across * 0.422618;
    float2 d2 = windDir * 0.422618 + across * 0.906308;
    float4 along = float4(dot(position, d0), dot(position, d1),
                          dot(position, d2), dot(position, windDir));
    float4 phase = along * (frequency * 0.06)
        + timer * float4(2.449490, 1.224745, 1.414214, 1.0)
        + float4(0.0, 1.7, 3.1, 4.4);
    float4 derivative = amplitude * (frequency * 0.06) * cos(phase);
    slope = derivative.x * d0 + derivative.y * d1 + derivative.z * d2 + derivative.w * windDir;
    return dot(amplitude, sin(phase));
}
