in vec3 aPosition;
in vec3 aNormal;
in vec2 aTexCoord;
in vec4 aColor;

uniform mat4 uViewProjection;
uniform bool uWaterPreview;
uniform vec4 uOceanFirst;
uniform vec4 uOceanSecond;
uniform float uOceanTime;
uniform float uOceanInverseFade;

out vec3 vNormal;
out vec3 vPosition;
out vec2 vTexCoord;
out vec4 vColor;
out vec2 vOceanSlope;

void main()
{
    vec3 position = aPosition;
    vOceanSlope = vec2(0.0);
    if (uWaterPreview)
    {
        float firstPhase = dot(position.xy, uOceanFirst.xy) + uOceanTime * uOceanFirst.z;
        float secondPhase = dot(position.xy, uOceanSecond.xy) + uOceanTime * uOceanSecond.z;
        firstPhase = fract(firstPhase * 0.15915494309189535 + 0.5) * 6.283185307179586 - 3.141592653589793;
        secondPhase = fract(secondPhase * 0.15915494309189535 + 0.5) * 6.283185307179586 - 3.141592653589793;
        float height = sin(firstPhase) * uOceanFirst.w + sin(secondPhase) * uOceanSecond.w;
        vec2 slope = cos(firstPhase) * uOceanFirst.w * uOceanFirst.xy
            + cos(secondPhase) * uOceanSecond.w * uOceanSecond.xy;
        vec2 edgeGradient = (aColor.gb * 255.0 - 128.0) / 127.0 * uOceanInverseFade;
        position.z += aColor.r * height;
        vOceanSlope = aColor.r * slope + edgeGradient * height;
    }
    gl_Position = uViewProjection * vec4(position, 1.0);
    vNormal = aNormal;
    vPosition = position;
    vTexCoord = aTexCoord;
    vColor = aColor;
}
