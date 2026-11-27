in vec3 aPosition;

uniform mat4 uViewProjection;
uniform vec3 uEye;

out vec3 vCubeDirection;

void main()
{
    // Radiant positions and exported cubemap axes both use game-space XYZ.
    vCubeDirection = aPosition - uEye;
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
}
