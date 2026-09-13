#ifdef GL_ES
precision highp samplerCube;
#endif

in vec3 vCubeDirection;
uniform samplerCube uSkyTexture;
out vec4 fragColor;

void main()
{
    fragColor = texture(uSkyTexture, normalize(vCubeDirection));
}
