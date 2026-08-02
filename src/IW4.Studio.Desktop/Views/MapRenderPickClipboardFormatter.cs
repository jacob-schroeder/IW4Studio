using System.Globalization;
using IW4.Render;
using IW4.Render.Materials;
using IW4.Render.Picking;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Formats a picked map surface as stable, line-oriented diagnostics suitable
/// for copying into an issue, handoff, or follow-up analysis task.
/// </summary>
internal static class MapRenderPickClipboardFormatter
{
    public static string Format(
        MapRenderScene scene,
        MapRenderCamera camera,
        MapRenderPickHit? selection,
        IReadOnlyList<MapRenderPickCandidate> overlapCandidates,
        IReadOnlyList<MapRenderPickCandidate> neighborCandidates,
        bool pickMissed)
    {
        var lines = new List<string>
        {
            "FastFile render pick",
            $"camera.position.x={F(camera.Position.X)}",
            $"camera.position.y={F(camera.Position.Y)}",
            $"camera.position.z={F(camera.Position.Z)}",
            $"camera.yawDegrees={F(ToDegrees(camera.YawRadians))}",
            $"camera.pitchDegrees={F(ToDegrees(camera.PitchRadians))}"
        };

        if (selection is not { } hit)
        {
            lines.Add($"selection={(pickMissed ? "miss" : "none")}");
            AppendOverlapCandidates(lines, overlapCandidates);
            AppendPickCandidates(lines, "neighbor", neighborCandidates);
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"selection.kind={hit.Kind}");
        lines.Add($"selection.objectIndex={hit.ObjectIndex}");
        lines.Add($"selection.surfaceIndex={hit.SurfaceIndex}");
        lines.Add($"selection.triangleIndex={hit.TriangleIndex}");
        lines.Add($"selection.name={hit.Name}");
        if (!string.IsNullOrWhiteSpace(hit.AuthoredMaterialName))
            lines.Add($"selection.authoredMaterialName={hit.AuthoredMaterialName}");
        lines.Add($"selection.distance={F(hit.Distance)}");
        lines.Add($"selection.position.x={F(hit.Position.X)}");
        lines.Add($"selection.position.y={F(hit.Position.Y)}");
        lines.Add($"selection.position.z={F(hit.Position.Z)}");
        if (hit.TexCoords is { } texCoords)
        {
            lines.Add($"selection.triangle.uv0.u={FormatFloat3(texCoords.Uv0.X)}");
            lines.Add($"selection.triangle.uv0.v={FormatFloat3(texCoords.Uv0.Y)}");
            lines.Add($"selection.triangle.uv1.u={FormatFloat3(texCoords.Uv1.X)}");
            lines.Add($"selection.triangle.uv1.v={FormatFloat3(texCoords.Uv1.Y)}");
            lines.Add($"selection.triangle.uv2.u={FormatFloat3(texCoords.Uv2.X)}");
            lines.Add($"selection.triangle.uv2.v={FormatFloat3(texCoords.Uv2.Y)}");
            lines.Add($"selection.triangle.lightmapUv0.u={FormatFloat3(texCoords.LightmapUv0.X)}");
            lines.Add($"selection.triangle.lightmapUv0.v={FormatFloat3(texCoords.LightmapUv0.Y)}");
            lines.Add($"selection.triangle.lightmapUv1.u={FormatFloat3(texCoords.LightmapUv1.X)}");
            lines.Add($"selection.triangle.lightmapUv1.v={FormatFloat3(texCoords.LightmapUv1.Y)}");
            lines.Add($"selection.triangle.lightmapUv2.u={FormatFloat3(texCoords.LightmapUv2.X)}");
            lines.Add($"selection.triangle.lightmapUv2.v={FormatFloat3(texCoords.LightmapUv2.Y)}");
        }
        AppendOverlapCandidates(lines, overlapCandidates);
        AppendPickCandidates(lines, "neighbor", neighborCandidates);

        if (hit.Material is not { } material)
        {
            if (!string.IsNullOrWhiteSpace(hit.AuthoredMaterialName))
            {
                lines.Add($"material.referenceName={hit.AuthoredMaterialName}");
                if (hit.AuthoredMaterialName[0] == ',')
                {
                    lines.Add($"material.canonicalName={hit.AuthoredMaterialName[1..]}");
                    lines.Add("material.status=unresolved-reference");
                }
            }
            lines.Add("material=none");
            return string.Join(Environment.NewLine, lines);
        }

        MapRenderState state = material.State;
        lines.Add($"material.name={material.MaterialName}");
        lines.Add($"material.techniqueSet={material.TechniqueSetName}");
        lines.Add($"material.techniqueSlot={material.TechniqueSlot}");
        lines.Add($"material.technique={material.TechniqueName}");
        lines.Add($"material.passClass={material.PassClass}");
        lines.Add($"material.passIndex={material.PassIndex}");
        lines.Add($"material.samplerArgIndex={material.SamplerArgIndex}");
        lines.Add($"material.samplerDest={material.SamplerDest}");
        lines.Add($"material.samplerHash=0x{material.SamplerHash:X8}");
        lines.Add($"material.textureSemantic=0x{material.TextureSemantic:X2}");
        lines.Add($"material.texCoordSource=0x{material.TexCoordSource:X2}");
        lines.Add($"material.texCoordSourceName={MapRenderUvRoute.StreamSourceName(material.TexCoordSource)}");
        lines.Add($"material.unresolvedCodeSamplerCount={material.UnresolvedCodeSamplerCount}");
        lines.Add($"material.colorLayerCount={material.ColorLayers.Count}");
        foreach (MapRenderPickColorLayerInfo layer in material.ColorLayers)
        {
            string prefix = $"material.colorLayer.{layer.LayerIndex}";
            lines.Add($"{prefix}.textureName={layer.TextureName}");
            lines.Add($"{prefix}.samplerArgIndex={layer.SamplerArgIndex}");
            lines.Add($"{prefix}.samplerDest={layer.SamplerDest}");
            lines.Add($"{prefix}.samplerHash=0x{layer.SamplerHash:X8}");
            lines.Add($"{prefix}.textureSemantic=0x{layer.TextureSemantic:X2}");
            lines.Add($"{prefix}.blendWeightComponent={layer.BlendWeightComponent}");
            lines.Add($"{prefix}.texCoordSource=0x{layer.UvRoute.TexCoordSource:X2}");
            lines.Add($"{prefix}.uvFormula={layer.UvRoute.Formula}");
        }
        lines.Add($"material.materialSamplerCount={material.MaterialSamplers.Count}");
        lines.Add($"material.shaderExecutionStatus={material.ShaderExecutionStatus}");
        lines.Add($"material.shader.vertex.name={material.ShaderExecution.VertexProgram.Name}");
        lines.Add($"material.shader.vertex.dataSize={material.ShaderExecution.VertexProgram.LoadedDataSize}");
        lines.Add($"material.shader.vertex.sha256={material.ShaderExecution.VertexProgram.DataSha256}");
        lines.Add($"material.shader.pixel.name={material.ShaderExecution.PixelProgram.Name}");
        lines.Add($"material.shader.pixel.dataSize={material.ShaderExecution.PixelProgram.LoadedDataSize}");
        lines.Add($"material.shader.pixel.sha256={material.ShaderExecution.PixelProgram.DataSha256}");
        lines.Add($"material.shader.programSamplerDestinations={string.Join(';', material.ShaderExecution.ProgramSamplerDestinations)}");
        lines.Add($"material.shader.customSamplerCount={material.ShaderExecution.CustomSamplerDestinations.Count}");
        for (int samplerIndex = 0; samplerIndex < material.ShaderExecution.CustomSamplerDestinations.Count; samplerIndex++)
        {
            MapRenderShaderSamplerDestination customSampler = material.ShaderExecution.CustomSamplerDestinations[samplerIndex];
            lines.Add($"material.shader.customSampler.{samplerIndex}.dest={customSampler.Destination}");
            lines.Add($"material.shader.customSampler.{samplerIndex}.resource={customSampler.ResourceIdentity}");
            lines.Add($"material.shader.customSampler.{samplerIndex}.target={customSampler.TextureTarget}");
        }
        lines.Add($"material.shader.vertexDeclaration={material.ShaderExecution.VertexDeclarationIdentity}");
        lines.Add($"material.shader.vertexInputCount={material.ShaderExecution.VertexInputs.Count}");
        for (int inputIndex = 0; inputIndex < material.ShaderExecution.VertexInputs.Count; inputIndex++)
        {
            MapRenderShaderVertexInputBinding input = material.ShaderExecution.VertexInputs[inputIndex];
            string prefix = $"material.shader.vertexInput.{inputIndex}";
            lines.Add($"{prefix}.source=0x{input.Source:X2}");
            lines.Add($"{prefix}.dest=0x{input.Destination:X2}");
            lines.Add($"{prefix}.stream={input.StreamIndex}");
            lines.Add($"{prefix}.stride=0x{input.Stride:X}");
            lines.Add($"{prefix}.offset=0x{input.Offset:X}");
            lines.Add($"{prefix}.components={input.ComponentCount}");
            lines.Add($"{prefix}.rsxType=0x{input.RsxType:X2}/{input.RsxTypeName}");
        }
        lines.Add($"material.shader.programCacheKey={material.ShaderExecution.ProgramCacheKey}");
        lines.Add($"material.shader.programIrReady={material.ShaderExecution.ProgramIrReady}");
        lines.Add($"material.shader.vertexInputPayloadReady={material.ShaderExecution.VertexInputPayloadReady}");
        lines.Add($"material.shader.rendererReady={material.ShaderExecution.ProgramExecutionReady}");
        lines.Add($"material.shader.rendererStatus={material.ShaderExecution.ProgramExecutionStatus}");
        lines.Add($"material.shader.rendererBlockers={string.Join(';', material.ShaderExecution.RendererBlockers)}");
        lines.Add($"material.shader.codeSamplerCount={material.ShaderExecution.CodeSamplerDestinations.Count}");
        for (int samplerIndex = 0; samplerIndex < material.ShaderExecution.CodeSamplerDestinations.Count; samplerIndex++)
        {
            MapRenderShaderSamplerDestination codeSampler = material.ShaderExecution.CodeSamplerDestinations[samplerIndex];
            string prefix = $"material.shader.codeSampler.{samplerIndex}";
            lines.Add($"{prefix}.dest={codeSampler.Destination}");
            lines.Add($"{prefix}.raw=0x{codeSampler.Argument:X8}");
            lines.Add($"{prefix}.resource={codeSampler.ResourceIdentity}");
        }
        lines.Add($"material.shader.constantCount={material.ShaderExecution.ConstantDestinations.Count}");
        for (int constantIndex = 0; constantIndex < material.ShaderExecution.ConstantDestinations.Count; constantIndex++)
        {
            MapRenderShaderSamplerDestination constant = material.ShaderExecution.ConstantDestinations[constantIndex];
            string prefix = $"material.shader.constant.{constantIndex}";
            lines.Add($"{prefix}.type={constant.ArgumentType}");
            lines.Add($"{prefix}.dest={constant.Destination}");
            lines.Add($"{prefix}.raw=0x{constant.Argument:X8}");
            lines.Add($"{prefix}.resource={constant.ResourceIdentity}");
        }
        for (int samplerIndex = 0; samplerIndex < material.MaterialSamplers.Count; samplerIndex++)
        {
            MapRenderPickMaterialSamplerInfo samplerBinding = material.MaterialSamplers[samplerIndex];
            string prefix = $"material.materialSampler.{samplerIndex}";
            lines.Add($"{prefix}.textureName={samplerBinding.TextureName}");
            lines.Add($"{prefix}.samplerArgIndex={samplerBinding.SamplerArgIndex}");
            lines.Add($"{prefix}.samplerDest={samplerBinding.SamplerDest}");
            lines.Add($"{prefix}.samplerHash=0x{samplerBinding.SamplerHash:X8}");
            lines.Add($"{prefix}.textureSemantic=0x{samplerBinding.TextureSemantic:X2}");
            lines.Add($"{prefix}.texCoordSource={(samplerBinding.UvRoute is { } route ? $"0x{route.TexCoordSource:X2}" : string.Empty)}");
            lines.Add($"{prefix}.uvFormula={samplerBinding.UvRoute?.Formula ?? string.Empty}");
        }
        lines.Add($"uv.label={material.UvRoute.Label}");
        lines.Add($"uv.worldVertexFormat={material.UvRoute.WorldVertexFormat}");
        lines.Add($"uv.texCoordSource=0x{material.UvRoute.TexCoordSource:X2}");
        lines.Add($"uv.texCoordSourceName={material.UvRoute.TexCoordSourceName}");
        lines.Add($"uv.streamIndex={material.UvRoute.StreamIndex}");
        lines.Add($"uv.stride=0x{material.UvRoute.Stride:X}");
        lines.Add($"uv.offset=0x{material.UvRoute.Offset:X}");
        lines.Add($"uv.formatByte0=0x{material.UvRoute.FormatByte0:X2}");
        lines.Add($"uv.formatByte1=0x{material.UvRoute.FormatByte1:X2}");
        lines.Add($"uv.format={material.UvRoute.FormatName}");
        lines.Add($"uv.baseMode={material.UvRoute.BaseMode}");
        lines.Add($"uv.components={material.UvRoute.ComponentText}");
        lines.Add($"uv.transform={material.UvRoute.TransformText}");
        lines.Add($"uv.formula={material.UvRoute.Formula}");
        lines.Add($"texture.name={material.TextureName}");
        lines.Add($"texture.size={material.TextureWidth}x{material.TextureHeight}");
        lines.Add($"texture.format={material.TextureFormat}");
        lines.Add($"texture.samplerState=0x{material.SamplerState:X2}");
        lines.Add($"texture.samplerRsxTexEnableMethod=0x{MapRenderSamplerDecoder.RsxTexEnableMethod(material.SamplerDest):X4}");
        lines.Add($"texture.samplerRsxTexEnablePayload=0x{material.SamplerRsxTexEnablePayload:X8}");
        lines.Add($"texture.samplerRsxTexFilterMethod=0x{MapRenderSamplerDecoder.RsxTexFilterMethod(material.SamplerDest):X4}");
        lines.Add($"texture.samplerRsxTexFilterPayload=0x{material.SamplerRsxTexFilterPayload:X8}");
        lines.Add($"texture.samplerRsxTexWrapMethod=0x{MapRenderSamplerDecoder.RsxTexWrapMethod(material.SamplerDest):X4}");
        lines.Add($"texture.samplerRsxTexWrapPayload=0x{material.SamplerRsxTexWrapPayload:X8}");
        lines.Add($"texture.samplerRsxClampMax={material.SamplerRsxClampMax}");
        lines.Add($"texture.samplerRsxDescriptorPad0F=0x{material.SamplerRsxDescriptorPad0F:X2}");
        lines.Add($"texture.samplerRsxDescriptorPad1B=0x{material.SamplerRsxDescriptorPad1B:X2}");
        lines.Add($"texture.samplerRsxCachePayload=0x{material.SamplerRsxCachePayload:X8}");
        lines.Add($"texture.samplerFilterClass={material.SamplerFilterClass}");
        lines.Add($"texture.samplerMipClass={material.SamplerMipClass}");
        lines.Add($"texture.samplerMinFilter={material.SamplerMinFilter}");
        lines.Add($"texture.samplerMagFilter={material.SamplerMagFilter}");
        lines.Add($"texture.samplerMipFilter={material.SamplerMipFilter}");
        lines.Add($"texture.samplerMaxAnisotropy={material.SamplerMaxAnisotropy}");
        lines.Add($"texture.samplerAddressU={material.SamplerAddressU}");
        lines.Add($"texture.samplerAddressV={material.SamplerAddressV}");
        lines.Add($"texture.samplerAddressW={material.SamplerAddressW}");
        lines.Add($"texture.rsxTexOffsetMethod=0x{MapRenderRsxTextureCommandBuilder.RsxTexOffsetMethod(material.SamplerDest):X4}");
        lines.Add($"texture.rsxTexOffsetPayload=0x{material.RsxTextureCommandState.TexOffsetPayload:X8}");
        lines.Add($"texture.rsxTexFormatMethod=0x{MapRenderRsxTextureCommandBuilder.RsxTexFormatMethod(material.SamplerDest):X4}");
        lines.Add($"texture.rsxTexFormatPayload=0x{material.RsxTextureCommandState.TexFormatPayload:X8}");
        lines.Add($"texture.rsxTexNpotSizeMethod=0x{MapRenderRsxTextureCommandBuilder.RsxTexNpotSizeMethod(material.SamplerDest):X4}");
        lines.Add($"texture.rsxTexNpotSizePayload=0x{material.RsxTextureCommandState.TexNpotSizePayload:X8}");
        lines.Add($"texture.rsxTexSize1Method=0x{MapRenderRsxTextureCommandBuilder.RsxTexSize1Method(material.SamplerDest):X4}");
        lines.Add($"texture.rsxTexSize1Payload=0x{material.RsxTextureCommandState.TexSize1Payload:X8}");
        lines.Add($"texture.rsxTexSwizzleMethod=0x{MapRenderRsxTextureCommandBuilder.RsxTexSwizzleMethod(material.SamplerDest):X4}");
        lines.Add($"texture.rsxTexSwizzlePayload=0x{material.RsxTextureCommandState.TexSwizzlePayload:X8}");
        lines.Add($"texture.hasTransparency={material.TextureHasTransparency}");
        lines.Add($"texture.mipLevelCount={material.TextureMipLevelCount}");
        lines.Add($"state.hasState={state.HasState}");
        lines.Add($"state.loadBits0=0x{state.LoadBits0:X8}");
        lines.Add($"state.loadBits1=0x{state.LoadBits1:X8}");
        lines.Add($"state.tail=0x{state.Tail:X8}");
        lines.Add($"state.colorMask=0x{state.ColorMask:X8}");
        lines.Add($"state.alphaTestEnabled={state.AlphaTestEnabled}");
        lines.Add($"state.alphaFunc=0x{state.AlphaFunc:X4}");
        lines.Add($"state.alphaRef={state.AlphaRef}");
        lines.Add($"state.cullEnabled={state.CullEnabled}");
        lines.Add($"state.cullFace=0x{state.CullFace:X4}");
        lines.Add($"state.blendEnabled={state.BlendEnabled}");
        lines.Add($"state.blendEquationRgb=0x{state.BlendEquationRgb:X4}");
        lines.Add($"state.blendEquationAlpha=0x{state.BlendEquationAlpha:X4}");
        lines.Add($"state.blendSourceRgb=0x{state.BlendSourceRgb:X4}");
        lines.Add($"state.blendSourceAlpha=0x{state.BlendSourceAlpha:X4}");
        lines.Add($"state.blendDestinationRgb=0x{state.BlendDestinationRgb:X4}");
        lines.Add($"state.blendDestinationAlpha=0x{state.BlendDestinationAlpha:X4}");
        lines.Add($"state.depthTestEnabled={state.DepthTestEnabled}");
        lines.Add($"state.depthWriteEnabled={state.DepthWriteEnabled}");
        lines.Add($"state.depthFunc=0x{state.DepthFunc:X4}");
        lines.Add($"state.stencilEnabled={state.Stencil.Enabled}");
        lines.Add($"state.stencilBackFaceIndependent={state.Stencil.BackFaceStateIsIndependent}");
        lines.Add($"state.stencilFrontFunc=0x{state.Stencil.Front.Function:X4}");
        lines.Add($"state.stencilFrontRef={state.Stencil.Front.Reference}");
        lines.Add($"state.stencilFrontCompareMask=0x{state.Stencil.Front.CompareMask:X2}");
        lines.Add($"state.stencilFrontFail=0x{state.Stencil.Front.FailOperation:X4}");
        lines.Add($"state.stencilFrontDepthFail=0x{state.Stencil.Front.DepthFailOperation:X4}");
        lines.Add($"state.stencilFrontPass=0x{state.Stencil.Front.PassOperation:X4}");
        lines.Add($"state.stencilBackFunc=0x{state.Stencil.Back.Function:X4}");
        lines.Add($"state.stencilBackRef={state.Stencil.Back.Reference}");
        lines.Add($"state.stencilBackCompareMask=0x{state.Stencil.Back.CompareMask:X2}");
        lines.Add($"state.stencilBackFail=0x{state.Stencil.Back.FailOperation:X4}");
        lines.Add($"state.stencilBackDepthFail=0x{state.Stencil.Back.DepthFailOperation:X4}");
        lines.Add($"state.stencilBackPass=0x{state.Stencil.Back.PassOperation:X4}");
        lines.Add(
            $"state.stencilExecution={(state.Stencil.Enabled ? "unsupported" : "inactive")}");
        lines.Add($"state.polygonOffsetEnabled={state.PolygonOffsetEnabled}");
        lines.Add($"state.polygonOffsetFactor={state.PolygonOffsetFactor.ToString("0.###", CultureInfo.InvariantCulture)}");
        lines.Add($"state.polygonOffsetUnits={state.PolygonOffsetUnits.ToString("0.###", CultureInfo.InvariantCulture)}");
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendOverlapCandidates(
        List<string> lines,
        IReadOnlyList<MapRenderPickCandidate> candidates)
    {
        AppendPickCandidates(lines, "overlap", candidates);
    }

    private static void AppendPickCandidates(
        List<string> lines,
        string blockName,
        IReadOnlyList<MapRenderPickCandidate> candidates)
    {
        lines.Add($"{blockName}.count={candidates.Count}");
        for (int i = 0; i < candidates.Count; i++)
        {
            MapRenderPickCandidate candidate = candidates[i];
            MapRenderPickHit hit = candidate.Hit;
            MapRenderPickMaterialInfo? material = hit.Material;

            string prefix = $"{blockName}.{i}";
            lines.Add($"{prefix}.rankScore={candidate.Priority}");
            lines.Add($"{prefix}.distanceRank={candidate.DistanceRank}");
            lines.Add($"{prefix}.distance={F(hit.Distance)}");
            lines.Add($"{prefix}.layer={candidate.Layer}");
            lines.Add($"{prefix}.kind={hit.Kind}");
            lines.Add($"{prefix}.surfaceIndex={hit.SurfaceIndex}");
            lines.Add($"{prefix}.triangleIndex={hit.TriangleIndex}");
            lines.Add($"{prefix}.name={hit.Name}");
            lines.Add($"{prefix}.reason={candidate.Reason}");
            lines.Add($"{prefix}.hasColorSemantic={candidate.HasColorSemantic}");
            lines.Add($"{prefix}.hasNonDegenerateUv={candidate.HasNonDegenerateUv}");
            lines.Add($"{prefix}.uvArea={F6(candidate.UvArea)}");
            lines.Add($"{prefix}.materialName={material?.MaterialName ?? string.Empty}");
            lines.Add($"{prefix}.textureName={material?.TextureName ?? string.Empty}");
            lines.Add($"{prefix}.rendererPassClass={material?.PassClass ?? string.Empty}");
            lines.Add($"{prefix}.techniqueSlot={material?.TechniqueSlot.ToString(CultureInfo.InvariantCulture) ?? string.Empty}");
            lines.Add($"{prefix}.textureSemantic={(material is null ? string.Empty : $"0x{material.TextureSemantic:X2}")}");
            lines.Add($"{prefix}.texCoordSource={(material is null ? string.Empty : $"0x{material.TexCoordSource:X2}/{MapRenderUvRoute.StreamSourceName(material.TexCoordSource)}")}");
        }
    }

    private static string F(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string FormatFloat3(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F6(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static float ToDegrees(float radians) => radians * 180f / MathF.PI;

}
