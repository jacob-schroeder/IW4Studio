in vec3 vNormal;
in vec2 vOceanSlope;
in vec3 vPosition;
in vec2 vTexCoord;
in vec4 vColor;

#include "material-alpha.glsl"

uniform sampler2D uTexture;
uniform bool uTextured;
uniform bool uLit;
uniform bool uPremultiplyAlpha;
uniform bool uIgnoreVertexColor;
uniform bool uWaterPreview;
uniform vec3 uEye;
uniform sampler2D uWaterHeight;
#ifdef GL_ES
precision highp samplerCube;
#endif
uniform samplerCube uWaterReflection;
uniform bool uHasWaterReflection;
uniform vec4 uEnvMapParms;
uniform bool uLinearCapture;
uniform vec4 uWaterColor;
uniform bool uCubicClip;
uniform vec3 uCubicClipCenter;
uniform float uCubicClipDistance;
uniform int uLightCount;
uniform sampler2D uLightData;
uniform sampler2D uShadowAtlas;
uniform ivec2 uShadowGrid;
uniform int uShadowTileSize;
uniform bool uSunEnabled;
uniform vec3 uSunDirection;
uniform vec3 uSunColor;
uniform mat4 uSunViewProjection;
uniform sampler2D uSunShadow;

out vec4 fragmentColor;

float sunVisibility(vec3 coordinate, vec2 depthGradient)
{
    // Never illuminate a receiver outside the shadow map's covered world bounds.
    if (any(lessThan(coordinate, vec3(0.0))) || any(greaterThan(coordinate, vec3(1.0))))
        return 0.0;
    ivec2 size = textureSize(uSunShadow, 0);
    ivec2 pixel = ivec2(floor(coordinate.xy * vec2(size)));
    float reference = coordinate.z - 0.00001;
    float visible = 0.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        ivec2 samplePixel = clamp(pixel + ivec2(x, y), ivec2(0), size - ivec2(1));
        // Compare at this tap's position on the receiving triangle, not the center pixel's depth.
        vec2 offset = (vec2(samplePixel) + 0.5) / vec2(size) - coordinate.xy;
        float receiverDepth = reference + dot(depthGradient, offset);
        visible += receiverDepth <= texelFetch(uSunShadow, samplePixel, 0).r ? 1.0 : 0.0;
    }
    return visible / 9.0;
}

float shadowVisibility(int lightIndex, vec3 fromLight, float radius, float diffuse, vec3 receiverPlane)
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
    float radialDepth = length(fromLight) / radius;
    float bias = max(0.0005, 0.002 * (1.0 - diffuse));
    float planeDistance = dot(receiverPlane, fromLight);
    float visible = 0.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        // Clamp PCF taps to this face so adjacent lights cannot bleed together.
        ivec2 samplePixel = clamp(pixel + ivec2(x, y), first, last);
        vec2 tap = (vec2(samplePixel - first) + 0.5) / float(uShadowTileSize) * 2.0 - 1.0;
        vec3 ray;
        if (face == 0) ray = vec3(1.0, -tap.y, -tap.x);
        else if (face == 1) ray = vec3(-1.0, -tap.y, tap.x);
        else if (face == 2) ray = vec3(tap.x, 1.0, tap.y);
        else if (face == 3) ray = vec3(tap.x, -1.0, -tap.y);
        else if (face == 4) ray = vec3(tap.x, -tap.y, 1.0);
        else ray = vec3(-tap.x, -tap.y, -1.0);
        // Radial depth is nonlinear in cube UV. Intersect this tap's ray with
        // the receiving triangle to compare the same world position as the atlas.
        float denominator = dot(receiverPlane, ray);
        float receiverDepth = radialDepth;
        if (abs(denominator) > 1e-20 && planeDistance / denominator > 0.0)
            receiverDepth = planeDistance / denominator * length(ray) / radius;
        visible += receiverDepth - bias <= texelFetch(uShadowAtlas, samplePixel, 0).r ? 1.0 : 0.0;
    }
    return visible / 9.0;
}

float waterHeight(vec2 uv)
{
    return texture(uWaterHeight, uv).r + 0.60009766 * texture(uWaterHeight, uv * 3.7).r
        + 0.36010742 * texture(uWaterHeight, uv * 13.69).r;
}

