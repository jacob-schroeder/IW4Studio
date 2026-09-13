in vec3 aPosition;
in vec3 aNormal;
in vec2 aTexCoord;
in vec3 aColor;

uniform mat4 uViewProjection;

out vec3 vNormal;
out vec2 vTexCoord;
out vec3 vColor;

void main()
{
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
    vNormal = aNormal;
    vTexCoord = aTexCoord;
    vColor = aColor;
}
