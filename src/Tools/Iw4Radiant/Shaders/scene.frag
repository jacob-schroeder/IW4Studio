in vec3 vNormal;
in vec2 vTexCoord;
in vec3 vColor;

uniform sampler2D uTexture;
uniform bool uTextured;
uniform bool uLit;

out vec4 fragmentColor;

void main()
{
    vec3 color = vColor;
    if (uLit)
    {
        if (uTextured)
            color *= texture(uTexture, vTexCoord).rgb;
        vec3 normal = normalize(vNormal);
        if (!gl_FrontFacing)
            normal = -normal;
        float diffuse = max(dot(normal, normalize(vec3(-0.4, -0.6, 1.0))), 0.0);
        color *= 0.38 + 0.62 * diffuse;
    }
    fragmentColor = vec4(color, 1.0);
}
