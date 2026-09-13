in vec3 aPosition;

uniform mat4 uViewProjection;

out vec3 vPosition;

void main()
{
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
    vPosition = aPosition;
}
