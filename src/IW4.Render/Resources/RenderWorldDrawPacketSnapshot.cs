using System.Collections.Immutable;

using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Shaders;

namespace IW4.Render.Resources;

/// <summary>
/// Bounded compatibility profiles for loaded world packets. A profile is an
/// executable preview contract, not a claim that the compatibility shader is
/// the authored RSX program retained in the packet provenance.
/// </summary>
public enum RenderWorldDrawPacketCompatibilityProfile
{
    LoadedCameraColorBaseTextureAlphaGequal128CullFrontDepthLequalV1 = 0
}

/// <summary>
/// Exact loaded CameraColor state admitted by the first bounded world slice.
/// The values are the shared semantic/raw tuple observed on the first four
/// contiguous mp_boneyard CameraColor batches, including the shader
/// packer sRGB enable and fragment-program control tuple.
/// </summary>
public static class RenderLoadedCameraColorCompatibilityProfile
{
    public const uint LoadBits0 = 0x5812B012u;
    public const uint LoadBits1 = 0x0000000Du;
    public const uint ColorMask = 0x01010101u;
    public const uint AlphaFuncGequal = 0x0206u;
    public const byte AlphaRef = 0x80;
    public const uint CullFaceFront = 0x0404u;
    public const uint PolygonModeFill = 0x1B02u;
    public const uint BlendSourceOne = 1u;
    public const uint BlendDestinationZero = 0u;
    public const uint DepthFuncLessOrEqual = 0x0203u;
    public const uint FragmentProgramControl = 0x02008400u;
    public const string FragmentExportPrecision = "Fp16";
    public const int TechniqueSlot = 10;
    public const int PassIndex = 0;
    public const string TechniqueName = "vcsh_t0c0_nc_dfog";
    public const int SamplerArgIndex = 2;
    public const ushort SamplerDestination = 0;
    public const uint SamplerHash = 0xA0AB1041u;
    public const byte TextureSemantic = 0x02;
    public const byte TexCoordSource = 0x02;
    public const string UvLabel = "engine effective row 5";
    public const string WorldVertexFormat =
        "MTL_WORLDVERT_TEX_1_NRM_1/backendRow5";

    public static MapRenderState RequiredState { get; } =
        MapRenderStateDecoder.Decode(LoadBits0, LoadBits1, 0);

    public static bool MatchesRawState(MapRenderState state) =>
        state.HasState &&
        state.LoadBits0 == LoadBits0 &&
        state.LoadBits1 == LoadBits1 &&
        state.Tail == 0;

    public static bool Matches(MapRenderState state) =>
        MatchesRawState(state) && state == RequiredState;

    public static bool MatchesTechnique(MapRenderMaterialPass pass) =>
        pass.TechniqueSlot == TechniqueSlot &&
        pass.PassIndex == PassIndex &&
        string.Equals(
            pass.TechniqueName,
            TechniqueName,
            StringComparison.Ordinal) &&
        string.Equals(
            pass.PassClass,
            MapRenderPassClassifier.CameraColor,
            StringComparison.Ordinal);

    public static bool MatchesPass(MapRenderMaterialPass pass) =>
        MatchesTechnique(pass) &&
        pass.SamplerArgIndex == SamplerArgIndex &&
        pass.SamplerDest == SamplerDestination &&
        pass.SamplerHash == SamplerHash &&
        pass.TextureSemantic == TextureSemantic &&
        pass.TexCoordSource == TexCoordSource;

    public static bool MatchesTechnique(
        RenderMaterialPassProvenanceSnapshot pass) =>
        pass.TechniqueSlot == TechniqueSlot &&
        pass.PassIndex == PassIndex &&
        string.Equals(
            pass.TechniqueName,
            TechniqueName,
            StringComparison.Ordinal) &&
        string.Equals(
            pass.PassClass,
            MapRenderPassClassifier.CameraColor,
            StringComparison.Ordinal);

    public static bool MatchesPass(
        RenderMaterialPassProvenanceSnapshot pass) =>
        MatchesTechnique(pass) &&
        pass.SamplerArgIndex == SamplerArgIndex &&
        pass.SamplerDest == SamplerDestination &&
        pass.SamplerHash == SamplerHash &&
        pass.TextureSemantic == TextureSemantic &&
        pass.TexCoordSource == TexCoordSource;

    public static bool MatchesShader(
        MapRenderShaderExecutionContract shader) =>
        shader.FragmentProgramControl == FragmentProgramControl &&
        string.Equals(
            shader.FragmentExportPrecision,
            FragmentExportPrecision,
            StringComparison.Ordinal) &&
        !shader.FragmentDepthExportEnabled;

    public static bool MatchesShader(
        RenderWorldShaderProvenanceSnapshot shader) =>
        shader.FragmentProgramControl == FragmentProgramControl &&
        string.Equals(
            shader.FragmentExportPrecision,
            FragmentExportPrecision,
            StringComparison.Ordinal) &&
        !shader.FragmentDepthExportEnabled;

