using System.Numerics;
using System.Runtime.Versioning;

using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.Metal.Pipelines;
using IW4.Render.Metal.Resources;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
using IW4.Render.Textures;
using IW4.Render.Transforms;

using SharpMetal.Metal;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed unsafe partial class MetalMapRenderer
{
    private const uint GenericMaterialFragmentProgramControl = 0x02008400u;
    private const string GenericMaterialVertexIdentity =
        "generic.material-fallback.vertex.render-position-uv0-wvp.v1";
    private const string GenericMaterialFragmentIdentity =
        "generic.material-fallback.fragment.sample-texture2d.v1";
    private const string GenericMaterialVertexDeclaration =
        "generic-material-fallback.vulkan.compact-rsx-inputs.v1";
    private bool _hasNormalCameraGenericMaterials;
    private MetalGenericMaterialFrameState _normalCameraGenericFrameState;

    private void PrepareGenericMaterialFrameState(
        in DerivedMatrixState matrices,
        float animationTimeSeconds)
    {
        if (!_hasNormalCameraGenericMaterials)
            return;
        Matrix4x4 worldViewProjection =
            RenderCoordinateConverter.RenderToGameMatrix *
            matrices.WorldViewProjection0;
        Vector3 cameraPosition = RenderCoordinateConverter
            .GameToRenderPosition(matrices.EyeOffset);
        _normalCameraGenericFrameState = new MetalGenericMaterialFrameState(
            worldViewProjection,
            new Vector4(cameraPosition, animationTimeSeconds),
            MapRenderGenericFogPlanner.Resolve(
                EditorPreviewFogRenderingEnabled,
                _normalCameraGenericActiveFog,
                _editorPreviewAtmosphere,
                shaderConsumesLinearFogColor: false),
            MapRenderGenericFogPlanner.Resolve(
                EditorPreviewFogRenderingEnabled,
                _normalCameraGenericActiveFog,
                _editorPreviewAtmosphere,
                shaderConsumesLinearFogColor: true));
    }

    private static bool HasGenericMaterialMarker(
        RenderWorldShaderProvenanceSnapshot shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return (shader.VertexProgramIr is { } vertex &&
                RsxGenericMaterialFallbackProgramFactory.IsVertex(vertex)) ||
               (shader.FragmentProgramIr is { } fragment &&
                RsxGenericMaterialFallbackProgramFactory.IsFragment(
                    fragment));
    }

    private static bool TryValidateGenericMaterialContract(
        RenderNormalCameraPreparedPassSnapshot pass,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(pass);
        RenderWorldShaderProvenanceSnapshot shader = pass.ShaderProvenance;
        if (shader.VertexProgramIr is not { } vertex ||
            !RsxGenericMaterialFallbackProgramFactory.IsVertex(vertex) ||
            shader.FragmentProgramIr is not { } fragment ||
            !RsxGenericMaterialFallbackProgramFactory.IsFragment(fragment))
        {
            blocker = "genericProgram=EXACT_MARKER_PAIR_REQUIRED";
            return false;
        }
        RsxGenericMaterialFallbackPrograms canonical =
            RsxGenericMaterialFallbackProgramFactory.Create();
        if (!string.Equals(
                vertex.Identity,
                canonical.VertexProgram.Identity,
                StringComparison.Ordinal) ||
            !string.Equals(
                fragment.Identity,
                canonical.FragmentProgram.Identity,
                StringComparison.Ordinal))
        {
            blocker = "genericProgram=CANONICAL_IR_MISMATCH";
            return false;
        }
        if (shader.Purpose != ShaderExecutionPurpose.CameraColor ||
            !shader.ProgramIrReady ||
            !shader.VertexInputPayloadReady ||
            !shader.RendererProgramReady ||
            !shader.ProgramExecutionReady)
        {
            blocker = "genericProgram=CAMERA_COLOR_EXECUTION_NOT_READY";
            return false;
        }
        if (!string.Equals(
                shader.VertexProgram.Name,
                GenericMaterialVertexIdentity,
                StringComparison.Ordinal) ||
            !string.Equals(
                shader.PixelProgram.Name,
                GenericMaterialFragmentIdentity,
                StringComparison.Ordinal) ||
            !string.Equals(
                shader.VertexDeclarationIdentity,
                GenericMaterialVertexDeclaration,
                StringComparison.Ordinal))
        {
            blocker = "genericProgram=IDENTITY_MISMATCH";
            return false;
        }
        if (!shader.VertexInputs.IsEmpty ||
            !shader.CustomSamplerDestinations.IsEmpty ||
            !shader.CodeSamplerDestinations.IsEmpty ||
            !shader.RuntimeSamplerRequirements.IsEmpty ||
            !shader.ProgramVertexConstantDestinations.IsEmpty ||
            !shader.ConstantDestinations.IsEmpty ||
            !shader.EmbeddedVertexConstants.IsEmpty ||
            !shader.CodePixelConstantPatchPlans.IsEmpty ||
            shader.MaterialSamplerDestinations.Length != 1 ||
            shader.MaterialSamplerDestinations[0].Destination != 0 ||
            !shader.MaterialSamplerDestinations[0].IsOperationallyResolved ||
            !string.Equals(
                shader.MaterialSamplerDestinations[0].TextureTarget,
                "Texture2D",
                StringComparison.Ordinal) ||
            shader.ProgramSamplerDestinations.Length != 1 ||
            shader.ProgramSamplerDestinations[0] != 0)
        {
            blocker = "genericProgram=SAMPLER_OR_CONSTANT_CONTRACT_MISMATCH";
            return false;
        }
        if (shader.FragmentProgramControl !=
                GenericMaterialFragmentProgramControl ||
            !string.Equals(
                shader.FragmentExportPrecision,
                "Fp16",
                StringComparison.Ordinal) ||
            shader.FragmentDepthExportEnabled ||
            shader.FragmentColorExports.Length != 1 ||
            shader.FragmentColorExports[0].ColorTarget != 0 ||
            !string.Equals(
                shader.FragmentColorExports[0].Register,
                "0",
                StringComparison.Ordinal) ||
            shader.FragmentColorExports[0].WrittenComponentMask !=
                RsxFragmentWriteMask.All ||
            !string.Equals(
                shader.FragmentColorExports[0].WrittenComponents,
                "xyzw",
                StringComparison.Ordinal))
        {
            blocker = "genericProgram=FRAGMENT_EXPORT_CONTRACT_MISMATCH";
            return false;
        }
        if (pass.Geometry.CoordinateSpace !=
                RenderGeometryCoordinateSpace.Render ||
            pass.Geometry.VertexStrideBytes !=
                MapRenderScene.TexturedVertexFloatCount * sizeof(float) ||
            pass.Geometry.Topology is not
                RenderPrimitiveTopology.TriangleList and not
                RenderPrimitiveTopology.TriangleStrip)
        {
            blocker = "genericGeometry=RENDER_TEXTURED_FLOAT22_REQUIRED";
            return false;
        }
        if (pass.ColorLayers.Length > MapRenderScene.MaxColorLayerCount ||
            (!pass.ColorLayers.IsEmpty &&
             pass.ColorLayers[0].LayerIndex != 0) ||
            pass.ColorLayers.Any(layer =>
                layer.LayerIndex < 0 ||
                layer.LayerIndex >= MapRenderScene.MaxColorLayerCount) ||
            pass.ColorLayers.Select(layer => layer.LayerIndex)
                .Distinct()
                .Count() != pass.ColorLayers.Length)
        {
            blocker = "genericColorLayers=BASE_AND_UNIQUE_RANGE_REQUIRED";
            return false;
        }
        if (AlphaTest.Resolve(pass.SourceState) is null)
        {
            blocker = "genericAlphaTest=UNSUPPORTED";
            return false;
        }
        blocker = string.Empty;
        return true;
    }

    private bool TryPrepareGenericNormalCameraPass(
        MapRenderScene scene,
        RenderNormalCameraPreparedPassSnapshot pass,
        out MetalPreparedNormalCameraPass? runtime,
        out string blocker)
    {
        runtime = null;
        if (!TryValidateGenericMaterialContract(pass, out blocker))
            return false;

        try
        {
            _ = _renderStates.GetOrCreate(pass.SourceState);
        }
        catch (InvalidOperationException exception)
        {
            blocker = $"renderState={exception.Message}";
            return false;
        }

        MetalGenericMaterialTextureBinding[] textures;
        int colorLayerCount;
        Vector4 blendWeightComponents;
        int normalTextureMask;
        int specularTextureMask;
        bool hasLightmap;
        try
        {
            if (!TryResolveGenericMaterialTextures(
                    pass,
                    out textures,
                    out colorLayerCount,
                    out blendWeightComponents,
                    out normalTextureMask,
                    out specularTextureMask,
                    out hasLightmap,
                    out blocker))
            {
                return false;
            }
        }
        catch (InvalidOperationException exception)
        {
            blocker = $"genericTextures={exception.Message}";
            return false;
        }

        if (_normalCameraGenericPipelines is null ||
            !_normalCameraGenericPipelines.TryGetOrCreate(
                pass,
                colorLayerCount,
                normalTextureMask,
                specularTextureMask,
                hasLightmap,
                pass.GenericMaterialFallback.UsesStaticModelLighting,
                out MetalGenericMaterialPipeline? pipeline,
                out blocker) ||
            pipeline is null)
        {
            return false;
        }
        if (pipeline.UsesStaticModelInstancing !=
            (pass.SourceKind == RenderNormalCameraDrawSourceKind.StaticModel))
        {
            blocker = "genericStaticComposition=SOURCE_KIND_MISMATCH";
            return false;
        }

        bool usesStaticModelLighting =
            pass.GenericMaterialFallback.UsesStaticModelLighting;
        MapRenderStaticInstanceLightingPayload lightingPayload =
            pass.GenericMaterialFallback.StaticInstanceLightingPayload;
        if (usesStaticModelLighting !=
            (lightingPayload ==
                MapRenderStaticInstanceLightingPayload.BaseLightingCoords))
        {
            blocker = "genericStaticLighting=PAYLOAD_CONTRACT_MISMATCH";
            return false;
        }

        MapRenderEditorPreviewLightingPlan lighting =
            scene.EditorPreviewLighting ??
            MapRenderEditorPreviewLightingPlanner.Create(comWorld: null);
        bool receivesLighting =
            usesStaticModelLighting ||
            pass.SourcePass.TechniqueSlot !=
                EditorPreviewTechniquePolicy
                    .PreferredEmissiveTechniqueSlot;
        bool genericStaticLightingMatchesDirectionalSun =
            usesStaticModelLighting &&
            lighting.DirectionalSunPrimaryLightIndex ==
                pass.SceneLightIndex;
        bool hasDirectionalSunDiffuse =
            receivesLighting &&
            lighting.HasDirectionalSun &&
            (!usesStaticModelLighting ||
             (genericStaticLightingMatchesDirectionalSun &&
              pass.GenericMaterialFallback
                  .StaticModelLightingAddsDirectionalDiffuse));
        bool hasDirectionalSunSpecular =
            receivesLighting &&
            lighting.HasDirectionalSun &&
            (!usesStaticModelLighting ||
             (genericStaticLightingMatchesDirectionalSun &&
              pass.GenericMaterialFallback
                  .StaticModelLightingAddsDirectionalSpecular));
        Vector3 sunDiffuse = Vector3.Zero;
        Vector3 sunSpecular = Vector3.Zero;
        if (hasDirectionalSunDiffuse || hasDirectionalSunSpecular)
        {
            DirectionalSunLinearColors colors =
                MapRenderEditorDirectCodeConstantProducers
                    .ProduceDirectionalSunLinearColors(
                        lighting,
                        MapRenderEditorPreviewPrimaryLightInvocationPolicy
                            .Resolve(
                                scene.EditorPreviewVision?.Vision?.PrimaryLight,
                                useHeroLighting: false));
            sunDiffuse = colors.Diffuse;
            sunSpecular = colors.Specular;
        }

        bool shaderPackerSrgb =
            pass.SourceState.ShaderPackerSrgbEnabled &&
            ((RsxFragmentProgramControlFlags)
                pass.ShaderProvenance.FragmentProgramControl &
             RsxFragmentProgramControlFlags.Exports32Bit) == 0;
        bool premultiplyAlpha =
            pass.SourceState.BlendEnabled &&
            pass.SourceState.BlendEquationRgb == RsxBlendEquation.Add &&
            pass.SourceState.BlendSourceRgb == RsxBlendFactor.One &&
            pass.SourceState.BlendDestinationRgb ==
                RsxBlendFactor.OneMinusSourceAlpha;
        if (AlphaTest.Resolve(pass.SourceState) is not { } alphaTestMode)
        {
            blocker = "genericAlphaTest=UNSUPPORTED";
            return false;
        }
        var generic = new MetalGenericMaterialDraw(
            pipeline,
            textures,
            colorLayerCount,
            blendWeightComponents,
            normalTextureMask,
            specularTextureMask,
            hasLightmap,
            receivesLighting,
            pass.GenericMaterialFallback.ColorInputLinearizationMask,
            usesStaticModelLighting,
            lighting.AmbientColor,
            hasDirectionalSunDiffuse,
            hasDirectionalSunSpecular,
            lighting.DirectionalSunDirection,
            sunDiffuse,
            sunSpecular,
            alphaTestMode,
            shaderPackerSrgb,
            premultiplyAlpha);
        MetalGeometryResource geometry =
            _resources.RequireGeometry(pass.Geometry.Identity);
        MetalInstanceResource? instances = null;
        if (pipeline.UsesStaticModelInstancing)
        {
            if (pass.Instances is null)
            {
                blocker = "genericStaticInstances=RESOURCE_MISSING";
                return false;
            }
            instances = _resources.RequireInstances(
                pass.Instances.Identity);
            if (instances.StrideBytes !=
                MapRenderStaticInstanceBufferPacker
                    .PlacementOnlyFloatStride * sizeof(float))
            {
                blocker = "genericStaticInstances=PLACEMENT_LAYOUT_MISMATCH";
                return false;
            }
        }
        runtime = new MetalPreparedNormalCameraPass(
            pass,
            generic,
            geometry,
            instances,
            lightingPayload,
            needsOwnedInstanceData: false);
        blocker = string.Empty;
        return true;
    }

    private bool TryResolveGenericMaterialTextures(
        RenderNormalCameraPreparedPassSnapshot pass,
        out MetalGenericMaterialTextureBinding[] textures,
        out int colorLayerCount,
        out Vector4 blendWeightComponents,
        out int normalTextureMask,
        out int specularTextureMask,
        out bool hasLightmap,
        out string blocker)
    {
        textures = new MetalGenericMaterialTextureBinding[
            MetalGenericMaterialShaderAbi.TextureBindingCount];
        MetalGenericMaterialTextureBinding baseBinding =
            ResolveGenericMaterialTexture(
                destination: 0,
                pass.BaseTextureIdentity,
                pass.BaseSamplerIdentity);
        for (int index = 0; index < textures.Length; index++)
            textures[index] = baseBinding with { Destination = (ulong)index };

        if (pass.ColorLayers.IsEmpty)
        {
            colorLayerCount = 1;
        }
        else
        {
            colorLayerCount = pass.ColorLayers.Length;
            for (int index = 0; index < colorLayerCount; index++)
            {
                RenderNormalCameraColorLayerSnapshot layer =
                    pass.ColorLayers[index];
                textures[index] = ResolveGenericMaterialTexture(
                    index,
                    layer.TextureIdentity,
                    layer.SamplerIdentity);
            }
        }

        blendWeightComponents = new Vector4(-1f);
        for (int layerIndex = 1;
             layerIndex < pass.ColorLayers.Length;
             layerIndex++)
        {
            SetComponent(
                ref blendWeightComponents,
                layerIndex - 1,
                pass.ColorLayers[layerIndex].BlendWeightComponent);
        }

        hasLightmap =
            pass.LightmapTextureIdentity is not null &&
            pass.LightmapSamplerIdentity is not null;
        if (pass.LightmapTextureIdentity is { } lightmapTexture &&
            pass.LightmapSamplerIdentity is { } lightmapSampler)
        {
            textures[MetalGenericMaterialShaderAbi.LightmapTexture] =
                ResolveGenericMaterialTexture(
                    MetalGenericMaterialShaderAbi.LightmapTexture,
                    lightmapTexture,
                    lightmapSampler);
        }

        normalTextureMask = ResolveGenericEditorRoleTextures(
            pass,
            [
                EditorMaterialTextureRole.BaseNormal,
                EditorMaterialTextureRole.NormalLayer1,
                EditorMaterialTextureRole.NormalLayer2,
                EditorMaterialTextureRole.NormalLayer3
            ],
            MetalGenericMaterialShaderAbi.NormalTextureStart,
            textures);
        specularTextureMask = ResolveGenericEditorRoleTextures(
            pass,
            [
                EditorMaterialTextureRole.BaseSpecular,
                EditorMaterialTextureRole.SpecularLayer1,
                EditorMaterialTextureRole.SpecularLayer2
            ],
            MetalGenericMaterialShaderAbi.SpecularTextureStart,
            textures);
        blocker = string.Empty;
        return true;
    }

    private int ResolveGenericEditorRoleTextures(
        RenderNormalCameraPreparedPassSnapshot pass,
        IReadOnlyList<EditorMaterialTextureRole> roles,
        int textureStart,
        MetalGenericMaterialTextureBinding[] textures)
    {
        int mask = 0;
        for (int roleIndex = 0; roleIndex < roles.Count; roleIndex++)
        {
            RenderNormalCameraMaterialSamplerSnapshot[] candidates = pass
                .MaterialSamplers
                .Where(binding =>
                    binding.EditorTextureRole == roles[roleIndex])
                .Take(2)
                .ToArray();
            if (candidates.Length != 1 ||
                candidates[0].TextureIdentity is not { } texture ||
                candidates[0].SamplerIdentity is not { } sampler)
            {
                continue;
            }
            int destination = textureStart + roleIndex;
            textures[destination] = ResolveGenericMaterialTexture(
                destination,
                texture,
                sampler);
            mask |= 1 << roleIndex;
        }
        return mask;
    }

    private MetalGenericMaterialTextureBinding ResolveGenericMaterialTexture(
        int destination,
        RenderSemanticIdentity textureIdentity,
        RenderSemanticIdentity samplerIdentity)
    {
        MetalTextureResource texture =
            _resources.RequireTexture(textureIdentity);
        if (texture.Descriptor.Dimension !=
            RenderTextureDimension.Texture2D)
        {
            throw new InvalidOperationException(
                $"Generic Metal texture {texture.Descriptor.Name} is not 2D.");
        }
        MetalSamplerResource sampler =
            _resources.RequireSampler(samplerIdentity);
        return new MetalGenericMaterialTextureBinding(
            checked((ulong)destination),
            texture.ResolveSampledTexture(sampler.UsesSrgbReads),
            sampler.State);
    }

    private static void NormalizeInactiveGenericMaterialBindings(
        IReadOnlyList<MetalPreparedNormalCameraPass> preparedPasses)
    {
        MetalGenericMaterialTextureBinding? canonicalInactive = null;
        for (int passIndex = 0;
             passIndex < preparedPasses.Count;
             passIndex++)
        {
            MetalGenericMaterialDraw? generic =
                preparedPasses[passIndex].GenericMaterial;
            if (generic is null)
                continue;
            if (generic.Textures.Length !=
                    MetalGenericMaterialShaderAbi.TextureBindingCount ||
                generic.Textures[0].Destination != 0 ||
                generic.Textures[0].Texture.NativePtr == 0 ||
                generic.Textures[0].Sampler.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "An authorized generic Metal pass has no valid mandatory slot-zero binding.");
            }
            canonicalInactive = generic.Textures[0];
            break;
        }
        if (canonicalInactive is not { } canonical)
            return;

        for (int passIndex = 0;
             passIndex < preparedPasses.Count;
             passIndex++)
        {
            MetalGenericMaterialDraw? generic =
                preparedPasses[passIndex].GenericMaterial;
            if (generic is null)
                continue;
            for (int destination = 0;
                 destination < generic.Textures.Length;
                 destination++)
            {
                if (IsGenericMaterialTextureActive(generic, destination))
                    continue;
                // Every declared MSL argument remains valid, while inactive
                // branches converge on one scene-lifetime pair. Metal never
                // observes nil and successive generic passes can retain the
                // same native slot binding.
                generic.Textures[destination] = canonical with
                {
                    Destination = checked((ulong)destination)
                };
            }
        }
    }

    private static bool IsGenericMaterialTextureActive(
        MetalGenericMaterialDraw generic,
        int destination)
    {
        if (destination >= MetalGenericMaterialShaderAbi.ColorTextureStart &&
            destination < generic.ColorLayerCount)
        {
            return true;
        }
        if (destination == MetalGenericMaterialShaderAbi.LightmapTexture)
            return generic.HasLightmap;
        int normalIndex = destination -
            MetalGenericMaterialShaderAbi.NormalTextureStart;
        if ((uint)normalIndex < 4)
            return (generic.NormalTextureMask & (1 << normalIndex)) != 0;
        int specularIndex = destination -
            MetalGenericMaterialShaderAbi.SpecularTextureStart;
        return (uint)specularIndex < 3 &&
            (generic.SpecularTextureMask & (1 << specularIndex)) != 0;
    }

    private static void InitializeStaticGenericMaterialConstants(
        MTLBuffer frameBuffer,
        MetalPreparedNormalCameraPass pass)
    {
        MetalGenericMaterialDraw generic = pass.GenericMaterial ??
            throw new InvalidOperationException(
                "A non-generic pass reached generic static constant publication.");
        Span<Vector4> rows = BufferVectors(
            frameBuffer,
            pass.CodePixelConstantsOffset,
            MetalGenericMaterialShaderAbi.ConstantFloat4Count);
        MapRenderEditorVegetationAnimationPlan? vegetation =
            pass.Source.VegetationAnimation;
        rows[5] = new Vector4(
            vegetation?.IsEnabled == true ? 1f : 0f,
            vegetation?.Amplitude ?? 0f,
            vegetation?.AngularFrequency ?? 0f,
            vegetation?.SpatialFrequency ?? 0f);
        rows[6] = new Vector4(
            pass.Source.LocalBounds.Min.Z,
            pass.Source.LocalBounds.Max.Z - pass.Source.LocalBounds.Min.Z,
            0f,
            0f);
        // The neutral generic-material contract retains the selected source
        // pass's color-input transfer and static-lighting requirements.
        rows[7] = new Vector4(
            generic.ColorLayerCount,
            generic.ColorInputLinearizationMask,
            generic.HasLightmap ? 1f : 0f,
            generic.LightingEnabled ? 1f : 0f);
        rows[8] = generic.BlendWeightComponents;
        int outputFlags =
            (generic.ShaderPackerSrgb ? 1 : 0) |
            (generic.PremultiplyAlpha ? 2 : 0);
        rows[9] = new Vector4(
            generic.NormalTextureMask,
            generic.SpecularTextureMask,
            (int)generic.AlphaTestMode,
            outputFlags);
        rows[10] = new Vector4(
            generic.AmbientColor,
            0f);
        rows[11] = new Vector4(
            generic.DirectionalSunDirection,
            generic.HasDirectionalSunDiffuse ? 1f : 0f);
        rows[12] = new Vector4(
            generic.DirectionalSunDiffuse,
            generic.HasDirectionalSunSpecular ? 1f : 0f);
        rows[13] = new Vector4(generic.DirectionalSunSpecular, 0f);
        rows[20] = MapRenderStaticModelLightingAtlas.SamplerTransform;
    }

    private void WriteGenericMaterialConstants(
        MTLBuffer frameBuffer,
        MetalPreparedNormalCameraPass pass)
    {
        MetalGenericMaterialDraw generic = pass.GenericMaterial ??
            throw new InvalidOperationException(
                "A non-generic pass reached generic constant publication.");
        Span<Vector4> rows = BufferVectors(
            frameBuffer,
            pass.CodePixelConstantsOffset,
            MetalGenericMaterialShaderAbi.ConstantFloat4Count);
        Matrix4x4 worldViewProjection =
            _normalCameraGenericFrameState.WorldViewProjection;
        rows[0] = new Vector4(
            worldViewProjection.M11,
            worldViewProjection.M12,
            worldViewProjection.M13,
            worldViewProjection.M14);
        rows[1] = new Vector4(
            worldViewProjection.M21,
            worldViewProjection.M22,
            worldViewProjection.M23,
            worldViewProjection.M24);
        rows[2] = new Vector4(
            worldViewProjection.M31,
            worldViewProjection.M32,
            worldViewProjection.M33,
            worldViewProjection.M34);
        rows[3] = new Vector4(
            worldViewProjection.M41,
            worldViewProjection.M42,
            worldViewProjection.M43,
            worldViewProjection.M44);
        rows[4] = _normalCameraGenericFrameState.CameraAndTime;

        MapRenderGenericFogPlan fog = generic.ShaderPackerSrgb
            ? _normalCameraGenericFrameState.LinearFog
            : _normalCameraGenericFrameState.GammaFog;
        rows[14] = new Vector4(
            fog.IsEnabled ? 1f : 0f,
            fog.UsesActiveFog ? 1f : 0f,
            fog.SunFogEnabled ? 1f : 0f,
            0f);
        rows[15] = new Vector4(fog.FogColor, fog.AtmosphereMaxOpacity);
        rows[16] = new Vector4(
            fog.AtmosphereStartDistance,
            fog.AtmosphereEndDistance,
            fog.FogDistanceScale,
            fog.FogDistanceBias);
        rows[17] = new Vector4(
            fog.FogMinimumVisibility,
            fog.SunFogDistanceScale,
            fog.SunFogEndCosine,
            fog.SunFogAngularScale);
        rows[18] = new Vector4(fog.SunFogColor, 0f);
        rows[19] = new Vector4(fog.SunFogDirection, 0f);
    }

    private void BindGenericMaterialResources(
        ref MetalNormalCameraEncoderBindingShadow bindings,
        MTLBuffer frameBuffer,
        MetalPreparedNormalCameraPass pass)
    {
        MetalGenericMaterialDraw generic = pass.GenericMaterial ??
            throw new InvalidOperationException(
                "A non-generic pass reached generic resource binding.");
        bindings.SetVertexBuffer(
            pass.Geometry.Buffer,
            pass.Geometry.VertexOffset,
            MetalGenericMaterialShaderAbi.VertexBufferIndex);
        bool vertexConstantsChanged = bindings.SetVertexBuffer(
            frameBuffer,
            checked((ulong)pass.CodePixelConstantsOffset),
            MetalGenericMaterialShaderAbi.VertexConstantBufferIndex);
        bool fragmentConstantsChanged = bindings.SetFragmentBuffer(
            frameBuffer,
            checked((ulong)pass.CodePixelConstantsOffset),
            MetalGenericMaterialShaderAbi.FragmentConstantBufferIndex);
        bool depthBiasChanged = _depthStencilFormat.EmulatesDepth24 &&
            bindings.SetFragmentBytes(
                _renderStates.CurrentDepthBias,
                MetalGenericMaterialShaderAbi.DepthBiasBufferIndex);
        if (vertexConstantsChanged || fragmentConstantsChanged ||
            depthBiasChanged)
        {
            _telemetry.AddCounter(MapRenderFrameCounter.UniformUpdates);
        }

        if (generic.Pipeline.UsesStaticModelInstancing)
        {
            MTLBuffer instanceBuffer;
            ulong instanceOffset;
            if (pass.LightingPayload ==
                MapRenderStaticInstanceLightingPayload.BaseLightingCoords)
            {
                RequireStaticModelLightingInstanceBinding(
                    pass,
                    out instanceBuffer,
                    out instanceOffset);
            }
            else
            {
                instanceBuffer = pass.Instances!.Buffer;
                instanceOffset = pass.Instances.Offset;
            }
            bindings.SetVertexBuffer(
                instanceBuffer,
                instanceOffset,
                MetalGenericMaterialShaderAbi.StaticInstanceBufferIndex);
        }
        for (int bindingIndex = 0;
             bindingIndex < generic.Textures.Length;
             bindingIndex++)
        {
            MetalGenericMaterialTextureBinding binding =
                generic.Textures[bindingIndex];
            bindings.SetFragmentTexture(
                binding.Texture,
                binding.Destination);
            bindings.SetFragmentSampler(
                binding.Sampler,
                binding.Destination);
        }
        if (generic.UsesStaticModelLighting)
        {
            RequireStaticModelLightingSamplerBinding(
                out MTLTexture modelLightingTexture,
                out MTLSamplerState modelLightingSampler);
            bindings.SetFragmentTexture(
                modelLightingTexture,
                MetalGenericMaterialShaderAbi.StaticModelLightingTexture);
            bindings.SetFragmentSampler(
                modelLightingSampler,
                MetalGenericMaterialShaderAbi.StaticModelLightingTexture);
        }
    }

    private static void SetComponent(
        ref Vector4 value,
        int component,
        float componentValue)
    {
        switch (component)
        {
            case 0:
                value.X = componentValue;
                break;
            case 1:
                value.Y = componentValue;
                break;
            case 2:
                value.Z = componentValue;
                break;
            case 3:
                value.W = componentValue;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(component));
        }
    }

    private sealed class MetalGenericMaterialDraw
    {
        internal MetalGenericMaterialDraw(
            MetalGenericMaterialPipeline pipeline,
            MetalGenericMaterialTextureBinding[] textures,
            int colorLayerCount,
            Vector4 blendWeightComponents,
            int normalTextureMask,
            int specularTextureMask,
            bool hasLightmap,
            bool lightingEnabled,
            int colorInputLinearizationMask,
            bool usesStaticModelLighting,
            Vector3 ambientColor,
            bool hasDirectionalSunDiffuse,
            bool hasDirectionalSunSpecular,
            Vector3 directionalSunDirection,
            Vector3 directionalSunDiffuse,
            Vector3 directionalSunSpecular,
            AlphaTestMode alphaTestMode,
            bool shaderPackerSrgb,
            bool premultiplyAlpha)
        {
            Pipeline = pipeline;
            Textures = textures;
            ColorLayerCount = colorLayerCount;
            BlendWeightComponents = blendWeightComponents;
            NormalTextureMask = normalTextureMask;
            SpecularTextureMask = specularTextureMask;
            HasLightmap = hasLightmap;
            LightingEnabled = lightingEnabled;
            ColorInputLinearizationMask = colorInputLinearizationMask;
            UsesStaticModelLighting = usesStaticModelLighting;
            AmbientColor = ambientColor;
            HasDirectionalSunDiffuse = hasDirectionalSunDiffuse;
            HasDirectionalSunSpecular = hasDirectionalSunSpecular;
            DirectionalSunDirection = directionalSunDirection;
            DirectionalSunDiffuse = directionalSunDiffuse;
            DirectionalSunSpecular = directionalSunSpecular;
            AlphaTestMode = alphaTestMode;
            ShaderPackerSrgb = shaderPackerSrgb;
            PremultiplyAlpha = premultiplyAlpha;
        }

        internal MetalGenericMaterialPipeline Pipeline { get; }
        internal MetalGenericMaterialTextureBinding[] Textures { get; }
        internal int ColorLayerCount { get; }
        internal Vector4 BlendWeightComponents { get; }
        internal int NormalTextureMask { get; }
        internal int SpecularTextureMask { get; }
        internal bool HasLightmap { get; }
        internal bool LightingEnabled { get; }
        internal int ColorInputLinearizationMask { get; }
        internal bool UsesStaticModelLighting { get; }
        internal Vector3 AmbientColor { get; }
        internal bool HasDirectionalSunDiffuse { get; }
        internal bool HasDirectionalSunSpecular { get; }
        internal Vector3 DirectionalSunDirection { get; }
        internal Vector3 DirectionalSunDiffuse { get; }
        internal Vector3 DirectionalSunSpecular { get; }
        internal AlphaTestMode AlphaTestMode { get; }
        internal bool ShaderPackerSrgb { get; }
        internal bool PremultiplyAlpha { get; }
    }

    private readonly record struct MetalGenericMaterialTextureBinding(
        ulong Destination,
        MTLTexture Texture,
        MTLSamplerState Sampler);

    private readonly record struct MetalGenericMaterialFrameState(
        Matrix4x4 WorldViewProjection,
        Vector4 CameraAndTime,
        MapRenderGenericFogPlan GammaFog,
        MapRenderGenericFogPlan LinearFog);
}