void main()
{
    // Derivatives must precede alpha discard and divergent lighting branches.
    vec3 sunCoordinate = (uSunViewProjection * vec4(vPosition, 1.0)).xyz * 0.5 + 0.5;
    vec3 receiverPlane = cross(dFdx(sunCoordinate), dFdy(sunCoordinate));
    vec2 sunDepthGradient = abs(receiverPlane.z) > 1e-20
        ? -receiverPlane.xy / receiverPlane.z : vec2(0.0);
    vec3 localReceiverPlane = cross(dFdx(vPosition), dFdy(vPosition));
    if (uCubicClip && any(greaterThan(abs(vPosition - uCubicClipCenter), vec3(uCubicClipDistance))))
        discard;
    vec4 surface = uTextured && uIgnoreVertexColor ? vec4(1.0) : vColor;
    if (uWaterPreview)
    {
        // Recovered PS3 water_l_nosun: scalar waves, three octaves, forward
        // differences in world XY, Fresnel and an encoded reflection probe.
        vec3 fromEye = vPosition - uEye;
        vec3 view = fromEye / max(length(fromEye), 1e-20);
        vec2 q = vTexCoord + view.xy * (0.5 - texture(uWaterHeight, vTexCoord * 0.5).r) * 0.0234375;
        float center = waterHeight(q);
        vec3 normal = normalize(vec3(waterHeight(q + vec2(0.00390625, 0.0)) - center - vOceanSlope.x,
            waterHeight(q + vec2(0.0, 0.00390625)) - center - vOceanSlope.y, 1.0));
        float side = gl_FrontFacing ? 1.0 : -1.0;
        normal *= side;
        vec3 direction = reflect(view, normal);
        direction.z = abs(direction.z) * side;
        vec3 reflected = uHasWaterReflection ? texture(uWaterReflection, direction).rgb : vec3(0.0);
        float facing = clamp(1.0 - abs(dot(view, normal)), 0.0, 1.0);
        float fresnel = clamp(uEnvMapParms.x + (uEnvMapParms.y - uEnvMapParms.x) * pow(facing, uEnvMapParms.z), 0.0, 1.0);
        vec3 linearColor = mix(abs(normal.z) * uWaterColor.rgb, reflected * reflected, fresnel);
        // Alpha carries baked distance to a real solid/water intersection, not
        // brush opacity. Match the native RSX foam and six-unit contact fade.
        float distanceToShore = clamp(vColor.a, 0.0, 1.0);
        float shore = 1.0 - smoothstep(0.0, 1.0, distanceToShore);
        float breakup = clamp((abs(normal.x) + abs(normal.y)) * 8.0 + 0.15, 0.0, 1.0);
        float foam = gl_FrontFacing ? shore * shore * breakup * 0.65 : 0.0;
        linearColor = mix(linearColor, vec3(0.72, 0.8, 0.78), foam);
        float opacity = gl_FrontFacing ? smoothstep(0.0, 0.25, distanceToShore) : 1.0;
        opacity += foam * (1.0 - opacity);
        // The native shader writes linear RGB with gammaWrite enabled. Match the
        // editor's squared-radiance capture domain; exact RSX display transfer is unverified.
        vec3 waterColor = sqrt(clamp(linearColor, 0.0, 1.0));
        applyMaterialAlpha(opacity);
        fragmentColor = vec4(uPremultiplyAlpha ? waterColor * opacity : waterColor, opacity);
        return;
    }
    else if (uTextured)
        surface *= texture(uTexture, vTexCoord);
    applyMaterialAlpha(surface.a);
    vec3 color = uLinearCapture ? surface.rgb * surface.rgb : surface.rgb;
    if (uLit)
    {
        vec3 normal = normalize(vNormal);
        if (!gl_FrontFacing)
            normal = -normal;
        vec3 illumination = vec3(0.25);
        if (uSunEnabled)
        {
            float diffuse = max(dot(normal, uSunDirection), 0.0);
            if (diffuse > 0.0)
                illumination += uSunColor * diffuse * sunVisibility(sunCoordinate, sunDepthGradient);
        }
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
            float visibility = shadowVisibility(i, -toLight, lightPositionRadius.w, diffuse, localReceiverPlane);
            illumination += lightColorExponent.rgb * attenuation * diffuse * visibility;
        }
        color *= illumination;
    }
    fragmentColor = vec4(uPremultiplyAlpha ? color * surface.a : color, surface.a);
}