    public static bool MatchesUvRoute(MapRenderUvRoute route) =>
        string.Equals(route.Label, UvLabel, StringComparison.Ordinal) &&
        string.Equals(
            route.WorldVertexFormat,
            WorldVertexFormat,
            StringComparison.Ordinal) &&
        route.TexCoordSource == 0x02 &&
        route.StreamIndex == 1 &&
        route.Stride == 28 &&
        route.Offset == 4 &&
        route.FormatByte0 == 0x04 &&
        route.FormatByte1 == 0x02 &&
        route.BaseMode == MapRenderUvBaseMode.Engine &&
        route.ComponentA == 0 &&
        route.ComponentB == 1 &&
        route.ScaleU == 1f &&
        route.ScaleV == 1f &&
        route.AddU == 0f &&
        route.AddV == 0f;

    public static bool MatchesUvRoute(RenderMaterialUvRouteSnapshot route) =>
        string.Equals(route.Label, UvLabel, StringComparison.Ordinal) &&
        string.Equals(
            route.WorldVertexFormat,
            WorldVertexFormat,
            StringComparison.Ordinal) &&
        route.TexCoordSource == 0x02 &&
        route.StreamIndex == 1 &&
        route.Stride == 28 &&
        route.Offset == 4 &&
        route.FormatByte0 == 0x04 &&
        route.FormatByte1 == 0x02 &&
        route.BaseMode == MapRenderUvBaseMode.Engine &&
        route.ComponentA == 0 &&
        route.ComponentB == 1 &&
        route.ScaleU == 1f &&
        route.ScaleV == 1f &&
        route.AddU == 0f &&
        route.AddV == 0f;
}

