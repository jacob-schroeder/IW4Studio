in vec3 vPosition;
in vec2 vTexCoord;
in float vAlpha;

uniform sampler2D uTexture;
#include "material-alpha.glsl"

uniform vec4 uLightPositionRadius;

void main()
{
    if (uAlphaTest != 0) applyMaterialAlpha(texture(uTexture, vTexCoord).a * vAlpha);
    // Radial depth makes the six cube views share one distance comparison.
    gl_FragDepth = length(vPosition - uLightPositionRadius.xyz) / uLightPositionRadius.w;
}
