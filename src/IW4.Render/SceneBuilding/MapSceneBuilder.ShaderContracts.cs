using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    internal static MapRenderShaderExecutionContract BuildShaderExecutionContract(
        MaterialAsset? material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass,
        IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
        bool vertexInputPayloadReady,
        string vertexInputPayloadBlocker,
        bool authoredSourcePassAvailable,
        MapRenderShaderExecutionPurpose purpose =
            MapRenderShaderExecutionPurpose.CameraColor,
        MapRenderShaderTranslationCache? shaderTranslationCache = null,
        int? fixedVertexSourceBackendRow = null,
        IReadOnlySet<int>? explicitCubeSamplerDestinations = null)
    {
        MaterialPassAsset? sourcePass = null;
        MaterialTechniqueAsset? sourceTechnique = null;
        MaterialVertexDeclarationAsset? vertexDecl = null;
        IReadOnlyList<MaterialShaderArgumentAsset> args = [];
        MapRenderSelectedPassProgramSources? programSources = null;

        if (techset is not null &&
            selectedPass.Pass.TechniqueSlot >= 0 &&
            selectedPass.Pass.PassIndex >= 0)
        {
            MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
                .FirstOrDefault(candidate => candidate.Index == selectedPass.Pass.TechniqueSlot);
            if (slot?.Technique is { } technique &&
                (uint)selectedPass.Pass.PassIndex < (uint)technique.Passes.Count)
            {
                sourceTechnique = technique;
                sourcePass = technique.Passes[selectedPass.Pass.PassIndex];
                programSources = lookup.ResolveSources(
                    techset,
                    technique,
                    new MapRenderSelectedTechniquePass(
                        selectedPass.Pass.PassIndex,
                        sourcePass));
                vertexDecl = programSources.VertexDeclaration;
                args = programSources.Arguments;
            }
        }

        // Shadow-only/effect materials are intentionally retained by the
        // EditorPreview scene as generic textured surfaces. They have no
        // authored camera-color pass, so do not manufacture an authored
        // translation from their technique metadata. Publish the explicit
        // backend-neutral generic fallback contract instead.
        if (!authoredSourcePassAvailable)
        {
            return BuildGenericMaterialFallbackShaderContract(
                selectedPass,
                materialSamplers,
                vertexInputPayloadReady,
                vertexInputPayloadBlocker,
                purpose);
        }

        MapRenderShaderProgramIdentity vertexProgram = CreateShaderProgramIdentity(
            "Vertex",
            programSources?.VertexProgram);
        MapRenderShaderProgramIdentity pixelProgram = CreateShaderProgramIdentity(
            "Pixel",
            programSources?.PixelProgram);

        MapRenderShaderSamplerDestination[] materialDestinations = materialSamplers
            .Where(binding => binding.SamplerArgIndex >= 0)
                .Select(binding => new MapRenderShaderSamplerDestination(
                binding.SamplerArgIndex,
                nameof(MaterialShaderArgumentType.MaterialPixelSampler),
                binding.SamplerDest,
                binding.SamplerHash,
                binding.TextureName,
                IsOperationallyResolved:
                    binding.Texture is not null &&
                    binding.UvRoute is not null))
            .ToArray();

        MapRenderShaderSamplerDestination[] customDestinations = materialSamplers
            .Where(binding => binding.SamplerArgIndex < 0)
            .Select(binding => new MapRenderShaderSamplerDestination(
                binding.SamplerArgIndex,
                "CustomPixelSampler",
                binding.SamplerDest,
                binding.SamplerHash,
                binding.TextureName,
                IsOperationallyResolved: false,
                TextureTarget:
                    explicitCubeSamplerDestinations?.Contains(
                        binding.SamplerDest) == true ||
                    binding.SamplerDest == 1
                        ? "TextureCube"
                        : "Texture2D"))
            .ToArray();

        MapRenderShaderSamplerDestination[] codeDestinations = args
            .Select((arg, index) => (Arg: arg, Index: index))
            .Where(item => item.Arg.Type == MaterialShaderArgumentType.CodePixelSampler)
            .Select(item => CreateCodeSamplerDestination(item.Index, item.Arg))
            .ToArray();
        MapRenderShaderRuntimeSamplerRequirement[] runtimeSamplerRequirements =
            CreateRuntimeSamplerRequirements(codeDestinations);

        MapRenderShaderSamplerDestination[] constantDestinations = args
            .Select((arg, index) => (Arg: arg, Index: index))
            .Where(item => item.Arg.Type is not MaterialShaderArgumentType.MaterialPixelSampler and
                                           not MaterialShaderArgumentType.CodePixelSampler)
            .SelectMany(item => CreateConstantDestinations(
                material,
                item.Index,
                item.Arg))
            .ToArray();

        RsxShaderTranslationResult? translation = null;
        if (authoredSourcePassAvailable &&
            sourcePass is not null &&
            programSources?.VertexProgram.HasProgramData == true &&
            programSources.PixelProgram.HasProgramData)
        {
            IReadOnlySet<int> cubeSamplerDestinations = customDestinations
                .Concat(codeDestinations)
                .Where(binding => string.Equals(
                    binding.TextureTarget,
                    "TextureCube",
                    StringComparison.Ordinal))
                .Select(binding => (int)binding.Destination)
                .ToHashSet();
            IReadOnlySet<int> shadowSamplerDestinations =
                codeDestinations
                    .Where(binding => string.Equals(
                        binding.TextureTarget,
                        "Texture2DShadow",
                        StringComparison.Ordinal))
                    .Select(binding => (int)binding.Destination)
                    .ToHashSet();
            IReadOnlySet<int> volumeSamplerDestinations =
                codeDestinations
                    .Where(binding => string.Equals(
                        binding.TextureTarget,
                        "Texture3D",
                        StringComparison.Ordinal))
                    .Select(binding => (int)binding.Destination)
                    .ToHashSet();
            translation = shaderTranslationCache is null
                ? RsxShaderTranslator.Translate(
                    programSources.VertexProgram.Data.ToArray(),
                    programSources.PixelProgram.Data.ToArray(),
                    CreateShaderTranslationPassSnapshot(sourcePass, args),
                    material,
                    cubeSamplerDestinations,
                    shadowSamplerDestinations,
                    volumeSamplerDestinations)
                : shaderTranslationCache.Resolve(
                    material,
                    sourcePass,
                    programSources,
                    cubeSamplerDestinations,
                    shadowSamplerDestinations,
                    volumeSamplerDestinations);
        }

        // Translation owns the semantic decode whenever a complete authored
        // program is available. Reuse that exact result for declaration
        // filtering instead of decoding the program bytes once before
        // Translate and again inside Translate. The fallback preserves the
        // existing declaration-only behavior when pixel data or an authored
        // pass is unavailable, so it never overlaps a translation operation.
        IReadOnlyList<int>? requiredVertexInputs = null;
        if (programSources?.VertexProgram.HasProgramData == true)
        {
            requiredVertexInputs = translation?.ReadVertexInputDestinations ??
                RsxShaderTranslator.ReadVertexInputDestinations(
                    programSources.VertexProgram.Data.ToArray());
        }
        MapRenderShaderVertexInputBinding[] vertexInputs =
            CreateVertexInputBindings(
                techset,
                sourceTechnique?.Flags ?? 0,
                vertexDecl,
                requiredVertexInputs,
                fixedVertexSourceBackendRow);

        string vertexDeclIdentity = vertexDecl is null
            ? string.Empty
            : $"streams={vertexDecl.StreamCount};optional={vertexDecl.HasOptionalSource};routes={string.Join(',', vertexDecl.Routing.Select(route => $"{route.Source:X2}>{route.Dest:X2}"))}";
        string cacheMaterial = string.Join('|',
            vertexProgram.DataSha256,
            pixelProgram.DataSha256,
            vertexDeclIdentity,
            string.Join(',', vertexInputs.Select(input => $"v{input.Source}>{input.Destination}:s{input.StreamIndex}:o{input.Offset}:n{input.ComponentCount}:t{input.RsxType}")),
            string.Join(',', materialDestinations.Select(binding => $"m{binding.Destination}")),
            string.Join(',', customDestinations.Select(binding => $"u{binding.Destination}:{binding.TextureTarget}")),
            string.Join(',', codeDestinations.Select(binding =>
                $"c{binding.Destination}:raw{binding.Argument}:{binding.TextureTarget}")),
            string.Join(',', runtimeSamplerRequirements.Select(requirement =>
                $"r{requirement.Destination}:{requirement.ResourceKind}:{requirement.Status}")),
            string.Join(',', constantDestinations.Select(binding =>
                $"k{binding.ArgumentType}:{binding.Destination}:{binding.Argument:X8}:{binding.ResourceIdentity}:" +
                $"{FloatIdentity(binding.X)}:{FloatIdentity(binding.Y)}:{FloatIdentity(binding.Z)}:{FloatIdentity(binding.W)}")),
            translation is null ? string.Empty : string.Join(',',
                translation.ReadVertexConstantDestinations.Select(destination => $"vr{destination}")),
            translation is null ? string.Empty : string.Join(',',
                translation.EmbeddedVertexConstants.Select(constant =>
                    $"ve{constant.Destination}:{constant.RawResourceIndex:X8}:{constant.ParameterOrdinal}:" +
                    $"{constant.DefaultValueOffset:X8}:{FloatIdentity(constant.Value.X)}:" +
                    $"{FloatIdentity(constant.Value.Y)}:{FloatIdentity(constant.Value.Z)}:" +
                    $"{FloatIdentity(constant.Value.W)}")),
            translation is null ? string.Empty : $"fpctrl:{translation.FragmentProgramControl:X8}",
            translation is null ? string.Empty : string.Join(',', translation.FragmentColorExports.Select(export =>
                $"o{export.ColorTarget}:{export.Register}:{export.WrittenComponentMask:X1}")));
        string cacheKey = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cacheMaterial)));
        var rendererBlockers = new List<string>();
        if (!authoredSourcePassAvailable || sourcePass is null)
            rendererBlockers.Add("authoredTechniquePass=missing");
        if (translation?.ProgramIrReady != true)
            rendererBlockers.Add("rsxProgramIr=notReady");
        if (vertexDecl is null)
            rendererBlockers.Add("vertexDeclaration=missing");
        if (!vertexInputPayloadReady)
        {
            rendererBlockers.Add(
                $"vertexInputPayload={(!string.IsNullOrWhiteSpace(vertexInputPayloadBlocker) ? vertexInputPayloadBlocker : "notMaterialized")}");
        }
        rendererBlockers.AddRange(UnsupportedRsxProgramRenderStateCapabilities(selectedPass.State));
        if (purpose == MapRenderShaderExecutionPurpose.DepthOnly &&
            (selectedPass.State.ColorMask != 0 ||
             !selectedPass.State.DepthTestEnabled ||
             !selectedPass.State.DepthWriteEnabled))
        {
            rendererBlockers.Add(
                "depthOnlyState=COLOR_MASK_OR_DEPTH_OWNERSHIP_MISMATCH");
        }

        if (translation is not null)
        {
            MapRenderShaderFragmentExport[] fragmentExports = translation
                .FragmentColorExports
                .Select(export => new MapRenderShaderFragmentExport(
                    export.ColorTarget,
                    export.Register,
                    export.WrittenComponentMask,
                    export.WrittenComponents))
                .ToArray();
            if (purpose == MapRenderShaderExecutionPurpose.CameraColor)
            {
                rendererBlockers.AddRange(
                    MapRenderEditorTranslatedProgramCapability.FindBlockers(
                        fragmentExports,
                        MapRenderEditorTranslatedProgramCapability
                            .CreateSurfaceAOutputAvailability()));
            }
            rendererBlockers.AddRange(
                MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                    .FindBlockers(
                        constantDestinations,
                        translation.CodePixelConstantPatchPlans));
            rendererBlockers.AddRange(
                MapRenderSelectedProgramVertexConstantOwnership.FindBlockers(
                    translation.ReadVertexConstantDestinations,
                    constantDestinations,
                    translation.EmbeddedVertexConstants));

            foreach (IGrouping<ushort, MapRenderMaterialSamplerBinding> destinationGroup in
                     materialSamplers.GroupBy(binding => binding.SamplerDest))
            {
                string[] resourceIdentities = destinationGroup
                    .Select(binding =>
                        $"{binding.WorldRuntimeTextureIdentity?.ToString() ?? "NO_WORLD_SLOT"}:" +
                        $"{binding.Texture?.BindingIdentity ?? "MISSING"}")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (resourceIdentities.Length > 1)
                    rendererBlockers.Add($"fragmentSamplerDest{destinationGroup.Key}=ambiguousResources");
            }

            foreach (int destination in translation.ReadVertexInputDestinations)
            {
                if (!vertexInputs.Any(input => input.Destination == destination))
                    rendererBlockers.Add($"vertexInputDest{destination}=routeMissing");
            }

            rendererBlockers.AddRange(FindFragmentSamplerResourceBlockers(
                translation.ReadFragmentSamplerDestinations,
                materialSamplers,
                runtimeSamplerRequirements));

            foreach (MapRenderShaderSamplerDestination constant in constantDestinations.Where(
                         constant => constant.ArgumentType.EndsWith("VertexConst", StringComparison.Ordinal)))
            {
                bool hasValue = constant.X.HasValue &&
                                constant.Y.HasValue &&
                                constant.Z.HasValue &&
                                constant.W.HasValue;
                bool hasSupportedDynamicMatrix = constant.CodeMatrixSemantic is { } semantic &&
                                                 MapRenderDerivedMatrixResolver.Supports(semantic);
                bool hasSupportedDirectCodeRow =
                    constant.CodeConstantSourceRow is { } sourceRow &&
                    MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                        .IsSupportedSourceRow(sourceRow);
                if (!hasValue &&
                    !hasSupportedDynamicMatrix &&
                    !hasSupportedDirectCodeRow)
                    rendererBlockers.Add($"vertexConstantDest{constant.Destination}=valueMissing");
            }
        }

        bool rendererProgramReady = rendererBlockers.Count == 0;

        return new MapRenderShaderExecutionContract(
            vertexProgram,
            pixelProgram,
            vertexDeclIdentity,
            vertexInputs,
            materialDestinations,
            customDestinations,
            codeDestinations,
            runtimeSamplerRequirements,
            translation?.ReadFragmentSamplerDestinations ?? [],
            translation?.ReadVertexConstantDestinations ?? [],
            constantDestinations,
            translation?.EmbeddedVertexConstants ?? [],
            translation?.CodePixelConstantPatchPlans ?? [],
            translation?.FragmentProgramControl ?? 0,
            translation?.FragmentExportPrecision ?? string.Empty,
            translation?.FragmentDepthExportEnabled ?? false,
            translation?.FragmentColorExports.Select(export => new MapRenderShaderFragmentExport(
                export.ColorTarget,
                export.Register,
                export.WrittenComponentMask,
                export.WrittenComponents)).ToArray() ?? [],
            cacheKey,
            translation?.ProgramIrReady ?? false,
            vertexInputPayloadReady,
            rendererProgramReady,
            rendererBlockers.Distinct(StringComparer.Ordinal).ToArray())
        {
            Purpose = purpose,
            VertexProgramIr = translation?.VertexProgramIr,
            FragmentProgramIr = translation?.FragmentProgramIr
        };
    }

    private static MapRenderShaderExecutionContract
        BuildGenericMaterialFallbackShaderContract(
            SelectedColorPass selectedPass,
            IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
            bool vertexInputPayloadReady,
            string vertexInputPayloadBlocker,
            MapRenderShaderExecutionPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(selectedPass);
        ArgumentNullException.ThrowIfNull(materialSamplers);

        RsxGenericMaterialFallbackPrograms programs =
            RsxGenericMaterialFallbackProgramFactory.Create();
        MapRenderMaterialSamplerBinding? selectedBinding = materialSamplers
            .FirstOrDefault(binding =>
                binding.Texture is not null &&
                binding.SamplerDest == selectedPass.Pass.SamplerDest) ??
            materialSamplers.FirstOrDefault(binding => binding.Texture is not null);
        string textureIdentity = selectedBinding?.TextureName ??
            selectedPass.Image.Name ??
            "generic-material-fallback-texture";
        int samplerArgument = selectedBinding?.SamplerArgIndex ??
            selectedPass.Pass.SamplerArgIndex;
        uint samplerArgumentRaw = selectedBinding?.SamplerHash ??
            selectedPass.Pass.SamplerHash;

        var materialDestination = new MapRenderShaderSamplerDestination(
            samplerArgument,
            nameof(MaterialShaderArgumentType.MaterialPixelSampler),
            0,
            samplerArgumentRaw,
            textureIdentity,
            IsOperationallyResolved: true,
            TextureTarget: "Texture2D");
        MapRenderShaderProgramIdentity vertexProgram =
            new(
                "Vertex",
                "generic.material-fallback.vertex.render-position-uv0-wvp.v1",
                checked((uint)programs.VertexProgram.InputByteCount),
                programs.VertexProgram.InputByteCount,
                programs.VertexProgram.InputSha256,
                HasProgramData: true);
        MapRenderShaderProgramIdentity pixelProgram =
            new(
                "Pixel",
                "generic.material-fallback.fragment.sample-texture2d.v1",
                checked((uint)programs.FragmentProgram.OriginalByteCount),
                programs.FragmentProgram.EffectiveByteCount,
                programs.FragmentProgram.OriginalSha256,
                HasProgramData: true);
        MapRenderShaderFragmentExport[] fragmentExports =
        [
            new MapRenderShaderFragmentExport(0, "0", 0x0f, "xyzw")
        ];
        string cacheMaterial = string.Join(
            '|',
            "generic-material-fallback/vulkan/1",
            programs.VertexProgram.Identity,
            programs.FragmentProgram.Identity,
            textureIdentity,
            selectedPass.State.AlphaTestEnabled
                ? $"alpha:{selectedPass.State.AlphaFunc:X8}:{selectedPass.State.AlphaRef}"
                : "alpha:disabled",
            selectedPass.State.ShaderPackerSrgbEnabled ? "srgb" : "linear",
            purpose.ToString());
        string cacheKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(cacheMaterial)));
        var blockers = new List<string>();
        if (!vertexInputPayloadReady)
        {
            blockers.Add(
                $"vertexInputPayload={(!string.IsNullOrWhiteSpace(vertexInputPayloadBlocker)
                    ? vertexInputPayloadBlocker
                    : "notMaterialized")}");
        }

        var translation = new RsxShaderTranslationResult(
            programs.VertexProgram,
            programs.FragmentProgram,
            ProgramIrReady: true,
            ReadVertexInputDestinations: [],
            ReadVertexConstantDestinations: [],
            EmbeddedVertexConstants: [],
            ReadFragmentSamplerDestinations: [0],
            FragmentProgramControl:
                programs.FragmentProgram.ProgramControl.EmittedControl,
            FragmentExportPrecision: "Fp16",
            FragmentDepthExportEnabled: false,
            FragmentColorExports: [
                new RsxFragmentColorExport(0, true, 0, 0x0f, "xyzw")
            ],
            StaticFragmentConstantPatches: [],
            CodePixelConstantPatchPlans: [],
            Blockers: []);

        bool rendererProgramReady = blockers.Count == 0;
        return new MapRenderShaderExecutionContract(
            vertexProgram,
            pixelProgram,
            "generic-material-fallback.vulkan.compact-rsx-inputs.v1",
            VertexInputs: [],
            MaterialSamplerDestinations: [materialDestination],
            CustomSamplerDestinations: [],
            CodeSamplerDestinations: [],
            RuntimeSamplerRequirements: [],
            ProgramSamplerDestinations: [0],
            ProgramVertexConstantDestinations: [],
            ConstantDestinations: [],
            EmbeddedVertexConstants: [],
            CodePixelConstantPatchPlans: [],
            FragmentProgramControl: translation.FragmentProgramControl,
            FragmentExportPrecision: translation.FragmentExportPrecision,
            FragmentDepthExportEnabled: false,
            FragmentColorExports: fragmentExports,
            ProgramCacheKey: cacheKey,
            ProgramIrReady: true,
            VertexInputPayloadReady: vertexInputPayloadReady,
            RendererProgramReady: rendererProgramReady,
            RendererBlockers: blockers)
        {
            Purpose = purpose,
            VertexProgramIr = translation.VertexProgramIr,
            FragmentProgramIr = translation.FragmentProgramIr
        };
    }

    private static MapRenderShaderVertexInputBinding[] ResolveSelectedVertexInputs(
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass,
        int? fixedVertexSourceBackendRow = null)
    {
        if (techset is null || selectedPass.Pass.TechniqueSlot < 0 || selectedPass.Pass.PassIndex < 0)
            return [];
        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate => candidate.Index == selectedPass.Pass.TechniqueSlot);
        if (slot?.Technique is not { } technique ||
            (uint)selectedPass.Pass.PassIndex >= (uint)technique.Passes.Count)
        {
            return [];
        }
        MaterialPassAsset sourcePass = technique.Passes[selectedPass.Pass.PassIndex];
        MapRenderSelectedPassProgramSources programSources = lookup.ResolveSources(
            techset,
            technique,
            new MapRenderSelectedTechniquePass(
                selectedPass.Pass.PassIndex,
                sourcePass));
        return CreateVertexInputBindings(
            techset,
            technique.Flags,
            programSources.VertexDeclaration,
            programSources.VertexProgram.HasProgramData
                ? RsxShaderTranslator.ReadVertexInputDestinations(
                    programSources.VertexProgram.Data.ToArray())
                : null,
            fixedVertexSourceBackendRow);
    }

    private static SelectedColorPass CreateStandardDepthPrepassSelection(
        SelectedColorPass colorPass,
        MapRenderEditorDepthPrepassPlan plan) => new(
            colorPass.Texture,
            colorPass.Image,
            new MapRenderMaterialPass(
                plan.MaterialName,
                plan.TechniqueSetName,
                plan.TechniqueSlot,
                plan.TechniqueName,
                MapRenderPassClassifier.NonColorWrite,
                plan.PassIndex,
                SamplerArgIndex: -1,
                SamplerDest: 0,
                SamplerHash: 0,
                TextureSemantic: 0,
                TexCoordSource: 0,
                CustomSamplerFlags: 0),
            plan.State,
            UnresolvedCodeSamplerCount: 0,
            TexCoordSource: 0,
            TexCoordSourceIsEngineRouted: false,
            AuthoredProgramExecutable: true);

    /// <summary>
    /// The translated world arena stores one vec4 per RSX destination. A
    /// camera-color program and its depth owner may share that slab only when
    /// a destination means the same source route to both programs. Extra
    /// depth-only destinations can be decoded into otherwise-unused rows.
    /// </summary>
    internal static bool TryMergeVertexInputBindings(
        IReadOnlyList<MapRenderShaderVertexInputBinding> colorBindings,
        IReadOnlyList<MapRenderShaderVertexInputBinding> depthBindings,
        out MapRenderShaderVertexInputBinding[] merged,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(colorBindings);
        ArgumentNullException.ThrowIfNull(depthBindings);

        var byDestination = new Dictionary<byte, MapRenderShaderVertexInputBinding>();
        var result = new List<MapRenderShaderVertexInputBinding>(
            colorBindings.Count + depthBindings.Count);
        foreach (MapRenderShaderVertexInputBinding binding in colorBindings)
        {
            if (byDestination.TryGetValue(
                    binding.Destination,
                    out MapRenderShaderVertexInputBinding? existing) &&
                !VertexInputRoutesMatch(existing, binding))
            {
                merged = [];
                blocker =
                    $"COLOR_VERTEX_INPUT_DEST{binding.Destination}_ROUTE_CONFLICT";
                return false;
            }
            if (existing is not null)
                continue;
            byDestination.Add(binding.Destination, binding);
            result.Add(binding);
        }

        foreach (MapRenderShaderVertexInputBinding binding in depthBindings)
        {
            if (byDestination.TryGetValue(
                    binding.Destination,
                    out MapRenderShaderVertexInputBinding? existing))
            {
                if (!VertexInputRoutesMatch(existing, binding))
                {
                    merged = colorBindings.ToArray();
                    blocker =
                        $"DEPTH_VERTEX_INPUT_DEST{binding.Destination}_ROUTE_CONFLICT";
                    return false;
                }
                continue;
            }

            byDestination.Add(binding.Destination, binding);
            result.Add(binding);
        }

        merged = result.ToArray();
        blocker = string.Empty;
        return true;
    }

    private static bool VertexInputRoutesMatch(
        MapRenderShaderVertexInputBinding first,
        MapRenderShaderVertexInputBinding next) =>
        first.Source == next.Source &&
        first.Destination == next.Destination &&
        first.StreamIndex == next.StreamIndex &&
        first.Stride == next.Stride &&
        first.Offset == next.Offset &&
        first.ComponentCount == next.ComponentCount &&
        first.RsxType == next.RsxType;

    private static MapRenderShaderProgramIdentity CreateShaderProgramIdentity(
        string stage,
        MapRenderShaderProgramResolution? resolution)
    {
        return new MapRenderShaderProgramIdentity(
            stage,
            resolution?.Name ?? string.Empty,
            resolution?.DeclaredDataSize ?? 0,
            resolution?.LoadedDataSize ?? 0,
            resolution?.DataSha256 ?? string.Empty,
            resolution?.HasProgramData ?? false);
    }

    internal static MaterialPassAsset CreateShaderTranslationPassSnapshot(
        MaterialPassAsset source,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments) => new()
        {
            Offset = source.Offset,
            VertexDeclPointer = source.VertexDeclPointer,
            VertexShaderPointer = source.VertexShaderPointer,
            PixelShaderPointer = source.PixelShaderPointer,
            PerPrimArgCount = source.PerPrimArgCount,
            PerObjArgCount = source.PerObjArgCount,
            StableArgCount = source.StableArgCount,
            CustomSamplerFlags = source.CustomSamplerFlags,
            PrecompiledIndex = source.PrecompiledIndex,
            ArgsPointer = source.ArgsPointer,
            VertexDeclaration = source.VertexDeclaration,
            VertexShader = source.VertexShader,
            PixelShader = source.PixelShader,
            Args = Array.AsReadOnly(arguments.ToArray())
        };

    private static string FloatIdentity(float? value) => value.HasValue
        ? unchecked((uint)BitConverter.SingleToInt32Bits(value.Value)).ToString("X8", CultureInfo.InvariantCulture)
        : "unset";

    private static MapRenderShaderVertexInputBinding[] CreateVertexInputBindings(
        MaterialTechniqueSetAsset? techset,
        ushort techniqueFlags,
        MaterialVertexDeclarationAsset? vertexDecl,
        IReadOnlyList<int>? requiredInputs,
        int? fixedVertexSourceBackendRow = null)
    {
        if (vertexDecl is null ||
            (techset is null && !fixedVertexSourceBackendRow.HasValue))
            return [];

        // Event20 initializes GfxCmdBufState+0x08 to row 4. Techniques with
        // flag 0x0008 replace it with
        // 5 + MaterialTechniqueSet+0x04 (worldVertexFormat) before emitting
        // the RSX vertex arrays.
        int effectiveVertexFormat = fixedVertexSourceBackendRow ??
            WorldVertexLayout.ResolveEffectiveBackendRow(
                techniqueFlags,
                techset!.WorldVertexFormat);
        var bindings = new List<MapRenderShaderVertexInputBinding>();
        int routeCount = Math.Min(vertexDecl.StreamCount, (byte)vertexDecl.Routing.Count);
        for (int routeIndex = 0; routeIndex < routeCount; routeIndex++)
        {
            MaterialVertexStreamRouting route = vertexDecl.Routing[routeIndex];
            if (!WorldVertexLayout.TryGetSource(
                    effectiveVertexFormat,
                    route.Source,
                    out WorldVertexSource source))
            {
                bindings.Add(new MapRenderShaderVertexInputBinding(
                    routeIndex, route.Source, route.Dest, 0, 0, 0, 0, 0,
                    "Unknown"));
                continue;
            }

            bool hasStride = WorldVertexLayout.TryGetStreamStride(
                effectiveVertexFormat,
                source.StreamIndex,
                out byte stride);
            bool disabledDefault = source.IsUnavailableSourceTuple;
            string typeName = RsxVertexTypeName(source.RsxType);
            bindings.Add(new MapRenderShaderVertexInputBinding(
                routeIndex,
                route.Source,
                route.Dest,
                source.StreamIndex,
                stride,
                source.ByteOffset,
                source.ComponentCount,
                source.RsxType,
                typeName));
        }
        if (requiredInputs is null)
            return bindings.ToArray();

        var requiredBindings = bindings
            .Where(binding => requiredInputs.Contains(binding.Destination))
            .ToList();
        foreach (int destination in requiredInputs.Where(destination => requiredBindings.All(binding => binding.Destination != destination)))
        {
            requiredBindings.Add(new MapRenderShaderVertexInputBinding(
                -1,
                0,
                checked((byte)destination),
                0,
                0,
                0,
                0,
                0,
                "Unknown"));
        }
        return requiredBindings.OrderBy(binding => binding.Destination).ToArray();
    }

    private static string RsxVertexTypeName(byte rsxType) => rsxType switch
    {
        0x00 => "B8G8R8A8_UNORM",
        0x01 => "V16_SNORM",
        0x02 => "V32_FLOAT",
        0x03 => "V16_FLOAT",
        0x04 => "U8_UNORM",
        0x05 => "V16_SSCALED",
        0x06 => "S11_11_10_NR",
        0x07 => "U8_USCALED",
        _ => "Unknown"
    };

    private static MapRenderShaderSamplerDestination CreateCodeSamplerDestination(
        int argumentIndex,
        MaterialShaderArgumentAsset argument)
    {
        uint raw = unchecked((uint)argument.ArgumentRaw);
        if (MapRenderCodePixelSamplerAbi.TryResolve(
                raw,
                out MapRenderCodePixelSamplerAbiEntry entry))
        {
            return new MapRenderShaderSamplerDestination(
                argumentIndex,
                nameof(MaterialShaderArgumentType.CodePixelSampler),
                argument.Dest,
                raw,
                entry.ResourceIdentity,
                IsOperationallyResolved: false,
                TextureTarget: entry.TextureTarget);
        }

        return new MapRenderShaderSamplerDestination(
            argumentIndex,
            nameof(MaterialShaderArgumentType.CodePixelSampler),
            argument.Dest,
            raw,
            $"raw{raw}",
            IsOperationallyResolved: false);
    }

    internal static MapRenderShaderRuntimeSamplerRequirement[]
        CreateRuntimeSamplerRequirements(
            IReadOnlyList<MapRenderShaderSamplerDestination> codeDestinations)
    {
        ArgumentNullException.ThrowIfNull(codeDestinations);
        return codeDestinations
            .Select(destination =>
                (Destination: destination,
                 Entry: MapRenderCodePixelSamplerAbi.TryResolve(
                     destination.Argument,
                     out MapRenderCodePixelSamplerAbiEntry entry)
                     ? entry
                     : null))
            .Where(item => item.Entry?.HasRuntimeRequirement == true)
            .Select(item =>
                new MapRenderShaderRuntimeSamplerRequirement(
                    item.Destination.ArgumentIndex,
                    item.Destination.Destination,
                    item.Destination.Argument,
                    item.Entry!.RuntimeResourceKind,
                    item.Entry.RuntimeRequirementStatus,
                    item.Entry.ResourceIdentity))
            .ToArray();
    }

    internal static string[] FindFragmentSamplerResourceBlockers(
        IReadOnlyList<int> programSamplerDestinations,
        IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
        IReadOnlyList<MapRenderShaderRuntimeSamplerRequirement>
            runtimeSamplerRequirements)
    {
        ArgumentNullException.ThrowIfNull(programSamplerDestinations);
        ArgumentNullException.ThrowIfNull(materialSamplers);
        ArgumentNullException.ThrowIfNull(runtimeSamplerRequirements);
        return programSamplerDestinations
            .Where(destination =>
                !materialSamplers.Any(binding =>
                    binding.SamplerDest == destination &&
                    binding.Texture is not null) &&
                !runtimeSamplerRequirements.Any(requirement =>
                    requirement.Destination == destination))
            .Select(destination =>
                $"fragmentSamplerDest{destination}=resourceMissing")
            .ToArray();
    }

    private static IEnumerable<MapRenderShaderSamplerDestination> CreateConstantDestinations(
        MaterialAsset? material,
        int argumentIndex,
        MaterialShaderArgumentAsset argument)
    {
        uint raw = unchecked((uint)argument.ArgumentRaw);
        if (argument.Type is MaterialShaderArgumentType.LiteralVertexConst or MaterialShaderArgumentType.LiteralPixelConst &&
            argument.LiteralConstant is { } literal)
        {
            string value = FormattableString.Invariant($"{literal.X:R},{literal.Y:R},{literal.Z:R},{literal.W:R}");
            yield return new MapRenderShaderSamplerDestination(
                argumentIndex,
                argument.Type.ToString(),
                argument.Dest,
                raw,
                value,
                IsOperationallyResolved: true,
                X: literal.X,
                Y: literal.Y,
                Z: literal.Z,
                W: literal.W);
            yield break;
        }

        if (argument.Type is MaterialShaderArgumentType.MaterialVertexConst or MaterialShaderArgumentType.MaterialPixelConst)
        {
            MaterialConstantDef? materialConstant = material?.Constants.FirstOrDefault(value => value.NameHash == raw);
            if (materialConstant is not null)
            {
                MaterialVec4 value = materialConstant.Literal;
                yield return new MapRenderShaderSamplerDestination(
                    argumentIndex,
                    argument.Type.ToString(),
                    argument.Dest,
                    raw,
                    FormattableString.Invariant($"{value.X:R},{value.Y:R},{value.Z:R},{value.W:R}"),
                    IsOperationallyResolved: true,
                    X: value.X,
                    Y: value.Y,
                    Z: value.Z,
                    W: value.W);
                yield break;
            }

            yield return new MapRenderShaderSamplerDestination(
                argumentIndex,
                argument.Type.ToString(),
                argument.Dest,
                raw,
                $"materialConstantHash0x{raw:X8}",
                IsOperationallyResolved: false);
            yield break;
        }

        ushort codeIndex = checked((ushort)(raw >> 16));
        byte firstRow = checked((byte)((raw >> 8) & 0xFF));
        byte rowCount = checked((byte)(raw & 0xFF));
        string argumentType = argument.Type == MaterialShaderArgumentType.CodePrimBegin
            ? "CodeVertexConst"
            : argument.Type.ToString();
        if (rowCount == 0)
        {
            yield return new MapRenderShaderSamplerDestination(
                argumentIndex,
                argumentType,
                argument.Dest,
                raw,
                $"codeConstantIndex{codeIndex}:firstRow{firstRow}:rows0",
                IsOperationallyResolved: false);
            yield break;
        }

        if (codeIndex >= MapRenderCodeConstantLayout.Float4Count)
        {
            bool isVertexCodeConstant = argument.Type == MaterialShaderArgumentType.CodeVertexConst;
            if (!isVertexCodeConstant ||
                !TryResolveCodeMatrixSemantic(codeIndex, out MapRenderCodeMatrixSemantic semantic, out MapRenderCodeMatrixTransform transform) ||
                firstRow + rowCount > 4)
            {
                yield return new MapRenderShaderSamplerDestination(
                    argumentIndex,
                    argumentType,
                    argument.Dest,
                    raw,
                    $"derivedCodeMatrixIndex{codeIndex}:firstRow{firstRow}:rows{rowCount}",
                    IsOperationallyResolved: false);
                yield break;
            }

            for (int row = 0; row < rowCount; row++)
            {
                int matrixRow = firstRow + row;
                yield return new MapRenderShaderSamplerDestination(
                    argumentIndex,
                    argumentType,
                    checked((ushort)(argument.Dest + row)),
                    raw,
                    $"{semantic}:{transform}:row{matrixRow}:codeIndex0x{codeIndex:X2}",
                    IsOperationallyResolved:
                        DerivedMatrixSemanticIsOperationallyResolved(
                            semantic),
                    CodeMatrixSemantic: semantic,
                    CodeMatrixTransform: transform,
                    CodeMatrixRow: matrixRow);
            }
            yield break;
        }

        for (int row = 0; row < rowCount; row++)
        {
            int sourceIndex = codeIndex + row;
            ushort destination = checked((ushort)(argument.Dest + row));
            yield return new MapRenderShaderSamplerDestination(
                argumentIndex,
                argumentType,
                destination,
                raw,
                $"codeConstantIndex{sourceIndex}",
                IsOperationallyResolved: false,
                CodeConstantSourceRow: checked((ushort)sourceIndex));
        }
    }

    private static bool TryResolveCodeMatrixSemantic(
        ushort codeIndex,
        out MapRenderCodeMatrixSemantic semantic,
        out MapRenderCodeMatrixTransform transform)
    {
        const int firstCodeMatrix = MapRenderCodeConstantLayout.Float4Count;
        int relativeIndex = codeIndex - firstCodeMatrix;
        int semanticIndex = relativeIndex >> 2;
        if (relativeIndex < 0 || semanticIndex >= 14)
        {
            semantic = default;
            transform = default;
            return false;
        }

        semantic = (MapRenderCodeMatrixSemantic)semanticIndex;
        transform = (MapRenderCodeMatrixTransform)(relativeIndex & 3);
        return true;
    }

    internal static bool DerivedMatrixSemanticIsOperationallyResolved(
        MapRenderCodeMatrixSemantic semantic) =>
        // Structural scene readiness and draw-time constant planning must use
        // one capability authority. ShadowLookup is intentionally populated
        // only after the same-revision atlas is ready, but it is still a
        // supported dynamic matrix source rather than an unresolved argument.
        MapRenderDerivedMatrixResolver.Supports(semantic);

}
