uniform sampler2D uScene;
uniform vec2 uSceneSize;
uniform float uBrightness;
uniform float uContrast;
uniform float uDesaturation;
uniform vec3 uLightTint;
uniform vec3 uDarkTint;
out vec4 fragmentColor;

void main()
{
    // The camera framebuffer holds display-encoded RGB. This is an artistic
    // preview adjustment in that domain, not the game's film-tweak pipeline.
    vec3 color = texture(uScene, gl_FragCoord.xy / uSceneSize).rgb;
    color = clamp((color - 0.5) * uContrast + 0.5 + uBrightness, 0.0, 1.0);
    float brightness = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(color, vec3(brightness), uDesaturation);
    color *= mix(uDarkTint, uLightTint, smoothstep(0.25, 0.75, brightness));
    fragmentColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}
