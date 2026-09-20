uniform sampler2D uSource;
out vec4 result;
void main()
{
    // Same encoded domain as BrushReflectionCompiler; native water squares it.
    result = vec4(sqrt(clamp(texelFetch(uSource, ivec2(gl_FragCoord.xy), 0).rgb, 0.0, 1.0)), 1.0);
}
