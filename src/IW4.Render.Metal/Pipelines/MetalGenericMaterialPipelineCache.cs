using System.Runtime.Versioning;

using IW4.Render.Metal.Targets;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Techniques;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

/// <summary>
/// Native Metal implementation of the explicit generic-material fallback
/// contract. This is deliberately separate from RSX lowering: the generic IR
/// pair is a backend marker and its empty vertex instruction stream must never
/// be interpreted as an authored RSX program.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalGenericMaterialPipelineCache : IDisposable
{
    private const string WorldVertexEntryPoint =
        "iw4_generic_material_world_vertex";
    private const string StaticVertexEntryPoint =
        "iw4_generic_material_static_vertex";
    private const string FragmentEntryPoint =
        "iw4_generic_material_fragment";
    private const ulong ColorLayerCountFunctionConstant = 0;
    private const ulong NormalTextureMaskFunctionConstant = 1;
    private const ulong SpecularTextureMaskFunctionConstant = 2;
    private const ulong HasLightmapFunctionConstant = 3;
    private const ulong HasStaticModelLightingFunctionConstant = 4;

    private const string Source = """
        #include <metal_stdlib>
        using namespace metal;

        constant uint IW4_TEXTURED_VERTEX_FLOAT_COUNT = 22;
        constant int IW4_COLOR_LAYER_COUNT [[function_constant(0)]];
        constant int IW4_NORMAL_TEXTURE_MASK [[function_constant(1)]];
        constant int IW4_SPECULAR_TEXTURE_MASK [[function_constant(2)]];
        constant int IW4_HAS_LIGHTMAP [[function_constant(3)]];
        constant int IW4_HAS_STATIC_MODEL_LIGHTING [[function_constant(4)]];

        struct GenericMaterialConstants
        {
            float4x4 worldViewProjection;
            float4 cameraAndTime;
            float4 vegetationParameters;
            float4 vegetationBounds;
            // x=color layer count mirror, y=color-input linearization mask,
            // z=has lightmap mirror, w=lighting enabled.
            float4 materialFlags0;
            // Blend-weight components for color/normal/specular layers 1..4.
            float4 blendWeightComponents;
            // x=normal texture mask, y=specular texture mask,
            // z=alpha-test mode, w=output flags (bit 0=sRGB, bit 1=premultiply).
            float4 materialFlags1;
            // xyz=editor ambient, w=reserved.
            float4 ambientAndProbe;
            // xyz=authored light-ray direction, w=directional diffuse enabled.
            float4 sunDirectionAndDiffuse;
            // xyz=linear directional diffuse, w=directional specular enabled.
            float4 sunDiffuseAndSpecular;
            float4 sunSpecular;
            // x=fog enabled, y=active fog, z=sun fog enabled.
            float4 fogFlags;
            // xyz=fog color, w=atmosphere maximum opacity.
            float4 fogColorAndOpacity;
            // x=atmosphere start, y=atmosphere end,
            // z=active distance scale, w=active distance bias.
            float4 fogDistance;
            // x=minimum visibility, y=sun distance scale,
            // z=sun end cosine, w=sun angular scale.
            float4 fogMinimumAndSun;
            float4 sunFogColor;
            float4 sunFogDirection;
            // xyz=static model-lighting sampler transform.
            float4 staticModelLightingSamplerTransform;
        };

        struct GenericMaterialStageOut
        {
            float4 position [[position]];
            float2 uv0;
            float2 uv1;
            float2 uv2;
            float2 uv3;
            float2 uv4;
            float4 blendWeights;
            float2 lightmapUv;
            float3 renderPosition;
            float3 renderNormal;
            float4 staticModelBaseLightingCoords;
        };

        struct GenericMaterialFragmentOut
        {
            float4 color [[color(0)]];
            float depth [[depth(any)]];
        };

        static GenericMaterialStageOut composeVertex(
            device const float* vertices,
            uint vertexId,
            constant GenericMaterialConstants& constants,
            bool instanced,
            float4 staticModelBaseLightingCoords,
            float4 instanceRow0,
            float4 instanceRow1,
            float4 instanceRow2)
        {
            uint offset = vertexId * IW4_TEXTURED_VERTEX_FLOAT_COUNT;
            float3 localPosition = float3(
                vertices[offset + 0],
                vertices[offset + 1],
                vertices[offset + 2]);
            float4 homogeneousLocal = float4(localPosition, 1.0f);
            float3 renderPosition = instanced
                ? float3(
                    dot(instanceRow0, homogeneousLocal),
                    dot(instanceRow1, homogeneousLocal),
                    dot(instanceRow2, homogeneousLocal))
                : localPosition;
            float3 localNormal = float3(
                vertices[offset + 19],
                vertices[offset + 20],
                vertices[offset + 21]);
            float3 renderNormal = instanced
                ? float3(
                    dot(instanceRow0.xyz, localNormal),
                    dot(instanceRow1.xyz, localNormal),
                    dot(instanceRow2.xyz, localNormal))
                : localNormal;

            if (instanced &&
                constants.vegetationParameters.x != 0.0f &&
                constants.vegetationBounds.y > 0.0001f)
            {
                float heightWeight = clamp(
                    (localPosition.z - constants.vegetationBounds.x) /
                        constants.vegetationBounds.y,
                    0.0f,
                    1.0f);
                heightWeight *= heightWeight;
                float phase =
                    constants.cameraAndTime.w *
                        constants.vegetationParameters.z +
                    renderPosition.x * constants.vegetationParameters.w +
                    renderPosition.z * constants.vegetationParameters.w *
                        1.37f;
                float wave = (
                    sin(phase) +
                    0.35f * sin(phase * 0.61f + 1.7f)) / 1.35f;
                float sway = constants.vegetationParameters.y *
                    heightWeight * wave;
                renderPosition.x += sway;
                renderPosition.z += sway * 0.35f;
            }

            GenericMaterialStageOut result;
            result.position = constants.worldViewProjection *
                float4(renderPosition, 1.0f);
            result.uv0 = float2(vertices[offset + 3], vertices[offset + 4]);
            result.uv1 = float2(vertices[offset + 5], vertices[offset + 6]);
            result.uv2 = float2(vertices[offset + 7], vertices[offset + 8]);
            result.uv3 = float2(vertices[offset + 9], vertices[offset + 10]);
            result.uv4 = float2(vertices[offset + 11], vertices[offset + 12]);
            result.blendWeights = float4(
                vertices[offset + 13],
                vertices[offset + 14],
                vertices[offset + 15],
                vertices[offset + 16]);
            result.lightmapUv = float2(
                vertices[offset + 17],
                vertices[offset + 18]);
            result.renderPosition = renderPosition;
            result.renderNormal = dot(renderNormal, renderNormal) > 0.000001f
                ? normalize(renderNormal)
                : float3(0.0f);
            result.staticModelBaseLightingCoords =
                staticModelBaseLightingCoords;
            return result;
        }

        vertex GenericMaterialStageOut iw4_generic_material_world_vertex(
            uint vertexId [[vertex_id]],
            device const float* vertices [[buffer(0)]],
            constant GenericMaterialConstants& constants [[buffer(1)]])
        {
            return composeVertex(
                vertices,
                vertexId,
                constants,
                false,
                float4(0.0f),
                float4(0.0f),
                float4(0.0f),
                float4(0.0f));
        }

        vertex GenericMaterialStageOut iw4_generic_material_static_vertex(
            uint vertexId [[vertex_id]],
            uint instanceId [[instance_id]],
            device const float* vertices [[buffer(0)]],
            constant GenericMaterialConstants& constants [[buffer(1)]],
            device const float4* instances [[buffer(2)]])
        {
            uint instanceOffset = instanceId *
                (IW4_HAS_STATIC_MODEL_LIGHTING != 0 ? 4 : 3);
            uint placementOffset = instanceOffset +
                (IW4_HAS_STATIC_MODEL_LIGHTING != 0 ? 1 : 0);
            return composeVertex(
                vertices,
                vertexId,
                constants,
                true,
                IW4_HAS_STATIC_MODEL_LIGHTING != 0
                    ? instances[instanceOffset]
                    : float4(0.0f),
                instances[placementOffset + 0],
                instances[placementOffset + 1],
                instances[placementOffset + 2]);
        }

        static float selectBlendComponent(float4 weights, int component)
        {
            return component == 0 ? weights.x :
                component == 1 ? weights.y :
                component == 2 ? weights.z : weights.w;
        }

        static float layerWeight(
            float4 weights,
            int component,
            float textureAlpha)
        {
            if (component < 0)
                return textureAlpha;
            return clamp(
                selectBlendComponent(weights, component) * textureAlpha,
                0.0f,
                1.0f);
        }

        static float controlWeight(float4 weights, int component)
        {
            if (component < 0)
                return 1.0f;
            return clamp(selectBlendComponent(weights, component), 0.0f, 1.0f);
        }

        static float4 linearizeColorInput(
            float4 encoded,
            int mask,
            int layerBit)
        {
            if ((mask & layerBit) != 0)
                encoded.rgb *= encoded.rgb;
            return encoded;
        }

        static float3 surfaceNormal(
            GenericMaterialStageOut stage,
            bool frontFacing)
        {
            float3 normal = stage.renderNormal;
            if (dot(normal, normal) <= 0.000001f)
            {
                normal = normalize(cross(
                    dfdx(stage.renderPosition),
                    dfdy(stage.renderPosition)));
            }
            normal = normalize(normal);
            return frontFacing ? normal : -normal;
        }

        static float3 decodeEditorNormal(float4 encoded)
        {
            float2 xy = float2(encoded.a, encoded.g) * 2.0f - 1.0f;
            float z = sqrt(max(1.0f - dot(xy, xy), 0.0f));
            return normalize(float3(xy, z));
        }

        static float3 applyEditorNormalMap(
            GenericMaterialStageOut stage,
            float3 baseNormal,
            float4 encoded,
            float2 uv)
        {
            float3 dp1 = dfdx(stage.renderPosition);
            float3 dp2 = dfdy(stage.renderPosition);
            float2 duv1 = dfdx(uv);
            float2 duv2 = dfdy(uv);
            float3 dp2Perp = cross(dp2, baseNormal);
            float3 dp1Perp = cross(baseNormal, dp1);
            float3 tangent = dp2Perp * duv1.x + dp1Perp * duv2.x;
            float3 bitangent = dp2Perp * duv1.y + dp1Perp * duv2.y;
            float maximumLength = max(
                dot(tangent, tangent),
                dot(bitangent, bitangent));
            if (maximumLength <= 0.00000001f)
                return baseNormal;
            float inverseLength = rsqrt(maximumLength);
            float3x3 tangentFrame = float3x3(
                tangent * inverseLength,
                bitangent * inverseLength,
                baseNormal);
            return normalize(tangentFrame * decodeEditorNormal(encoded));
        }

        static float4 sampleStaticModelLighting(
            GenericMaterialStageOut stage,
            float3 renderNormal,
            constant GenericMaterialConstants& constants,
            texture3d<float> staticModelLighting,
            sampler staticModelLightingSampler)
        {
            float3 gameNormal = normalize(float3(
                renderNormal.x,
                -renderNormal.z,
                renderNormal.y));
            float3 coordinates =
                stage.staticModelBaseLightingCoords.xyz +
                gameNormal *
                    constants.staticModelLightingSamplerTransform.xyz;
            return staticModelLighting.sample(
                staticModelLightingSampler,
                coordinates);
        }

        IW4_GENERIC_FRAGMENT_RETURN_TYPE iw4_generic_material_fragment(
            GenericMaterialStageOut stage [[stage_in]],
            bool frontFacing [[front_facing]],
            constant GenericMaterialConstants& constants [[buffer(0)]],
            texture2d<float> color0 [[texture(0)]],
            texture2d<float> color1 [[texture(1)]],
            texture2d<float> color2 [[texture(2)]],
            texture2d<float> color3 [[texture(3)]],
            texture2d<float> color4 [[texture(4)]],
            texture2d<float> lightmap [[texture(5)]],
            texture2d<float> normal0 [[texture(6)]],
            texture2d<float> normal1 [[texture(7)]],
            texture2d<float> normal2 [[texture(8)]],
            texture2d<float> normal3 [[texture(9)]],
            texture2d<float> specular0 [[texture(10)]],
            texture2d<float> specular1 [[texture(11)]],
            texture2d<float> specular2 [[texture(12)]],
            texture3d<float> staticModelLighting [[texture(13)]],
            sampler colorSampler0 [[sampler(0)]],
            sampler colorSampler1 [[sampler(1)]],
            sampler colorSampler2 [[sampler(2)]],
            sampler colorSampler3 [[sampler(3)]],
            sampler colorSampler4 [[sampler(4)]],
            sampler lightmapSampler [[sampler(5)]],
            sampler normalSampler0 [[sampler(6)]],
            sampler normalSampler1 [[sampler(7)]],
            sampler normalSampler2 [[sampler(8)]],
            sampler normalSampler3 [[sampler(9)]],
            sampler specularSampler0 [[sampler(10)]],
            sampler specularSampler1 [[sampler(11)]],
            sampler specularSampler2 [[sampler(12)]],
            sampler staticModelLightingSampler [[sampler(13)]]
            IW4_GENERIC_DEPTH_BIAS_PARAMETER)
        {
            IW4_GENERIC_DEPTH_BIAS_PRELUDE
            int colorLayerCount = IW4_COLOR_LAYER_COUNT;
            int normalMask = IW4_NORMAL_TEXTURE_MASK;
            int specularMask = IW4_SPECULAR_TEXTURE_MASK;
            int blendComponent1 = int(round(constants.blendWeightComponents.x));
            int blendComponent2 = int(round(constants.blendWeightComponents.y));
            int blendComponent3 = int(round(constants.blendWeightComponents.z));
            int blendComponent4 = int(round(constants.blendWeightComponents.w));
            int colorInputLinearizationMask =
                int(round(constants.materialFlags0.y));

            float4 color = linearizeColorInput(
                color0.sample(colorSampler0, stage.uv0),
                colorInputLinearizationMask,
                1);
            if (colorLayerCount > 1)
            {
                float4 layer = linearizeColorInput(
                    color1.sample(colorSampler1, stage.uv1),
                    colorInputLinearizationMask,
                    2);
                float weight = layerWeight(
                    stage.blendWeights,
                    blendComponent1,
                    layer.a);
                color = float4(
                    mix(color.rgb, layer.rgb, weight),
                    max(color.a, weight));
            }
            if (colorLayerCount > 2)
            {
                float4 layer = linearizeColorInput(
                    color2.sample(colorSampler2, stage.uv2),
                    colorInputLinearizationMask,
                    4);
                float weight = layerWeight(
                    stage.blendWeights,
                    blendComponent2,
                    layer.a);
                color = float4(
                    mix(color.rgb, layer.rgb, weight),
                    max(color.a, weight));
            }
            if (colorLayerCount > 3)
            {
                float4 layer = linearizeColorInput(
                    color3.sample(colorSampler3, stage.uv3),
                    colorInputLinearizationMask,
                    8);
                float weight = layerWeight(
                    stage.blendWeights,
                    blendComponent3,
                    layer.a);
                color = float4(
                    mix(color.rgb, layer.rgb, weight),
                    max(color.a, weight));
            }
            if (colorLayerCount > 4)
            {
                float4 layer = linearizeColorInput(
                    color4.sample(colorSampler4, stage.uv4),
                    colorInputLinearizationMask,
                    16);
                float weight = layerWeight(
                    stage.blendWeights,
                    blendComponent4,
                    layer.a);
                color = float4(
                    mix(color.rgb, layer.rgb, weight),
                    max(color.a, weight));
            }

            int alphaTestMode = int(round(constants.materialFlags1.z));
            if ((alphaTestMode == 1 && !(color.a > 0.0f)) ||
                (alphaTestMode == 2 &&
                    !(color.a < (128.0f / 255.0f))) ||
                (alphaTestMode == 3 &&
                    !(color.a >= (128.0f / 255.0f))))
            {
                discard_fragment();
            }

            bool lightingEnabled = constants.materialFlags0.w != 0.0f;
            bool hasDirectionalDiffuse =
                constants.sunDirectionAndDiffuse.w != 0.0f;
            bool hasDirectionalSpecular =
                constants.sunDiffuseAndSpecular.w != 0.0f;
            float3 normal = float3(0.0f, 0.0f, 1.0f);
            if (lightingEnabled &&
                (hasDirectionalDiffuse ||
                 hasDirectionalSpecular ||
                 IW4_HAS_STATIC_MODEL_LIGHTING != 0))
            {
                float3 geometric = surfaceNormal(stage, frontFacing);
                normal = geometric;
                if ((normalMask & 1) != 0)
                {
                    normal = applyEditorNormalMap(
                        stage,
                        geometric,
                        normal0.sample(normalSampler0, stage.uv0),
                        stage.uv0);
                }
                if ((normalMask & 2) != 0)
                {
                    float3 layer = applyEditorNormalMap(
                        stage,
                        geometric,
                        normal1.sample(normalSampler1, stage.uv1),
                        stage.uv1);
                    normal = normalize(mix(
                        normal,
                        layer,
                        controlWeight(stage.blendWeights, blendComponent1)));
                }
                if ((normalMask & 4) != 0)
                {
                    float3 layer = applyEditorNormalMap(
                        stage,
                        geometric,
                        normal2.sample(normalSampler2, stage.uv2),
                        stage.uv2);
                    normal = normalize(mix(
                        normal,
                        layer,
                        controlWeight(stage.blendWeights, blendComponent2)));
                }
                if ((normalMask & 8) != 0)
                {
                    float3 layer = applyEditorNormalMap(
                        stage,
                        geometric,
                        normal3.sample(normalSampler3, stage.uv3),
                        stage.uv3);
                    normal = normalize(mix(
                        normal,
                        layer,
                        controlWeight(stage.blendWeights, blendComponent3)));
                }
            }

            float primaryLightVisibility = 1.0f;
            float4 encodedStaticModelLighting = float4(0.0f);
            if (lightingEnabled &&
                IW4_HAS_STATIC_MODEL_LIGHTING != 0)
            {
                encodedStaticModelLighting = sampleStaticModelLighting(
                    stage,
                    normal,
                    constants,
                    staticModelLighting,
                    staticModelLightingSampler);
                primaryLightVisibility = encodedStaticModelLighting.a;
            }

            if (IW4_HAS_LIGHTMAP != 0)
            {
                color.rgb *= lightmap.sample(
                    lightmapSampler,
                    stage.lightmapUv).rgb;
            }
            else if (lightingEnabled)
            {
                float3 irradiance;
                if (IW4_HAS_STATIC_MODEL_LIGHTING != 0)
                {
                    float3 expandedLighting =
                        encodedStaticModelLighting.rgb * 2.0f;
                    irradiance = expandedLighting * expandedLighting;
                    if (hasDirectionalDiffuse)
                    {
                        float nDotL = max(dot(
                            normalize(normal),
                            -constants.sunDirectionAndDiffuse.xyz), 0.0f);
                        irradiance += constants.sunDiffuseAndSpecular.rgb *
                            nDotL * primaryLightVisibility;
                    }
                }
                else
                {
                    irradiance = constants.ambientAndProbe.rgb;
                    if (hasDirectionalDiffuse)
                    {
                        float nDotL = max(dot(
                            normalize(normal),
                            -constants.sunDirectionAndDiffuse.xyz), 0.0f);
                        irradiance += constants.sunDiffuseAndSpecular.rgb *
                            nDotL;
                    }
                }
                color.rgb *= irradiance;
            }

            if (lightingEnabled &&
                IW4_SPECULAR_TEXTURE_MASK != 0 &&
                hasDirectionalSpecular)
            {
                float specular = (specularMask & 1) != 0
                    ? specular0.sample(specularSampler0, stage.uv0).r
                    : 0.0f;
                if ((specularMask & 2) != 0)
                {
                    specular = mix(
                        specular,
                        specular1.sample(specularSampler1, stage.uv1).r,
                        controlWeight(stage.blendWeights, blendComponent1));
                }
                if ((specularMask & 4) != 0)
                {
                    specular = mix(
                        specular,
                        specular2.sample(specularSampler2, stage.uv2).r,
                        controlWeight(stage.blendWeights, blendComponent2));
                }
                specular = clamp(specular, 0.0f, 1.0f);
                float3 toLight = -constants.sunDirectionAndDiffuse.xyz;
                float3 toCamera = normalize(
                    constants.cameraAndTime.xyz - stage.renderPosition);
                float3 halfVector = normalize(toLight + toCamera);
                float highlight = pow(
                    max(dot(normal, halfVector), 0.0f),
                    32.0f);
                color.rgb += constants.sunSpecular.rgb * specular *
                    highlight * primaryLightVisibility;
            }

            if (constants.fogFlags.x != 0.0f)
            {
                float3 cameraOffset = stage.renderPosition -
                    constants.cameraAndTime.xyz;
                float cameraDistance = sqrt(max(
                    dot(cameraOffset, cameraOffset),
                    0.0000001f));
                if (constants.fogFlags.y != 0.0f)
                {
                    constexpr float naturalExponentToBase2 =
                        1.4426950408889634f;
                    float fogVisibility = max(
                        exp2((constants.fogDistance.z * cameraDistance +
                            constants.fogDistance.w) *
                            naturalExponentToBase2),
                        constants.fogMinimumAndSun.x);
                    float visibility = fogVisibility;
                    float3 resolvedFogColor =
                        constants.fogColorAndOpacity.rgb;
                    if (constants.fogFlags.z != 0.0f)
                    {
                        float directionalCosine = dot(
                            cameraOffset / cameraDistance,
                            constants.sunFogDirection.xyz);
                        float sunFogFactor = clamp(
                            (directionalCosine -
                                constants.fogMinimumAndSun.z) *
                                constants.fogMinimumAndSun.w,
                            0.0f,
                            1.0f);
                        float sunFogVisibility = max(
                            exp2((constants.fogMinimumAndSun.y *
                                cameraDistance + constants.fogDistance.w) *
                                naturalExponentToBase2),
                            constants.fogMinimumAndSun.x);
                        visibility = clamp(
                            sunFogFactor *
                                (sunFogVisibility - fogVisibility) +
                                fogVisibility,
                            0.0f,
                            1.0f);
                        resolvedFogColor = mix(
                            constants.fogColorAndOpacity.rgb,
                            constants.sunFogColor.rgb,
                            sunFogFactor);
                    }
                    color.rgb = mix(
                        resolvedFogColor,
                        color.rgb,
                        clamp(visibility, 0.0f, 1.0f));
                }
                else
                {
                    float fogRange = max(
                        constants.fogDistance.y - constants.fogDistance.x,
                        0.0001f);
                    float fogFactor = clamp(
                        (cameraDistance - constants.fogDistance.x) / fogRange,
                        0.0f,
                        1.0f) * constants.fogColorAndOpacity.w;
                    color.rgb = mix(
                        color.rgb,
                        constants.fogColorAndOpacity.rgb,
                        fogFactor);
                }
            }

            int outputFlags = int(round(constants.materialFlags1.w));
            if ((outputFlags & 1) != 0)
            {
                float3 low = color.rgb * 12.92f;
                float3 high = 1.055f *
                    pow(color.rgb, float3(1.0f / 2.4f)) - 0.055f;
                color.rgb = clamp(
                    select(
                        high,
                        low,
                        color.rgb < float3(0.0031308f)),
                    float3(0.0f),
                    float3(1.0f));
            }
            if ((outputFlags & 2) != 0)
                color.rgb *= color.a;
            IW4_GENERIC_FRAGMENT_RETURN(color, stage.position.z)
        }
        """;

    private readonly MTLDevice _device;
    private readonly MTLPixelFormat _depthStencilFormat;
    private readonly bool _emulatesDepth24;
    private readonly Dictionary<PipelineKey, MetalGenericMaterialPipeline>
        _pipelines = [];
    private readonly Dictionary<PipelineKey, string> _failures = [];
    private MTLLibrary _library;
    private string? _libraryFailure;
    private bool _disposed;

    internal MetalGenericMaterialPipelineCache(
        MTLDevice device,
        MetalDepthStencilFormatSelection depthStencilFormat)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(depthStencilFormat);
        _device = device;
        _depthStencilFormat = depthStencilFormat.PixelFormat;
        _emulatesDepth24 = depthStencilFormat.EmulatesDepth24;
    }

    internal bool TryGetOrCreate(
        RenderNormalCameraPreparedPassSnapshot pass,
        int colorLayerCount,
        int normalTextureMask,
        int specularTextureMask,
        bool hasLightmap,
        bool usesStaticModelLighting,
        out MetalGenericMaterialPipeline? pipeline,
        out string blocker)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pass);

        bool usesStaticModelInstancing =
            pass.SourceKind == RenderNormalCameraDrawSourceKind.StaticModel;
        var key = new PipelineKey(
            usesStaticModelInstancing,
            colorLayerCount,
            normalTextureMask,
            specularTextureMask,
            hasLightmap,
            usesStaticModelLighting,
            pass.Geometry.Topology,
            pass.SourceState.ColorMask,
            pass.SourceState.BlendEnabled,
            pass.SourceState.BlendEquationRgb,
            pass.SourceState.BlendEquationAlpha,
            pass.SourceState.BlendSourceRgb,
            pass.SourceState.BlendSourceAlpha,
            pass.SourceState.BlendDestinationRgb,
            pass.SourceState.BlendDestinationAlpha);
        if (_pipelines.TryGetValue(key, out MetalGenericMaterialPipeline? cached))
        {
            pipeline = cached;
            blocker = string.Empty;
            return true;
        }
        if (_failures.TryGetValue(key, out string? failure))
        {
            pipeline = null;
            blocker = failure;
            return false;
        }

        if (!EnsureLibrary(out blocker))
            return Fail(key, blocker, out pipeline, out blocker);

        MTLFunction vertexFunction = default;
        MTLFunction fragmentFunction = default;
        try
        {
            using (var constants = new MTLFunctionConstantValues())
            {
                SetIntFunctionConstant(
                    constants,
                    key.ColorLayerCount,
                    ColorLayerCountFunctionConstant);
                SetIntFunctionConstant(
                    constants,
                    key.NormalTextureMask,
                    NormalTextureMaskFunctionConstant);
                SetIntFunctionConstant(
                    constants,
                    key.SpecularTextureMask,
                    SpecularTextureMaskFunctionConstant);
                SetIntFunctionConstant(
                    constants,
                    key.HasLightmap ? 1 : 0,
                    HasLightmapFunctionConstant);
                SetIntFunctionConstant(
                    constants,
                    key.UsesStaticModelLighting ? 1 : 0,
                    HasStaticModelLightingFunctionConstant);
                NSError vertexFunctionError = default;
                vertexFunction = _library.NewFunction(
                    usesStaticModelInstancing
                        ? StaticVertexEntryPoint
                        : WorldVertexEntryPoint,
                    constants,
                    ref vertexFunctionError);
                if (vertexFunction.NativePtr == 0 ||
                    vertexFunctionError.NativePtr != 0)
                {
                    return Fail(
                        key,
                        "metalPipeline=GENERIC_VERTEX_SPECIALIZATION_" +
                        Describe(vertexFunctionError),
                        out pipeline,
                        out blocker);
                }
                NSError functionError = default;
                fragmentFunction = _library.NewFunction(
                    FragmentEntryPoint,
                    constants,
                    ref functionError);
                if (fragmentFunction.NativePtr == 0 ||
                    functionError.NativePtr != 0)
                {
                    return Fail(
                        key,
                        "metalPipeline=GENERIC_FRAGMENT_SPECIALIZATION_" +
                        Describe(functionError),
                        out pipeline,
                        out blocker);
                }
            }

            var descriptor = new MTLRenderPipelineDescriptor
            {
                VertexFunction = vertexFunction,
                FragmentFunction = fragmentFunction,
                RasterSampleCount = MetalFrameTargets.SceneSampleCount,
                InputPrimitiveTopology = ToTopologyClass(
                    pass.Geometry.Topology),
                DepthAttachmentPixelFormat = _depthStencilFormat,
                StencilAttachmentPixelFormat = _depthStencilFormat
            };
            try
            {
                MetalRenderStateCache.ConfigureColorAttachment(
                    descriptor.ColorAttachments.Object(0),
                    pass.SourceState,
                    MetalFrameTargets.SceneColorFormat);
                NSError error = default;
                MTLRenderPipelineState state =
                    _device.NewRenderPipelineState(descriptor, ref error);
                if (state.NativePtr == 0 || error.NativePtr != 0)
                {
                    if (state.NativePtr != 0)
                        state.Dispose();
                    return Fail(
                        key,
                        $"metalPipeline={Describe(error)}",
                        out pipeline,
                        out blocker);
                }
                pipeline = new MetalGenericMaterialPipeline(
                    state,
                    usesStaticModelInstancing);
                _pipelines.Add(key, pipeline);
                blocker = string.Empty;
                return true;
            }
            finally
            {
                descriptor.Dispose();
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException or
            OverflowException)
        {
            return Fail(
                key,
                $"metalPipeline={exception.Message}",
                out pipeline,
                out blocker);
        }
        finally
        {
            if (fragmentFunction.NativePtr != 0)
                fragmentFunction.Dispose();
            if (vertexFunction.NativePtr != 0)
                vertexFunction.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (MetalGenericMaterialPipeline pipeline in _pipelines.Values)
            pipeline.Dispose();
        _pipelines.Clear();
        _failures.Clear();
        if (_library.NativePtr != 0)
        {
            _library.Dispose();
            _library = default;
        }
    }

    private bool EnsureLibrary(out string blocker)
    {
        if (_library.NativePtr != 0)
        {
            blocker = string.Empty;
            return true;
        }
        if (_libraryFailure is not null)
        {
            blocker = _libraryFailure;
            return false;
        }

        using var options = new MTLCompileOptions
        {
            FastMathEnabled = false
        };
        NSError error = default;
        string source = Source.Replace(
            "IW4_GENERIC_FRAGMENT_RETURN_TYPE",
            _emulatesDepth24
                ? "fragment GenericMaterialFragmentOut"
                : "fragment float4",
            StringComparison.Ordinal).Replace(
            "IW4_GENERIC_DEPTH_BIAS_PARAMETER",
            _emulatesDepth24
                ? $",\n            constant float2& depthBias [[buffer({MetalGenericMaterialShaderAbi.DepthBiasBufferIndex})]],\n            uint sampleId [[sample_id]]"
                : string.Empty,
            StringComparison.Ordinal).Replace(
            "IW4_GENERIC_DEPTH_BIAS_PRELUDE",
            _emulatesDepth24
                ? "(void)sampleId;\n            float rasterDepth = stage.position.z;\n            float polygonOffsetSlope = max(abs(dfdx(rasterDepth)), abs(dfdy(rasterDepth)));"
                : string.Empty,
            StringComparison.Ordinal).Replace(
            "IW4_GENERIC_FRAGMENT_RETURN(color, stage.position.z)",
            _emulatesDepth24
                ? """
                    constexpr float maximumDepth24 = 16777215.0f;
                    float biasedDepth = clamp(
                        stage.position.z + depthBias.x +
                            depthBias.y * polygonOffsetSlope,
                        0.0f,
                        1.0f);
                    float depth = floor(biasedDepth *
                        maximumDepth24 + 0.5f) / maximumDepth24;
                    return { color, depth };
                    """
                : "return color;",
            StringComparison.Ordinal);
        _library = _device.NewLibrary(source, options, ref error);
        if (_library.NativePtr == 0 || error.NativePtr != 0)
        {
            if (_library.NativePtr != 0)
            {
                _library.Dispose();
                _library = default;
            }
            _libraryFailure =
                $"genericMsl={Describe(error)}";
            blocker = _libraryFailure;
            return false;
        }
        blocker = string.Empty;
        return true;
    }

    private bool Fail(
        PipelineKey key,
        string reason,
        out MetalGenericMaterialPipeline? pipeline,
        out string blocker)
    {
        _failures.TryAdd(key, reason);
        pipeline = null;
        blocker = reason;
        return false;
    }

    private static MTLPrimitiveTopologyClass ToTopologyClass(
        RenderPrimitiveTopology topology) => topology switch
    {
        RenderPrimitiveTopology.TriangleList or
        RenderPrimitiveTopology.TriangleStrip =>
            MTLPrimitiveTopologyClass.Triangle,
        _ => throw new ArgumentOutOfRangeException(
            nameof(topology),
            topology,
            "Generic material rendering requires triangle geometry.")
    };

    private static string Describe(NSError error) =>
        error.NativePtr == 0
            ? "no NSError was returned"
            : error.LocalizedDescription.ToString() ?? "unknown Metal error";

    private static unsafe void SetIntFunctionConstant(
        MTLFunctionConstantValues constants,
        int value,
        ulong index) =>
        constants.SetConstantValue(
            (IntPtr)(&value),
            MTLDataType.Int,
            index);

    private readonly record struct PipelineKey(
        bool UsesStaticModelInstancing,
        int ColorLayerCount,
        int NormalTextureMask,
        int SpecularTextureMask,
        bool HasLightmap,
        bool UsesStaticModelLighting,
        RenderPrimitiveTopology Topology,
        RsxColorMask ColorMask,
        bool BlendEnabled,
        RsxBlendEquation BlendEquationRgb,
        RsxBlendEquation BlendEquationAlpha,
        RsxBlendFactor BlendSourceRgb,
        RsxBlendFactor BlendSourceAlpha,
        RsxBlendFactor BlendDestinationRgb,
        RsxBlendFactor BlendDestinationAlpha);
}