/// <summary>
/// Immutable source shader facts retained beside the compatibility program.
/// The exact RSX IR remains authoritative provenance even though this first
/// loaded slice executes a separately identified base-texture program.
/// </summary>
public sealed class RenderWorldShaderProvenanceSnapshot
{
    internal RenderWorldShaderProvenanceSnapshot(
        MapRenderShaderExecutionContract source,
        string shaderExecutionStatus)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderExecutionStatus);
        ArgumentNullException.ThrowIfNull(source.VertexProgram);
        ArgumentNullException.ThrowIfNull(source.PixelProgram);
        ArgumentNullException.ThrowIfNull(source.VertexDeclarationIdentity);
        ArgumentNullException.ThrowIfNull(source.ProgramCacheKey);
        ArgumentNullException.ThrowIfNull(source.RendererBlockers);

        Purpose = source.Purpose;
        VertexProgram = source.VertexProgram with { };
        PixelProgram = source.PixelProgram with { };
        VertexDeclarationIdentity = source.VertexDeclarationIdentity;
        VertexInputs = FreezeRecords(source.VertexInputs, nameof(source));
        MaterialSamplerDestinations = FreezeRecords(
            source.MaterialSamplerDestinations,
            nameof(source));
        CustomSamplerDestinations = FreezeRecords(
            source.CustomSamplerDestinations,
            nameof(source));
        CodeSamplerDestinations = FreezeRecords(
            source.CodeSamplerDestinations,
            nameof(source));
        RuntimeSamplerRequirements = FreezeRecords(
            source.RuntimeSamplerRequirements,
            nameof(source));
        ProgramSamplerDestinations = RenderSnapshotCollections.Freeze(
            source.ProgramSamplerDestinations,
            nameof(source));
        ProgramVertexConstantDestinations = RenderSnapshotCollections.Freeze(
            source.ProgramVertexConstantDestinations,
            nameof(source));
        ConstantDestinations = FreezeRecords(
            source.ConstantDestinations,
            nameof(source));
        EmbeddedVertexConstants = FreezeRecords(
            source.EmbeddedVertexConstants,
            nameof(source));
        CodePixelConstantPatchPlans = FreezeRecords(
            source.CodePixelConstantPatchPlans,
            nameof(source));
        FragmentProgramControl = source.FragmentProgramControl;
        FragmentExportPrecision = source.FragmentExportPrecision;
        FragmentDepthExportEnabled = source.FragmentDepthExportEnabled;
        FragmentColorExports = FreezeRecords(
            source.FragmentColorExports,
            nameof(source));
        ProgramCacheKey = source.ProgramCacheKey;
        ProgramIrReady = source.ProgramIrReady;
        VertexInputPayloadReady = source.VertexInputPayloadReady;
        RendererProgramReady = source.RendererProgramReady;
        ProgramExecutionReady = source.ProgramExecutionReady;
        RendererBlockers = RenderSnapshotCollections.Freeze(
            source.RendererBlockers,
            nameof(source));
        ShaderExecutionStatus = shaderExecutionStatus;
        VertexProgramIr = source.VertexProgramIr;
        FragmentProgramIr = source.FragmentProgramIr;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public MapRenderShaderExecutionPurpose Purpose { get; }

    public MapRenderShaderProgramIdentity VertexProgram { get; }

    public MapRenderShaderProgramIdentity PixelProgram { get; }

    public string VertexDeclarationIdentity { get; }

    public ImmutableArray<MapRenderShaderVertexInputBinding> VertexInputs
        { get; }

    public ImmutableArray<MapRenderShaderSamplerDestination>
        MaterialSamplerDestinations { get; }

    public ImmutableArray<MapRenderShaderSamplerDestination>
        CustomSamplerDestinations { get; }

    public ImmutableArray<MapRenderShaderSamplerDestination>
        CodeSamplerDestinations { get; }

    public ImmutableArray<MapRenderShaderRuntimeSamplerRequirement>
        RuntimeSamplerRequirements { get; }

    public ImmutableArray<int> ProgramSamplerDestinations { get; }

    public ImmutableArray<int> ProgramVertexConstantDestinations { get; }

    public ImmutableArray<MapRenderShaderSamplerDestination>
        ConstantDestinations { get; }

    public ImmutableArray<MapRenderEmbeddedVertexConstant>
        EmbeddedVertexConstants { get; }

    public ImmutableArray<MapRenderCodePixelConstantPatchPlan>
        CodePixelConstantPatchPlans { get; }

    public uint FragmentProgramControl { get; }

    public string FragmentExportPrecision { get; }

    public bool FragmentDepthExportEnabled { get; }

    public ImmutableArray<MapRenderShaderFragmentExport> FragmentColorExports
        { get; }

    public string ProgramCacheKey { get; }

    public bool ProgramIrReady { get; }

    public bool VertexInputPayloadReady { get; }

    public bool RendererProgramReady { get; }

    public bool ProgramExecutionReady { get; }

    public ImmutableArray<string> RendererBlockers { get; }

    public string ShaderExecutionStatus { get; }

    public RsxVertexProgramIr? VertexProgramIr { get; }

    public RsxFragmentProgramIr? FragmentProgramIr { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-world-shader-provenance/v2");
        writer.WriteInt32((int)Purpose);
        AppendProgram(writer, VertexProgram);
        AppendProgram(writer, PixelProgram);
        writer.WriteString(VertexDeclarationIdentity);
        writer.WriteString(ProgramCacheKey);
        writer.WriteBoolean(ProgramIrReady);
        writer.WriteBoolean(VertexInputPayloadReady);
        writer.WriteBoolean(RendererProgramReady);
        writer.WriteBoolean(ProgramExecutionReady);
        writer.WriteString(ShaderExecutionStatus);
        writer.WriteUInt32(FragmentProgramControl);
        writer.WriteString(FragmentExportPrecision);
        writer.WriteBoolean(FragmentDepthExportEnabled);
        writer.WriteInt32(VertexInputs.Length);
        foreach (MapRenderShaderVertexInputBinding input in VertexInputs)
        {
            writer.WriteInt32(input.RouteIndex);
            writer.WriteByte(input.Source);
            writer.WriteByte(input.Destination);
            writer.WriteByte(input.StreamIndex);
            writer.WriteInt32(input.Stride);
            writer.WriteInt32(input.Offset);
            writer.WriteByte(input.ComponentCount);
            writer.WriteByte(input.RsxType);
            writer.WriteString(input.RsxTypeName);
        }
        AppendSamplerDestinations(writer, MaterialSamplerDestinations);
        AppendSamplerDestinations(writer, CustomSamplerDestinations);
        AppendSamplerDestinations(writer, CodeSamplerDestinations);
        writer.WriteInt32(RuntimeSamplerRequirements.Length);
        foreach (MapRenderShaderRuntimeSamplerRequirement requirement in
                 RuntimeSamplerRequirements)
        {
            writer.WriteInt32(requirement.ArgumentIndex);
            writer.WriteInt32(requirement.Destination);
            writer.WriteUInt32(requirement.CodeSamplerArgument);
            writer.WriteInt32((int)requirement.ResourceKind);
            writer.WriteInt32((int)requirement.Status);
            writer.WriteString(requirement.ResourceIdentity);
        }
        writer.WriteInt32(ProgramSamplerDestinations.Length);
        foreach (int destination in ProgramSamplerDestinations)
            writer.WriteInt32(destination);
        writer.WriteInt32(ProgramVertexConstantDestinations.Length);
        foreach (int destination in ProgramVertexConstantDestinations)
            writer.WriteInt32(destination);
        AppendSamplerDestinations(writer, ConstantDestinations);
        writer.WriteInt32(EmbeddedVertexConstants.Length);
        foreach (MapRenderEmbeddedVertexConstant constant in
                 EmbeddedVertexConstants)
        {
            writer.WriteInt32(constant.ParameterOrdinal);
            writer.WriteInt32(constant.Destination);
            writer.WriteUInt32(constant.RawResourceIndex);
            writer.WriteString(constant.ParameterName);
            writer.WriteUInt32(constant.DefaultValueOffset);
            writer.WriteSingle(constant.Value.X);
            writer.WriteSingle(constant.Value.Y);
            writer.WriteSingle(constant.Value.Z);
            writer.WriteSingle(constant.Value.W);
            writer.WriteBoolean(constant.IsOperationallyResolved);
        }
        writer.WriteInt32(CodePixelConstantPatchPlans.Length);
        foreach (MapRenderCodePixelConstantPatchPlan plan in
                 CodePixelConstantPatchPlans)
        {
            writer.WriteInt32(plan.ArgumentOrdinal);
            writer.WriteInt32(plan.Destination);
            writer.WriteInt32(plan.ArgumentRaw);
            writer.WriteInt32(plan.CodeIndex);
            writer.WriteInt32((int)plan.Status);
            writer.WriteBoolean(plan.Detail is not null);
            if (plan.Detail is not null)
                writer.WriteString(plan.Detail);
            writer.WriteInt32(plan.PatchSites.Count);
            foreach (MapRenderCodePixelConstantPatchSite site in
                     plan.PatchSites)
            {
                writer.WriteInt32(site.RelativePatchOffset);
                writer.WriteInt32(site.ProgramByteOffset);
                writer.WriteBoolean(site.InstructionIndex.HasValue);
                if (site.InstructionIndex.HasValue)
                    writer.WriteInt32(site.InstructionIndex.Value);
            }
        }
        writer.WriteInt32(FragmentColorExports.Length);
        foreach (MapRenderShaderFragmentExport export in FragmentColorExports)
        {
            writer.WriteInt32(export.ColorTarget);
            writer.WriteString(export.Register);
            writer.WriteByte(export.WrittenComponentMask);
            writer.WriteString(export.WrittenComponents);
        }
        writer.WriteInt32(RendererBlockers.Length);
        foreach (string blocker in RendererBlockers)
            writer.WriteString(blocker);
        writer.WriteBoolean(VertexProgramIr is not null);
        if (VertexProgramIr is not null)
        {
            writer.WriteString(VertexProgramIr.Identity);
            writer.WriteBytes(VertexProgramIr.InputProgramBytes);
        }
        writer.WriteBoolean(FragmentProgramIr is not null);
        if (FragmentProgramIr is not null)
        {
            writer.WriteString(FragmentProgramIr.Identity);
            writer.WriteBytes(FragmentProgramIr.OriginalProgramBytes);
            writer.WriteBytes(FragmentProgramIr.EffectiveProgramBytes);
        }
    }

    private static ImmutableArray<T> FreezeRecords<T>(
        IEnumerable<T> source,
        string parameterName)
        where T : class
    {
        ImmutableArray<T> frozen = RenderSnapshotCollections.Freeze(
            source,
            parameterName);
        if (frozen.Any(value => value is null))
        {
            throw new ArgumentException(
                "Shader provenance collections cannot contain null entries.",
                parameterName);
        }
        return frozen;
    }

    private static void AppendProgram(
        RenderContentDigestWriter writer,
        MapRenderShaderProgramIdentity program)
    {
        writer.WriteString(program.Stage);
        writer.WriteString(program.Name);
        writer.WriteUInt32(program.DeclaredDataSize);
        writer.WriteInt32(program.LoadedDataSize);
        writer.WriteString(program.DataSha256);
        writer.WriteBoolean(program.HasProgramData);
    }

    private static void AppendSamplerDestinations(
        RenderContentDigestWriter writer,
        ImmutableArray<MapRenderShaderSamplerDestination> destinations)
    {
        writer.WriteInt32(destinations.Length);
        foreach (MapRenderShaderSamplerDestination destination in destinations)
        {
            writer.WriteInt32(destination.ArgumentIndex);
            writer.WriteString(destination.ArgumentType);
            writer.WriteInt32(destination.Destination);
            writer.WriteUInt32(destination.Argument);
            writer.WriteString(destination.ResourceIdentity);
            writer.WriteBoolean(destination.IsOperationallyResolved);
            writer.WriteString(destination.TextureTarget);
            AppendNullableSingle(writer, destination.X);
            AppendNullableSingle(writer, destination.Y);
            AppendNullableSingle(writer, destination.Z);
            AppendNullableSingle(writer, destination.W);
            writer.WriteBoolean(destination.CodeMatrixSemantic.HasValue);
            if (destination.CodeMatrixSemantic.HasValue)
            {
                writer.WriteInt32((int)destination.CodeMatrixSemantic.Value);
            }
            writer.WriteInt32((int)destination.CodeMatrixTransform);
            writer.WriteInt32(destination.CodeMatrixRow);
            writer.WriteBoolean(destination.CodeConstantSourceRow.HasValue);
            if (destination.CodeConstantSourceRow.HasValue)
                writer.WriteInt32(destination.CodeConstantSourceRow.Value);
        }
    }

    private static void AppendNullableSingle(
        RenderContentDigestWriter writer,
        float? value)
    {
        writer.WriteBoolean(value.HasValue);
        if (value.HasValue)
            writer.WriteSingle(value.Value);
    }
}

