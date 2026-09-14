in vec3 aPosition;
in vec2 aTexCoord;
in vec4 aColor;

uniform mat4 uViewProjection;

out vec3 vPosition;
out vec2 vTexCoord;
out float vAlpha;

void main()
{
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
    vPosition = aPosition;
    vTexCoord = aTexCoord;
    vAlpha = aColor.a;
}
