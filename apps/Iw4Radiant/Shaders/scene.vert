#define IW4_OPENGL
#include "ocean-waves.hlsl"

in vec3 aPosition;
in vec3 aNormal;
in vec2 aTexCoord;
in vec4 aColor;

uniform mat4 uViewProjection;
uniform mat4 uModel;
uniform mat4 uNormalTransform;
uniform bool uWaterPreview;
uniform vec4 uOceanShape;
uniform vec4 uOceanMotion;
uniform float uOceanTime;
uniform vec3 uEye;

out vec3 vNormal;
out vec3 vPosition;
out vec2 vTexCoord;
out vec4 vColor;
out vec2 vOceanSlope;
out vec4 vOceanSurface;

void main()
{
    vec3 position = (uModel * vec4(aPosition, 1.0)).xyz;
    vOceanSlope = vec2(0.0);
    vColor = aColor;
    float rise = 0.0;
    if (uWaterPreview && uOceanShape.w > 0.0)
    {
        vec2 waveSlope;
        float height = OceanHeight(position.xy * uOceanShape.z, uOceanTime * uOceanMotion.x,
            uOceanShape.xy, waveSlope) * uOceanShape.w;
        vec2 gradient = (aColor.gb * 255.0 - 128.0) / 127.0 * uOceanMotion.y;
        rise = aColor.r * height;
        position.z += rise;
        vOceanSlope = aColor.r * waveSlope * (uOceanShape.z * uOceanShape.w) + gradient * height;
    }
    vOceanSurface = vec4(position.xy / 128.0,
        aColor.a * uOceanMotion.z + uOceanMotion.w + rise / 12.0,
        rise / max(uOceanShape.w * 2.4, 0.0001));
    gl_Position = uViewProjection * vec4(position, 1.0);
    vec3 transformedNormal = (uNormalTransform * vec4(aNormal, 0.0)).xyz;
    vNormal = dot(transformedNormal, transformedNormal) > 0.000001
        ? normalize(transformedNormal) : vec3(0.0);
    vPosition = position;
    vTexCoord = aTexCoord;
}
