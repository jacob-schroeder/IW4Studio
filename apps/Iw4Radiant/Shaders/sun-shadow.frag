in vec2 vTexCoord;
in float vAlpha;

uniform sampler2D uTexture;
#include "material-alpha.glsl"

void main()
{
    if (uAlphaTest != 0) applyMaterialAlpha(texture(uTexture, vTexCoord).a * vAlpha);
    // The orthographic projection supplies the linear OpenGL window depth.
}