/// <summary>
/// One immutable loaded CameraColor batch admitted for a bounded base-texture
/// compatibility draw. It is not the synthetic material-preview packet and it
/// carries no DPVS or full-map visibility claim.
/// </summary>
public sealed class RenderWorldDrawPacketSnapshot
{
    public const int VertexStrideBytes =
        MapRenderScene.TexturedVertexFloatCount * sizeof(float);

    public const int RsxVertexInputFloatStride = 16 * 4;

    internal RenderWorldDrawPacketSnapshot(
        RenderWorldDrawPacketCompatibilityProfile profile,
        int sourceOrdinal,
        RenderMaterialPassProvenanceSnapshot sourcePass,
        RenderMaterialTextureBindingProvenanceSnapshot baseTextureBinding,
        RenderMaterialUvRouteSnapshot uvRoute,
        MapRenderState sourceState,
        byte sceneLightIndex,
        RenderWorldShaderProvenanceSnapshot shaderProvenance,
        IEnumerable<MapRenderPickRange> surfaceRanges,
        IEnumerable<float> rsxVertexInputs,
        RenderSemanticIdentity fullBatchDrawIdentity,
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryDescriptor geometry,
        RenderTextureDescriptor texture,
        RenderSamplerDescriptor sampler)
    {
        if (!Enum.IsDefined(profile))
            throw new ArgumentOutOfRangeException(nameof(profile));
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        ArgumentNullException.ThrowIfNull(sourcePass);
        ArgumentNullException.ThrowIfNull(baseTextureBinding);
        ArgumentNullException.ThrowIfNull(uvRoute);
        ArgumentNullException.ThrowIfNull(shaderProvenance);
        ArgumentNullException.ThrowIfNull(surfaceRanges);
        ArgumentNullException.ThrowIfNull(rsxVertexInputs);
        ArgumentNullException.ThrowIfNull(vertexLayout);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(sampler);
        RenderVertexLayoutDescriptor.RequireIdentity(
            fullBatchDrawIdentity,
            RenderSemanticResourceKind.Draw);

        ImmutableArray<RenderMaterialPickRangeSnapshot> frozenRanges =
            RenderSnapshotCollections.Freeze(
                surfaceRanges.Select(range =>
                    new RenderMaterialPickRangeSnapshot(range)),
                nameof(surfaceRanges));
        ImmutableArray<float> frozenRsxVertexInputs =
            RenderSnapshotCollections.Freeze(
                rsxVertexInputs,
                nameof(rsxVertexInputs));
        ValidateSource(
            profile,
            sourcePass,
            baseTextureBinding,
            uvRoute,
            sourceState,
            shaderProvenance);
        ValidateResources(vertexLayout, geometry, texture, sampler);
        if (!string.Equals(
                baseTextureBinding.TextureName,
                texture.Name,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Loaded CameraColor texture provenance must name the snapshotted texture.",
                nameof(texture));
        }
        ValidateRanges(frozenRanges, geometry.IndexCount);
        if (frozenRsxVertexInputs.Length != checked(
                geometry.VertexCount * RsxVertexInputFloatStride) ||
            frozenRsxVertexInputs.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException(
                "Loaded CameraColor RSX vertex inputs must retain 16 float4 values per geometry vertex.",
                nameof(rsxVertexInputs));
        }

        Profile = profile;
        SourceOrdinal = sourceOrdinal;
        SourcePass = sourcePass;
        BaseTextureBinding = baseTextureBinding;
        UvRoute = uvRoute;
        SourceState = sourceState;
        SceneLightIndex = sceneLightIndex;
        ShaderProvenance = shaderProvenance;
        SurfaceRanges = frozenRanges;
        RsxVertexInputs = frozenRsxVertexInputs;
        FullBatchDrawIdentity = fullBatchDrawIdentity;
        VertexLayout = vertexLayout;
        Geometry = geometry;
        Texture = texture;
        Sampler = sampler;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderWorldDrawPacketCompatibilityProfile Profile { get; }

    public int SourceOrdinal { get; }

    public RenderMaterialPassProvenanceSnapshot SourcePass { get; }

    public RenderMaterialTextureBindingProvenanceSnapshot BaseTextureBinding
        { get; }

    public RenderMaterialUvRouteSnapshot UvRoute { get; }

    public MapRenderState SourceState { get; }

    public byte SceneLightIndex { get; }

    public RenderWorldShaderProvenanceSnapshot ShaderProvenance { get; }

    public ImmutableArray<RenderMaterialPickRangeSnapshot> SurfaceRanges
        { get; }

    public ImmutableArray<float> RsxVertexInputs { get; }

    public RenderSemanticIdentity FullBatchDrawIdentity { get; }

    public RenderVertexLayoutDescriptor VertexLayout { get; }

    public RenderGeometryDescriptor Geometry { get; }

    public RenderTextureDescriptor Texture { get; }

    public RenderSamplerDescriptor Sampler { get; }

    public RenderSemanticIdentity VertexLayoutIdentity =>
        VertexLayout.Identity;

    public RenderSemanticIdentity GeometryIdentity => Geometry.Identity;

    public RenderSemanticIdentity TextureIdentity => Texture.Identity;

    public RenderSemanticIdentity SamplerIdentity => Sampler.Identity;

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-world-draw-packet/v1");
        writer.WriteInt32((int)Profile);
        writer.WriteInt32(SourceOrdinal);
        SourcePass.AppendContent(writer);
        BaseTextureBinding.AppendContent(writer);
        UvRoute.AppendContent(writer);
        AppendState(writer, SourceState);
        writer.WriteByte(SceneLightIndex);
        ShaderProvenance.AppendContent(writer);
        writer.WriteInt32(SurfaceRanges.Length);
        foreach (RenderMaterialPickRangeSnapshot range in SurfaceRanges)
            range.AppendContent(writer);
        writer.WriteInt32(RsxVertexInputs.Length);
        foreach (float value in RsxVertexInputs)
            writer.WriteSingle(value);
        writer.WriteIdentity(FullBatchDrawIdentity);
        writer.WriteIdentity(VertexLayout.Identity);
        writer.WriteString(VertexLayout.ContentDigest);
        writer.WriteIdentity(Geometry.Identity);
        writer.WriteString(Geometry.ContentDigest);
        writer.WriteIdentity(Texture.Identity);
        writer.WriteString(Texture.ContentDigest);
        writer.WriteIdentity(Sampler.Identity);
        writer.WriteString(Sampler.ContentDigest);
    }

