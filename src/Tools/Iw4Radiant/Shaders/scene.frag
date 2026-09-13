in vec3 vNormal;
in vec3 vPosition;
in vec2 vTexCoord;
in vec3 vColor;

uniform sampler2D uTexture;
uniform bool uTextured;
uniform bool uLit;
uniform int uLightCount;
uniform sampler2D uLightData;
uniform sampler2D uShadowAtlas;
uniform ivec2 uShadowGrid;
uniform int uShadowTileSize;

out vec4 fragmentColor;

float shadowVisibility(int lightIndex, vec3 fromLight, float radialDepth, float diffuse)
{
    // Cube-view ordering and orientation match SceneShadows.ViewProjection.
    vec3 magnitude = abs(fromLight);
    vec2 coordinate;
    float major;
    int face;
    if (magnitude.x >= magnitude.y && magnitude.x >= magnitude.z)
    {
        major = magnitude.x;
        face = fromLight.x >= 0.0 ? 0 : 1;
        coordinate = vec2(fromLight.x >= 0.0 ? -fromLight.z : fromLight.z, -fromLight.y);
    }
    else if (magnitude.y >= magnitude.z)
    {
        major = magnitude.y;
        face = fromLight.y >= 0.0 ? 2 : 3;
        coordinate = vec2(fromLight.x, fromLight.y >= 0.0 ? fromLight.z : -fromLight.z);
    }
    else
    {
        major = magnitude.z;
        face = fromLight.z >= 0.0 ? 4 : 5;
        coordinate = vec2(fromLight.z >= 0.0 ? fromLight.x : -fromLight.x, -fromLight.y);
    }
    vec2 uv = coordinate / max(major, 0.000001) * 0.5 + 0.5;
    int tile = lightIndex * 6 + face;
    ivec2 first = ivec2(tile % uShadowGrid.x, tile / uShadowGrid.x) * uShadowTileSize;
    ivec2 last = first + ivec2(uShadowTileSize - 1);
    ivec2 pixel = first + ivec2(floor(uv * float(uShadowTileSize)));
    float reference = radialDepth - max(0.0005, 0.002 * (1.0 - diffuse));
    float visible = 0.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        // Clamp PCF taps to this face so adjacent lights cannot bleed together.
        ivec2 samplePixel = clamp(pixel + ivec2(x, y), first, last);
        visible += reference <= texelFetch(uShadowAtlas, samplePixel, 0).r ? 1.0 : 0.0;
    }
    return visible / 9.0;
}

void main()
{
    vec3 color = vColor;
    if (uTextured)
        color *= texture(uTexture, vTexCoord).rgb;
    if (uLit)
    {
        vec3 normal = normalize(vNormal);
        if (!gl_FrontFacing)
            normal = -normal;
        vec3 illumination = vec3(0.0);
        for (int i = 0; i < uLightCount; i++)
        {
            vec4 lightPositionRadius = texelFetch(uLightData, ivec2(0, i), 0);
            vec4 lightColorExponent = texelFetch(uLightData, ivec2(1, i), 0);
            vec3 toLight = lightPositionRadius.xyz - vPosition;
            float distanceToLight = length(toLight);
            float attenuation = max(1.0 - distanceToLight / lightPositionRadius.w, 0.0);
            if (attenuation <= 0.0)
                continue;
            vec4 directionOuterAngle = texelFetch(uLightData, ivec2(2, i), 0);
            vec2 innerAngleSpot = texelFetch(uLightData, ivec2(3, i), 0).xy;
            if (innerAngleSpot.y > 0.5)
            {
                float angle = acos(clamp(dot(directionOuterAngle.xyz,
                    -toLight / max(distanceToLight, 0.0001)), -1.0, 1.0));
                if (angle >= directionOuterAngle.w)
                    continue;
                if (lightColorExponent.w > 0.0)
                    attenuation *= pow(clamp((directionOuterAngle.w - angle) /
                        (directionOuterAngle.w - innerAngleSpot.x), 0.0, 1.0), lightColorExponent.w);
            }
            float diffuse = max(dot(normal, toLight / max(distanceToLight, 0.0001)), 0.0);
            if (diffuse <= 0.0)
                continue;
            float visibility = shadowVisibility(i, -toLight, distanceToLight / lightPositionRadius.w, diffuse);
            illumination += lightColorExponent.rgb * attenuation * diffuse * visibility;
        }
        color *= illumination;
    }
    fragmentColor = vec4(color, 1.0);
}
