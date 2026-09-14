in vec3 aPosition;
in vec3 aNormal;
in vec2 aTexCoord;
in vec4 aColor;

uniform mat4 uViewProjection;

out vec3 vNormal;
out vec3 vPosition;
out vec2 vTexCoord;
out vec4 vColor;

void main()
{
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
    vNormal = aNormal;
    vPosition = aPosition;
    vTexCoord = aTexCoord;
    vColor = aColor;
}