    internal static void ValidateRanges(
        ImmutableArray<RenderMaterialPickRangeSnapshot> ranges,
        int geometryIndexCount)
    {
        if (ranges.IsDefaultOrEmpty)
            throw new ArgumentException("A loaded world packet requires GfxSurface ranges.", nameof(ranges));

        int expectedFirstIndex = 0;
        var surfaceIndices = new HashSet<int>();
        foreach (RenderMaterialPickRangeSnapshot range in ranges)
        {
            if (range.Kind != MapRenderPickKind.GfxSurface ||
                range.ObjectIndex < 0 ||
                range.SurfaceIndex < 0 ||
                range.ObjectIndex != range.SurfaceIndex ||
                !surfaceIndices.Add(range.SurfaceIndex) ||
                range.FirstIndex != expectedFirstIndex ||
                range.IndexCount <= 0 ||
                range.IndexCount % 3 != 0)
            {
                throw new ArgumentException(
                    "Loaded world ranges must be unique, ordered, contiguous, positive triangle-aligned GfxSurface ownership.",
                    nameof(ranges));
            }
            expectedFirstIndex = checked(
                range.FirstIndex + range.IndexCount);
            if (expectedFirstIndex > geometryIndexCount)
            {
                throw new ArgumentException(
                    "A loaded world range exceeds its geometry.",
                    nameof(ranges));
            }
        }
        if (expectedFirstIndex != geometryIndexCount)
        {
            throw new ArgumentException(
                "Loaded world ranges must cover the complete geometry.",
                nameof(ranges));
        }
    }

