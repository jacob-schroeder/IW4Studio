#ifdef GL_ES
precision highp samplerCube;
#endif

in vec3 vCubeDirection;
uniform samplerCube uSkyTexture;
uniform bool uLinearCapture;
out vec4 fragColor;

void main()
{
    vec4 sampleColor = texture(uSkyTexture, normalize(vCubeDirection));
    vec3 linearColor = sampleColor.rgb * sampleColor.a;
    fragColor = uLinearCapture ? vec4(linearColor * linearColor, 1.0) : sampleColor;
}
