using IW4.Assets.Assets.TechniqueSet;

namespace MapConverter.Game.IW3.PC.Techniques;

internal static class Iw3TechniqueBindingFacts
{
    private static readonly IReadOnlyDictionary<string, Iw3CodeConstantBinding>
        ConstantBindings = new Dictionary<string, Iw3CodeConstantBinding>(
            StringComparer.Ordinal)
        {
            ["lightPosition"] = Constant(MaterialConstantSource.LightPosition),
            ["lightDiffuse"] = Constant(MaterialConstantSource.LightDiffuse),
            ["lightSpecular"] = Constant(MaterialConstantSource.LightSpecular),
            // IW4's directional-light path writes the active sun into the same
            // position/diffuse/specular rows used by the lit-sun techniques.
            ["sunPosition"] = Constant(MaterialConstantSource.LightPosition),
            ["sunDiffuse"] = Constant(MaterialConstantSource.LightDiffuse),
            ["sunSpecular"] = Constant(MaterialConstantSource.LightSpecular),
            ["lightSpotDir"] = Constant(MaterialConstantSource.LightSpotDirection),
            ["lightSpotFactors"] = Constant(
                MaterialConstantSource.LightSpotFactors,
                flags: MaterialTechniqueFlags.UsesLightSpotFactors),
            ["nearPlaneOrg"] = Constant(MaterialConstantSource.NearPlaneOrigin),
            ["nearPlaneDx"] = Constant(MaterialConstantSource.NearPlaneDx),
            ["nearPlaneDy"] = Constant(MaterialConstantSource.NearPlaneDy),
            ["renderTargetSize"] = Constant(MaterialConstantSource.RenderTargetSize),
            ["lightFalloffPlacement"] = Constant(MaterialConstantSource.LightFalloffPlacement),
            ["dofEquationViewModelAndFarBlur"] = Constant(MaterialConstantSource.DofEquationViewModelAndFarBlur),
            ["dofEquationScene"] = Constant(MaterialConstantSource.DofEquationScene),
            ["dofLerpScale"] = Constant(MaterialConstantSource.DofLerpScale),
            ["dofLerpBias"] = Constant(MaterialConstantSource.DofLerpBias),
            ["dofRowDelta"] = Constant(MaterialConstantSource.DofRowDelta),
            ["particleCloudColor"] = Constant(MaterialConstantSource.ParticleCloudColor),
            ["gameTime"] = Constant(MaterialConstantSource.GameTime),
            ["pixelCostFracs"] = Constant(MaterialConstantSource.PixelCostFractions),
            ["pixelCostDecode"] = Constant(MaterialConstantSource.PixelCostDecode),
            ["filterTap"] = Constant(
                MaterialConstantSource.FilterTap0,
                arrayCount: 8),
            ["colorMatrixR"] = Constant(MaterialConstantSource.ColorMatrixR),
            ["colorMatrixG"] = Constant(MaterialConstantSource.ColorMatrixG),
            ["colorMatrixB"] = Constant(MaterialConstantSource.ColorMatrixB),
            ["shadowmapSwitchPartition"] = Constant(MaterialConstantSource.ShadowMapSwitchPartition),
            ["shadowmapScale"] = Constant(MaterialConstantSource.ShadowMapScale),
            ["zNear"] = Constant(MaterialConstantSource.ZNear),
            ["lightingLookupScale"] = Constant(MaterialConstantSource.LightingLookupScale),
            ["debugBumpmap"] = Constant(MaterialConstantSource.DebugBumpMap),
            ["materialColor"] = Constant(MaterialConstantSource.MaterialColor),
            ["fogConsts"] = Constant(MaterialConstantSource.Fog),
            // IW3 uploads the unpacked packed-color bytes without linearizing them.
            ["fogColor"] = Constant(MaterialConstantSource.FogColorGamma),
            ["glowSetup"] = Constant(MaterialConstantSource.GlowSetup),
            ["glowApply"] = Constant(MaterialConstantSource.GlowApply),
            ["colorBias"] = Constant(MaterialConstantSource.ColorBias),
            ["colorTintBase"] = Constant(MaterialConstantSource.ColorTintBase),
            ["colorTintDelta"] = Constant(MaterialConstantSource.ColorTintDelta),
            ["outdoorFeatherParms"] = Constant(MaterialConstantSource.OutdoorFeatherParameters),
            ["envMapParms"] = Constant(MaterialConstantSource.EnvMapParameters),
            ["spotShadowmapPixelAdjust"] = Constant(MaterialConstantSource.SpotShadowMapPixelAdjust),
            ["clipSpaceLookupScale"] = Constant(MaterialConstantSource.ClipSpaceLookupScale),
            ["clipSpaceLookupOffset"] = Constant(MaterialConstantSource.ClipSpaceLookupOffset),
            ["particleCloudMatrix"] = Constant(
                MaterialConstantSource.ParticleCloudMatrix0,
                MaterialUpdateFrequency.PerObject),
            ["depthFromClip"] = Constant(
                MaterialConstantSource.DepthFromClip,
                MaterialUpdateFrequency.PerObject),
            ["codeMeshArg"] = Constant(
                MaterialConstantSource.CodeMeshArgument0,
                MaterialUpdateFrequency.PerObject,
                arrayCount: 2),
            ["baseLightingCoords"] = Constant(
                MaterialConstantSource.BaseLightingCoords,
                MaterialUpdateFrequency.PerPrimitive),
            ["worldMatrix"] = Matrix(
                MaterialConstantSource.WorldMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["inverseWorldMatrix"] = Matrix(
                MaterialConstantSource.InverseWorldMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["transposeWorldMatrix"] = Matrix(
                MaterialConstantSource.TransposeWorldMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["inverseTransposeWorldMatrix"] = Matrix(
                MaterialConstantSource.InverseTransposeWorldMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["viewMatrix"] = Matrix(
                MaterialConstantSource.ViewMatrix,
                MaterialUpdateFrequency.PerObject),
            ["inverseViewMatrix"] = Matrix(
                MaterialConstantSource.InverseViewMatrix,
                MaterialUpdateFrequency.PerObject),
            ["transposeViewMatrix"] = Matrix(
                MaterialConstantSource.TransposeViewMatrix,
                MaterialUpdateFrequency.PerObject),
            ["inverseTransposeViewMatrix"] = Matrix(
                MaterialConstantSource.InverseTransposeViewMatrix,
                MaterialUpdateFrequency.PerObject),
            ["projectionMatrix"] = Matrix(
                MaterialConstantSource.ProjectionMatrix,
                MaterialUpdateFrequency.PerObject),
            ["inverseProjectionMatrix"] = Matrix(
                MaterialConstantSource.InverseProjectionMatrix,
                MaterialUpdateFrequency.PerObject),
            ["transposeProjectionMatrix"] = Matrix(
                MaterialConstantSource.TransposeProjectionMatrix,
                MaterialUpdateFrequency.PerObject),
            ["inverseTransposeProjectionMatrix"] = Matrix(
                MaterialConstantSource.InverseTransposeProjectionMatrix,
                MaterialUpdateFrequency.PerObject),
            ["worldViewMatrix"] = Matrix(
                MaterialConstantSource.WorldViewMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["inverseWorldViewMatrix"] = Matrix(
                MaterialConstantSource.InverseWorldViewMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["transposeWorldViewMatrix"] = Matrix(
                MaterialConstantSource.TransposeWorldViewMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["inverseTransposeWorldViewMatrix"] = Matrix(
                MaterialConstantSource.InverseTransposeWorldViewMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["viewProjectionMatrix"] = Matrix(
                MaterialConstantSource.ViewProjectionMatrix,
                MaterialUpdateFrequency.PerObject),
            ["inverseViewProjectionMatrix"] = Matrix(
                MaterialConstantSource.InverseViewProjectionMatrix,
                MaterialUpdateFrequency.PerObject),
            ["transposeViewProjectionMatrix"] = Matrix(
                MaterialConstantSource.TransposeViewProjectionMatrix,
                MaterialUpdateFrequency.PerObject),
            ["inverseTransposeViewProjectionMatrix"] = Matrix(
                MaterialConstantSource.InverseTransposeViewProjectionMatrix,
                MaterialUpdateFrequency.PerObject),
            ["worldViewProjectionMatrix"] = Matrix(
                MaterialConstantSource.WorldViewProjectionMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["inverseWorldViewProjectionMatrix"] = Matrix(
                MaterialConstantSource.InverseWorldViewProjectionMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["transposeWorldViewProjectionMatrix"] = Matrix(
                MaterialConstantSource.TransposeWorldViewProjectionMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["inverseTransposeWorldViewProjectionMatrix"] = Matrix(
                MaterialConstantSource.InverseTransposeWorldViewProjectionMatrix0,
                MaterialUpdateFrequency.PerPrimitive),
            ["shadowLookupMatrix"] = Matrix(
                MaterialConstantSource.ShadowLookupMatrix,
                MaterialUpdateFrequency.PerObject),
            ["inverseShadowLookupMatrix"] = Matrix(
                MaterialConstantSource.InverseShadowLookupMatrix,
                MaterialUpdateFrequency.PerObject),
            ["transposeShadowLookupMatrix"] = Matrix(
                MaterialConstantSource.TransposeShadowLookupMatrix,
                MaterialUpdateFrequency.PerObject),
            ["inverseTransposeShadowLookupMatrix"] = Matrix(
                MaterialConstantSource.InverseTransposeShadowLookupMatrix,
                MaterialUpdateFrequency.PerObject),
            ["worldOutdoorLookupMatrix"] = Matrix(
                MaterialConstantSource.WorldOutdoorLookupMatrix,
                MaterialUpdateFrequency.PerPrimitive),
            ["inverseWorldOutdoorLookupMatrix"] = Matrix(
                MaterialConstantSource.InverseWorldOutdoorLookupMatrix,
                MaterialUpdateFrequency.PerPrimitive),
            ["transposeWorldOutdoorLookupMatrix"] = Matrix(
                MaterialConstantSource.TransposeWorldOutdoorLookupMatrix,
                MaterialUpdateFrequency.PerPrimitive),
            ["inverseTransposeWorldOutdoorLookupMatrix"] = Matrix(
                MaterialConstantSource.InverseTransposeWorldOutdoorLookupMatrix,
                MaterialUpdateFrequency.PerPrimitive)
        };

    private static readonly IReadOnlyDictionary<string, Iw3CodeSamplerBinding>
        SamplerBindings = new Dictionary<string, Iw3CodeSamplerBinding>(
            StringComparer.Ordinal)
        {
            ["black"] = Sampler(MaterialTextureSource.Black),
            ["white"] = Sampler(MaterialTextureSource.White),
            ["identityNormalMap"] = Sampler(MaterialTextureSource.IdentityNormalMap),
            ["modelLightingSampler"] = Sampler(MaterialTextureSource.ModelLighting),
            ["lightmapSamplerPrimary"] = Sampler(
                MaterialTextureSource.LightmapPrimary,
                MaterialUpdateFrequency.Custom,
                customFlags: MaterialCustomSamplerFlags.PrimaryLightmap),
            ["lightmapSamplerSecondary"] = Sampler(
                MaterialTextureSource.LightmapSecondary,
                MaterialUpdateFrequency.Custom,
                customFlags: MaterialCustomSamplerFlags.SecondaryLightmap),
            ["shadowmapSamplerSun"] = Sampler(MaterialTextureSource.ShadowMapSun),
            ["shadowmapSamplerSpot"] = Sampler(MaterialTextureSource.ShadowMapSpot),
            ["feedbackSampler"] = Sampler(
                MaterialTextureSource.Feedback,
                MaterialUpdateFrequency.PerObject),
            ["resolvedPostSun"] = Sampler(
                MaterialTextureSource.ResolvedPostSun,
                flags: MaterialTechniqueFlags.NeedsResolvedPostSun),
            ["resolvedScene"] = Sampler(
                MaterialTextureSource.ResolvedScene,
                flags: MaterialTechniqueFlags.NeedsResolvedScene),
            ["postEffect0"] = Sampler(MaterialTextureSource.PostEffect0),
            ["postEffect1"] = Sampler(MaterialTextureSource.PostEffect1),
            ["attenuationSampler"] = Sampler(
                MaterialTextureSource.LightAttenuation,
                MaterialUpdateFrequency.PerObject),
            ["outdoor"] = Sampler(MaterialTextureSource.Outdoor),
            ["floatZSampler"] = Sampler(
                MaterialTextureSource.FloatZ,
                flags: MaterialTechniqueFlags.UsesFloatZ),
            ["processedFloatZSampler"] = Sampler(
                MaterialTextureSource.ProcessedFloatZ,
                flags: MaterialTechniqueFlags.UsesFloatZ),
            ["rawFloatZSampler"] = Sampler(
                MaterialTextureSource.RawFloatZ,
                flags: MaterialTechniqueFlags.UsesFloatZ),
            ["caseTexture"] = Sampler(
                MaterialTextureSource.CaseTexture,
                MaterialUpdateFrequency.PerObject),
            ["cinematicYSampler"] = Sampler(
                MaterialTextureSource.CinematicY,
                MaterialUpdateFrequency.PerObject),
            ["cinematicCrSampler"] = Sampler(
                MaterialTextureSource.CinematicCr,
                MaterialUpdateFrequency.PerObject),
            ["cinematicCbSampler"] = Sampler(
                MaterialTextureSource.CinematicCb,
                MaterialUpdateFrequency.PerObject),
            ["cinematicASampler"] = Sampler(
                MaterialTextureSource.CinematicA,
                MaterialUpdateFrequency.PerObject),
            ["reflectionProbeSampler"] = Sampler(
                MaterialTextureSource.ReflectionProbe,
                MaterialUpdateFrequency.Custom,
                customFlags: MaterialCustomSamplerFlags.ReflectionProbe)
        };

    internal static bool TryGetConstant(
        string accessor,
        out Iw3CodeConstantBinding binding) =>
        ConstantBindings.TryGetValue(accessor, out binding!);

    internal static bool TryGetSampler(
        string accessor,
        out Iw3CodeSamplerBinding binding) =>
        SamplerBindings.TryGetValue(accessor, out binding!);

    private static Iw3CodeConstantBinding Constant(
        MaterialConstantSource source,
        MaterialUpdateFrequency frequency = MaterialUpdateFrequency.Rarely,
        int arrayCount = 1,
        MaterialTechniqueFlags flags = MaterialTechniqueFlags.None) =>
        new(source, frequency, arrayCount, IsMatrix: false, flags);

    private static Iw3CodeConstantBinding Matrix(
        MaterialConstantSource source,
        MaterialUpdateFrequency frequency) =>
        new(source, frequency, ArrayCount: 1, IsMatrix: true, MaterialTechniqueFlags.None);

    private static Iw3CodeSamplerBinding Sampler(
        MaterialTextureSource source,
        MaterialUpdateFrequency frequency = MaterialUpdateFrequency.Rarely,
        MaterialTechniqueFlags flags = MaterialTechniqueFlags.None,
        MaterialCustomSamplerFlags customFlags = MaterialCustomSamplerFlags.None) =>
        new(source, frequency, flags, customFlags);
}

internal sealed record Iw3CodeConstantBinding(
    MaterialConstantSource Source,
    MaterialUpdateFrequency Frequency,
    int ArrayCount,
    bool IsMatrix,
    MaterialTechniqueFlags Flags);

internal sealed record Iw3CodeSamplerBinding(
    MaterialTextureSource Source,
    MaterialUpdateFrequency Frequency,
    MaterialTechniqueFlags Flags,
    MaterialCustomSamplerFlags CustomFlags);