    private static void ValidateSource(
        RenderWorldDrawPacketCompatibilityProfile profile,
        RenderMaterialPassProvenanceSnapshot pass,
        RenderMaterialTextureBindingProvenanceSnapshot binding,
        RenderMaterialUvRouteSnapshot uvRoute,
        MapRenderState state,
        RenderWorldShaderProvenanceSnapshot shader)
    {
        if (profile != RenderWorldDrawPacketCompatibilityProfile
                .LoadedCameraColorBaseTextureAlphaGequal128CullFrontDepthLequalV1 ||
            !RenderLoadedCameraColorCompatibilityProfile.MatchesPass(pass) ||
            shader.Purpose != MapRenderShaderExecutionPurpose.CameraColor ||
            !shader.ProgramIrReady ||
            !shader.VertexInputPayloadReady ||
            !shader.RendererProgramReady ||
            !shader.ProgramExecutionReady ||
            !shader.RendererBlockers.IsEmpty ||
            !shader.RuntimeSamplerRequirements.IsEmpty ||
            !shader.VertexProgram.HasProgramData ||
            !shader.PixelProgram.HasProgramData ||
            shader.VertexProgramIr is null ||
            shader.FragmentProgramIr is null ||
            !shader.VertexProgramIr.HasValidUpload ||
            !shader.FragmentProgramIr.HasValidUpload ||
            !string.Equals(
                shader.ShaderExecutionStatus,
                "RENDERER_PROGRAM_EXECUTION_READY",
                StringComparison.Ordinal) ||
            shader.MaterialSamplerDestinations.Length != 1 ||
            shader.MaterialSamplerDestinations[0].ArgumentIndex !=
                RenderLoadedCameraColorCompatibilityProfile.SamplerArgIndex ||
            shader.MaterialSamplerDestinations[0].Destination !=
                RenderLoadedCameraColorCompatibilityProfile
                    .SamplerDestination ||
            !shader.MaterialSamplerDestinations[0]
                .IsOperationallyResolved ||
            !string.Equals(
                shader.MaterialSamplerDestinations[0].TextureTarget,
                "Texture2D",
                StringComparison.Ordinal) ||
            !shader.CustomSamplerDestinations.IsEmpty ||
            !shader.CodeSamplerDestinations.IsEmpty ||
            shader.ProgramSamplerDestinations.Length != 1 ||
            shader.ProgramSamplerDestinations[0] !=
                RenderLoadedCameraColorCompatibilityProfile
                    .SamplerDestination ||
            !RenderLoadedCameraColorCompatibilityProfile.Matches(state) ||
            !RenderLoadedCameraColorCompatibilityProfile.MatchesShader(
                shader) ||
            !RenderLoadedCameraColorCompatibilityProfile.MatchesUvRoute(
                uvRoute))
        {
            throw new ArgumentException(
                "A loaded world packet does not match its exact CameraColor compatibility profile.");
        }
        if (binding.LayerIndex != 0 ||
            binding.BlendWeightComponent != -1 ||
            binding.SamplerArgIndex !=
                RenderLoadedCameraColorCompatibilityProfile.SamplerArgIndex ||
            binding.SamplerDest !=
                RenderLoadedCameraColorCompatibilityProfile
                    .SamplerDestination ||
            binding.SamplerHash !=
                RenderLoadedCameraColorCompatibilityProfile.SamplerHash ||
            binding.TextureSemantic !=
                RenderLoadedCameraColorCompatibilityProfile.TextureSemantic ||
            binding.SamplerArgIndex != pass.SamplerArgIndex ||
            binding.SamplerDest != pass.SamplerDest ||
            binding.SamplerHash != pass.SamplerHash ||
            binding.TextureSemantic != pass.TextureSemantic ||
            binding.WorldRuntimeTextureIdentity is not null ||
            uvRoute.TexCoordSource != pass.TexCoordSource)
        {
            throw new ArgumentException(
                "Loaded CameraColor pass, base texture, and UV provenance do not match.");
        }
    }

