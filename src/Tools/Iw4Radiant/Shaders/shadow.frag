in vec3 vPosition;

uniform vec4 uLightPositionRadius;

void main()
{
    // Radial depth makes the six cube views share one distance comparison.
    gl_FragDepth = length(vPosition - uLightPositionRadius.xyz) / uLightPositionRadius.w;
}