[SupportedOSPlatform("macos")]
internal sealed class MetalGenericMaterialPipeline : IDisposable
{
    private MTLRenderPipelineState _state;

    internal MetalGenericMaterialPipeline(
        MTLRenderPipelineState state,
        bool usesStaticModelInstancing)
    {
        if (state.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal pipeline state is required.",
                nameof(state));
        }
        _state = state;
        UsesStaticModelInstancing = usesStaticModelInstancing;
    }

    internal MTLRenderPipelineState State => _state.NativePtr != 0
        ? _state
        : throw new ObjectDisposedException(
            nameof(MetalGenericMaterialPipeline));

    internal bool UsesStaticModelInstancing { get; }

    public void Dispose()
    {
        if (_state.NativePtr == 0)
            return;
        _state.Dispose();
        _state = default;
    }
}

internal static class MetalGenericMaterialShaderAbi
{
    internal const ulong VertexBufferIndex = 0;
    internal const ulong VertexConstantBufferIndex = 1;
    internal const ulong StaticInstanceBufferIndex = 2;
    internal const ulong FragmentConstantBufferIndex = 0;
    internal const ulong DepthBiasBufferIndex = 1;

    internal const int ColorTextureStart = 0;
    internal const int LightmapTexture = 5;
    internal const int NormalTextureStart = 6;
    internal const int SpecularTextureStart = 10;
    internal const int StaticModelLightingTexture = 13;
    internal const int TextureBindingCount = 13;

    internal const int ConstantFloat4Count = 21;
    internal const int ConstantByteCount =
        ConstantFloat4Count * sizeof(float) * 4;
}