    private static void ValidateResources(
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryDescriptor geometry,
        RenderTextureDescriptor texture,
        RenderSamplerDescriptor sampler)
    {
        if (vertexLayout.StrideBytes != VertexStrideBytes ||
            vertexLayout.Elements.Length != 2 ||
            vertexLayout.Elements[0] != new RenderVertexElementDescriptor(
                RenderVertexSemantic.Position,
                0,
                RenderVertexElementFormat.Float32x3,
                0) ||
            vertexLayout.Elements[1] != new RenderVertexElementDescriptor(
                RenderVertexSemantic.TextureCoordinate,
                0,
                RenderVertexElementFormat.Float32x2,
                3 * sizeof(float)) ||
            geometry.VertexLayout != vertexLayout.Identity ||
            !string.Equals(
                geometry.VertexLayoutContentDigest,
                vertexLayout.ContentDigest,
                StringComparison.Ordinal) ||
            geometry.VertexStrideBytes != VertexStrideBytes ||
            geometry.CoordinateSpace != RenderGeometryCoordinateSpace.Render ||
            geometry.Topology != RenderPrimitiveTopology.TriangleList ||
            geometry.IndexFormat != RenderIndexFormat.Unsigned32 ||
            texture.Dimension != RenderTextureDimension.Texture2D ||
            texture.ArrayLayerCount != 1 ||
            texture.FaceCount != 1 ||
            texture.Subresources.Any(subresource =>
                !subresource.Payloads.Any(payload =>
                    payload.Kind == RenderTexturePayloadKind.DecodedRgba8)))
        {
            throw new ArgumentException(
                "Loaded CameraColor compatibility resources must be stride-88 U32 triangles with a complete decoded RGBA8 Texture2D.");
        }
        RenderVertexLayoutDescriptor.RequireIdentity(
            sampler.Identity,
            RenderSemanticResourceKind.Sampler);
    }

    private static void AppendState(
        RenderContentDigestWriter writer,
        MapRenderState state)
    {
        writer.WriteBoolean(state.HasState);
        writer.WriteUInt32(state.LoadBits0);
        writer.WriteUInt32(state.LoadBits1);
        writer.WriteUInt32(state.Tail);
        writer.WriteBoolean(state.ShaderPackerSrgbEnabled);
        writer.WriteUInt32(state.ColorMask);
        writer.WriteBoolean(state.AlphaTestEnabled);
        writer.WriteUInt32(state.AlphaFunc);
        writer.WriteByte(state.AlphaRef);
        writer.WriteBoolean(state.CullEnabled);
        writer.WriteUInt32(state.CullFace);
        writer.WriteUInt32(state.PolygonMode);
        writer.WriteBoolean(state.BlendEnabled);
        writer.WriteUInt32(state.BlendEquationRgb);
        writer.WriteUInt32(state.BlendEquationAlpha);
        writer.WriteUInt32(state.BlendSourceRgb);
        writer.WriteUInt32(state.BlendSourceAlpha);
        writer.WriteUInt32(state.BlendDestinationRgb);
        writer.WriteUInt32(state.BlendDestinationAlpha);
        writer.WriteBoolean(state.DepthTestEnabled);
        writer.WriteBoolean(state.DepthWriteEnabled);
        writer.WriteUInt32(state.DepthFunc);
        writer.WriteBoolean(state.Stencil.Enabled);
        writer.WriteBoolean(state.Stencil.BackFaceStateIsIndependent);
        AppendStencilFace(writer, state.Stencil.Front);
        AppendStencilFace(writer, state.Stencil.Back);
        writer.WriteBoolean(state.PolygonOffsetEnabled);
        writer.WriteSingle(state.PolygonOffsetFactor);
        writer.WriteSingle(state.PolygonOffsetUnits);
    }

    private static void AppendStencilFace(
        RenderContentDigestWriter writer,
        MapRenderStencilFaceState state)
    {
        writer.WriteUInt32(state.Function);
        writer.WriteInt32(state.Reference);
        writer.WriteUInt32(state.CompareMask);
        writer.WriteUInt32(state.FailOperation);
        writer.WriteUInt32(state.DepthFailOperation);
        writer.WriteUInt32(state.PassOperation);
    }
}

public enum RenderWorldDrawPacketCandidateRejectionCode
{
    NullBatch,
    MissingPass,
    MissingMaterialIdentity,
    NotLoadedCameraColorPass,
    UnsupportedTechniqueIdentity,
    MissingShaderExecution,
    ShaderPurposeNotCameraColor,
    ProgramIrNotReady,
    ProgramIrMissing,
    VertexInputPayloadNotReady,
    RendererProgramNotReady,
    ProgramExecutionNotReady,
    ShaderExecutionContractInconsistent,
    UnsupportedShaderProfile,
    RuntimeSamplerRequirementsPresent,
    UnsupportedSamplerProgramShape,
    LightmapPresent,
    ColorLayerCountNotOne,
    MaterialSamplerCountNotOne,
    BaseTextureBindingMismatch,
    UnresolvedCodeSamplers,
    DepthPrepassPresent,
    UnsupportedStateProfile,
    SourceStateInconsistentWithRawBits,
    UnsupportedUvProfile,
    GeometryMissingOrMalformed,
    RsxVertexPayloadMissingOrMalformed,
    TextureNotTwoDimensional,
    DecodedRgbaMipChainIncomplete,
    MissingGfxSurfaceRanges,
    PickRangeKindNotGfxSurface,
    NegativeSurfaceOrObjectIndex,
    GfxSurfaceOwnershipMismatch,
    DuplicateSurfaceIndex,
    PickRangeFirstIndexNotContiguous,
    PickRangeIndexCountNotPositive,
    PickRangeIndexCountNotTriangleAligned,
    PickRangeExceedsGeometry,
    PickRangesDoNotCoverGeometry,
    ResourceSnapshotCreationFailed
}

public sealed class RenderWorldDrawPacketCandidateRejection
{
    internal RenderWorldDrawPacketCandidateRejection(
        int sourceOrdinal,
        IEnumerable<RenderWorldDrawPacketCandidateRejectionCode> codes)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        ImmutableArray<RenderWorldDrawPacketCandidateRejectionCode>
            frozenCodes = RenderSnapshotCollections.Freeze(
                codes,
                nameof(codes));
        if (frozenCodes.IsEmpty ||
            frozenCodes.Any(code => !Enum.IsDefined(code)) ||
            frozenCodes.Distinct().Count() != frozenCodes.Length)
        {
            throw new ArgumentException(
                "World candidate rejection codes must be non-empty, defined, and unique.",
                nameof(codes));
        }

        SourceOrdinal = sourceOrdinal;
        Codes = frozenCodes;
        Reason = string.Join(",", frozenCodes.Select(code => code.ToString()));
    }

    public int SourceOrdinal { get; }

    public ImmutableArray<RenderWorldDrawPacketCandidateRejectionCode> Codes
        { get; }

    public string Reason { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteInt32(SourceOrdinal);
        writer.WriteInt32(Codes.Length);
        foreach (RenderWorldDrawPacketCandidateRejectionCode code in Codes)
            writer.WriteInt32((int)code);
    }
}

public enum RenderWorldDrawPacketAdmissionFailure
{
    None,
    SourceCollectionMissing,
    NoSourceBatches,
    NoEligibleLoadedCameraColorBatch
}

/// <summary>
/// First bounded loaded CameraColor world admission. Failure remains immutable
/// scene data so document-only and unsupported maps still produce snapshots.
/// </summary>
public sealed class RenderWorldDrawPacketAdmission
{
    internal RenderWorldDrawPacketAdmission(
        RenderWorldDrawPacketSnapshot? packet,
        IEnumerable<RenderWorldDrawPacketCandidateRejection> rejections,
        RenderWorldDrawPacketAdmissionFailure failure,
        string? rejectionReason)
    {
        if (!Enum.IsDefined(failure))
            throw new ArgumentOutOfRangeException(nameof(failure));
        ImmutableArray<RenderWorldDrawPacketCandidateRejection>
            frozenRejections = RenderSnapshotCollections.Freeze(
                rejections,
                nameof(rejections));
        if (frozenRejections.Any(value => value is null) ||
            frozenRejections.Select(value => value.SourceOrdinal)
                .Distinct().Count() != frozenRejections.Length)
        {
            throw new ArgumentException(
                "World candidate rejections must be non-null with unique source ordinals.",
                nameof(rejections));
        }
        for (int index = 1; index < frozenRejections.Length; index++)
        {
            if (frozenRejections[index - 1].SourceOrdinal >=
                frozenRejections[index].SourceOrdinal)
            {
                throw new ArgumentException(
                    "World candidate rejections must preserve ascending source order.",
                    nameof(rejections));
            }
        }
        bool admitted = packet is not null;
        if (admitted != (failure == RenderWorldDrawPacketAdmissionFailure.None) ||
            admitted == !string.IsNullOrWhiteSpace(rejectionReason))
        {
            throw new ArgumentException(
                "World packet, failure, and rejection reason do not form a valid admission result.");
        }
        if (packet is not null &&
            frozenRejections.Any(value =>
                value.SourceOrdinal >= packet.SourceOrdinal))
        {
            throw new ArgumentException(
                "Only candidates before the admitted world packet may be rejected.",
                nameof(rejections));
        }

        Packet = packet;
        CandidateRejections = frozenRejections;
        Failure = failure;
        RejectionReason = rejectionReason;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderWorldDrawPacketSnapshot? Packet { get; }

    public ImmutableArray<RenderWorldDrawPacketCandidateRejection>
        CandidateRejections { get; }

    public RenderWorldDrawPacketAdmissionFailure Failure { get; }

    public string? RejectionReason { get; }

    public bool IsAdmitted => Packet is not null;

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-world-draw-packet-admission/v1");
        writer.WriteInt32((int)Failure);
        writer.WriteBoolean(RejectionReason is not null);
        if (RejectionReason is not null)
            writer.WriteString(RejectionReason);
        writer.WriteInt32(CandidateRejections.Length);
        foreach (RenderWorldDrawPacketCandidateRejection rejection in
                 CandidateRejections)
        {
            rejection.AppendContent(writer);
        }
        writer.WriteBoolean(Packet is not null);
        Packet?.AppendContent(writer);
    }
}
