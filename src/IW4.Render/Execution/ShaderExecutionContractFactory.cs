using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Techniques;

namespace IW4.Render.Execution;

internal static class ShaderExecutionContractFactory
{
    internal static ShaderExecutionContract Create(
        MaterialAsset? material,
        MaterialTechniqueSetAsset? techset,
        IMaterialExecutionLookup lookup,
        ShaderExecutionPassSelection selectedPass,
        IReadOnlyList<MaterialSamplerBinding> materialSamplers,
        bool vertexInputPayloadReady,
        string vertexInputPayloadBlocker,
        bool authoredSourcePassAvailable,
        ShaderExecutionPurpose purpose =
            ShaderExecutionPurpose.CameraColor,
        ShaderTranslationCache? shaderTranslationCache = null,
        int? fixedVertexSourceBackendRow = null,
        IReadOnlySet<int>? explicitCubeSamplerDestinations = null,
        IReadOnlyList<ShaderVertexInputBinding>?
            explicitVertexInputs = null,
        IReadOnlyList<string>?
            scopedResourceIdentities = null)
    {
        if (scopedResourceIdentities is not null &&
            scopedResourceIdentities.Count != materialSamplers.Count)
        {
            throw new ArgumentException(
                "Scoped resource identities must remain aligned with material samplers.",
                nameof(scopedResourceIdentities));
        }
        MaterialPassAsset? sourcePass = null;
        MaterialTechniqueAsset? sourceTechnique = null;
        MaterialVertexDeclarationAsset? vertexDecl = null;
        IReadOnlyList<MaterialShaderArgumentAsset> args = [];
        SelectedPassProgramSources? programSources = null;

        if (techset is not null &&
            selectedPass.Pass.TechniquePass.TechniqueSlot >= 0 &&
            selectedPass.Pass.TechniquePass.PassIndex >= 0)
        {
            MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
                .FirstOrDefault(candidate => candidate.Index ==
                    selectedPass.Pass.TechniquePass.TechniqueSlot);
            if (slot?.Technique is { } technique &&
                (uint)selectedPass.Pass.TechniquePass.PassIndex <
                    (uint)technique.Passes.Count)
            {
                sourceTechnique = technique;
                sourcePass = technique.Passes[
                    selectedPass.Pass.TechniquePass.PassIndex];
                programSources = lookup.ResolveSources(
                    techset,
                    technique,
                    selectedPass.Pass.TechniquePass.PassIndex,
                    sourcePass);
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

        ShaderProgramIdentity vertexProgram = CreateShaderProgramIdentity(
            "Vertex",
            programSources?.VertexProgram);
        ShaderProgramIdentity pixelProgram = CreateShaderProgramIdentity(
            "Pixel",
            programSources?.PixelProgram);

        ShaderSamplerDestination[] materialDestinations = materialSamplers
            .Where(binding => binding.Identity.SamplerArgIndex >= 0)
                .Select(binding => new ShaderSamplerDestination(
                binding.Identity.SamplerArgIndex,
                nameof(MaterialShaderArgumentType.MaterialPixelSampler),
                binding.Identity.SamplerDest,
                binding.Identity.SamplerHash,
                binding.ShaderResourceIdentity,
                binding.IsOperationallyResolved))
            .ToArray();

        ShaderSamplerDestination[] customDestinations = materialSamplers
            .Where(binding => binding.Identity.SamplerArgIndex < 0)
            .Select(binding => new ShaderSamplerDestination(
                binding.Identity.SamplerArgIndex,
                "CustomPixelSampler",
                binding.Identity.SamplerDest,
                binding.Identity.SamplerHash,
                binding.TextureName,
                IsOperationallyResolved:
                    binding.IsOperationallyResolved,
                TextureTarget:
                    explicitCubeSamplerDestinations?.Contains(
                        binding.Identity.SamplerDest) == true ||
                    binding.Identity.SamplerDest == 1
                        ? "TextureCube"
                        : "Texture2D"))
            .ToArray();

        ShaderSamplerDestination[] codeDestinations = args
            .Select((arg, index) => (Arg: arg, Index: index))
            .Where(item => item.Arg.Type == MaterialShaderArgumentType.CodePixelSampler)
            .Select(item => CreateCodeSamplerDestination(item.Index, item.Arg))
            .ToArray();
        ShaderRuntimeSamplerRequirement[] runtimeSamplerRequirements =
            CreateRuntimeSamplerRequirements(codeDestinations);

        ShaderConstantDestination[] constantDestinations = args
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
        ShaderVertexInputBinding[] vertexInputs =
            explicitVertexInputs?.ToArray() ?? [];

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
                $"{FloatIdentity(binding.Value?.X)}:{FloatIdentity(binding.Value?.Y)}:{FloatIdentity(binding.Value?.Z)}:{FloatIdentity(binding.Value?.W)}")),
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
        if (translation is not null)
        {
            rendererBlockers.AddRange(translation.Blockers.Select(blocker =>
                $"rsxTranslation={blocker}"));
        }
        if (vertexDecl is null)
            rendererBlockers.Add("vertexDeclaration=missing");
        if (!vertexInputPayloadReady)
        {
            rendererBlockers.Add(
                $"vertexInputPayload={(!string.IsNullOrWhiteSpace(vertexInputPayloadBlocker) ? vertexInputPayloadBlocker : "notMaterialized")}");
        }
        rendererBlockers.AddRange(RenderStateExecutionCapability.FindBlockers(selectedPass.State));
        if (purpose == ShaderExecutionPurpose.DepthOnly &&
            (selectedPass.State.ColorMask != 0 ||
             !selectedPass.State.DepthTestEnabled ||
             !selectedPass.State.DepthWriteEnabled))
        {
            rendererBlockers.Add(
                "depthOnlyState=COLOR_MASK_OR_DEPTH_OWNERSHIP_MISMATCH");
        }

        if (translation is not null)
        {
            ShaderFragmentExport[] fragmentExports = translation
                .FragmentColorExports
                .Select(export => new ShaderFragmentExport(
                    export.ColorTarget,
                    export.Register,
                    export.WrittenComponentMask,
                    export.WrittenComponents))
                .ToArray();
            if (purpose == ShaderExecutionPurpose.CameraColor)
            {
                rendererBlockers.AddRange(
                TranslatedProgramCapability.FindBlockers(
                        fragmentExports,
                        TranslatedProgramCapability
                            .CreateSurfaceAOutputAvailability()));
            }
            rendererBlockers.AddRange(
                TranslatedProgramDirectCodeConstantRows
                    .FindBlockers(
                        constantDestinations,
                        translation.CodePixelConstantPatchPlans));
            rendererBlockers.AddRange(
                SelectedProgramVertexConstantOwnership.FindBlockers(
                    translation.ReadVertexConstantDestinations,
                    constantDestinations,
                    translation.EmbeddedVertexConstants));

            foreach (IGrouping<ushort, (MaterialSamplerBinding binding, int index)>
                     destinationGroup in materialSamplers
                         .Select((binding, index) => (binding, index))
                         .GroupBy(entry => entry.binding.Identity.SamplerDest))
            {
                string[] resourceIdentities = destinationGroup
                    .Select(entry =>
                        scopedResourceIdentities?[entry.index] ??
                        $"NO_WORLD_SLOT:{entry.binding.ResourceBindingIdentity}")
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

            foreach (ShaderConstantDestination constant in constantDestinations.Where(
                         constant => constant.ArgumentType.EndsWith("VertexConst", StringComparison.Ordinal)))
            {
                bool hasValue = constant.Value.HasValue;
                bool hasSupportedDynamicMatrix = constant.CodeMatrix is { } matrix &&
                                                 DerivedMatrixResolver.Supports(matrix.Semantic);
                bool hasSupportedDirectCodeRow =
                    constant.CodeConstantSourceRow is { } sourceRow &&
                    TranslatedProgramDirectCodeConstantRows
                        .IsSupportedSourceRow(sourceRow);
                if (!hasValue &&
                    !hasSupportedDynamicMatrix &&
                    !hasSupportedDirectCodeRow)
                    rendererBlockers.Add($"vertexConstantDest{constant.Destination}=valueMissing");
            }
        }

        bool rendererProgramReady = rendererBlockers.Count == 0;

        return new ShaderExecutionContract(
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
            translation?.FragmentColorExports.Select(export => new ShaderFragmentExport(
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

    private static ShaderExecutionContract
        BuildGenericMaterialFallbackShaderContract(
            ShaderExecutionPassSelection selectedPass,
            IReadOnlyList<MaterialSamplerBinding> materialSamplers,
            bool vertexInputPayloadReady,
            string vertexInputPayloadBlocker,
            ShaderExecutionPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(selectedPass);
        ArgumentNullException.ThrowIfNull(materialSamplers);

        RsxGenericMaterialFallbackPrograms programs =
            RsxGenericMaterialFallbackProgramFactory.Create();
        MaterialSamplerIdentity? primarySampler =
            selectedPass.PrimarySampler;
        MaterialSamplerBinding? selectedBinding = materialSamplers
            .FirstOrDefault(binding =>
                binding.Texture is not null &&
                primarySampler is { } primary &&
                binding.Identity.SamplerDest == primary.SamplerDest) ??
            materialSamplers.FirstOrDefault(binding => binding.Texture is not null);
        string textureIdentity = selectedBinding?.TextureName ??
            selectedPass.FallbackTextureName ??
            "generic-material-fallback-texture";
        int samplerArgument = selectedBinding?.Identity.SamplerArgIndex ??
            primarySampler?.SamplerArgIndex ?? -1;
        uint samplerArgumentRaw = selectedBinding?.Identity.SamplerHash ??
            primarySampler?.SamplerHash ?? 0;

        var materialDestination = new ShaderSamplerDestination(
            samplerArgument,
            nameof(MaterialShaderArgumentType.MaterialPixelSampler),
            0,
            samplerArgumentRaw,
            textureIdentity,
            IsOperationallyResolved: true,
            TextureTarget: "Texture2D");
        ShaderProgramIdentity vertexProgram =
            new(
                "Vertex",
                "generic.material-fallback.vertex.render-position-uv0-wvp.v1",
                checked((uint)programs.VertexProgram.InputByteCount),
                programs.VertexProgram.InputByteCount,
                programs.VertexProgram.InputSha256,
                HasProgramData: true);
        ShaderProgramIdentity pixelProgram =
            new(
                "Pixel",
                "generic.material-fallback.fragment.sample-texture2d.v1",
                checked((uint)programs.FragmentProgram.OriginalByteCount),
                programs.FragmentProgram.EffectiveByteCount,
                programs.FragmentProgram.OriginalSha256,
                HasProgramData: true);
        ShaderFragmentExport[] fragmentExports =
        [
            new ShaderFragmentExport(0, "0", 0x0f, "xyzw")
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
        return new ShaderExecutionContract(
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

    private static ShaderProgramIdentity CreateShaderProgramIdentity(
        string stage,
        ShaderProgramResolution? resolution)
    {
        return new ShaderProgramIdentity(
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

    private static ShaderSamplerDestination CreateCodeSamplerDestination(
        int argumentIndex,
        MaterialShaderArgumentAsset argument)
    {
        uint raw = unchecked((uint)argument.ArgumentRaw);
        if (CodePixelSamplerAbi.TryResolve(
                raw,
                out CodePixelSamplerAbiEntry entry))
        {
            return new ShaderSamplerDestination(
                argumentIndex,
                nameof(MaterialShaderArgumentType.CodePixelSampler),
                argument.Dest,
                raw,
                entry.ResourceIdentity,
                IsOperationallyResolved: false,
                TextureTarget: entry.TextureTarget);
        }

        return new ShaderSamplerDestination(
            argumentIndex,
            nameof(MaterialShaderArgumentType.CodePixelSampler),
            argument.Dest,
            raw,
            $"raw{raw}",
            IsOperationallyResolved: false);
    }

    internal static ShaderRuntimeSamplerRequirement[]
        CreateRuntimeSamplerRequirements(
            IReadOnlyList<ShaderSamplerDestination> codeDestinations)
    {
        ArgumentNullException.ThrowIfNull(codeDestinations);
        return codeDestinations
            .Select(destination =>
                (Destination: destination,
                 Entry: CodePixelSamplerAbi.TryResolve(
                     destination.Argument,
                     out CodePixelSamplerAbiEntry entry)
                     ? entry
                     : null))
            .Where(item => item.Entry?.HasRuntimeRequirement == true)
            .Select(item =>
                new ShaderRuntimeSamplerRequirement(
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
        IReadOnlyList<MaterialSamplerBinding> materialSamplers,
        IReadOnlyList<ShaderRuntimeSamplerRequirement>
            runtimeSamplerRequirements)
    {
        ArgumentNullException.ThrowIfNull(programSamplerDestinations);
        ArgumentNullException.ThrowIfNull(materialSamplers);
        ArgumentNullException.ThrowIfNull(runtimeSamplerRequirements);
        return programSamplerDestinations
            .Where(destination =>
                !materialSamplers.Any(binding =>
                    binding.Identity.SamplerDest == destination &&
                    binding.IsOperationallyResolved) &&
                !runtimeSamplerRequirements.Any(requirement =>
                    requirement.Destination == destination))
            .Select(destination =>
                $"fragmentSamplerDest{destination}=resourceMissing")
            .ToArray();
    }

    private static IEnumerable<ShaderConstantDestination> CreateConstantDestinations(
        MaterialAsset? material,
        int argumentIndex,
        MaterialShaderArgumentAsset argument)
    {
        uint raw = unchecked((uint)argument.ArgumentRaw);
        if (argument.Type is MaterialShaderArgumentType.LiteralVertexConst or MaterialShaderArgumentType.LiteralPixelConst &&
            argument.LiteralConstant is { } literal)
        {
            string value = FormattableString.Invariant($"{literal.X:R},{literal.Y:R},{literal.Z:R},{literal.W:R}");
            yield return new ShaderConstantDestination(
                argumentIndex,
                argument.Type.ToString(),
                argument.Dest,
                raw,
                value,
                IsOperationallyResolved: true,
                Value: new ShaderConstantValue(
                    literal.X,
                    literal.Y,
                    literal.Z,
                    literal.W));
            yield break;
        }

        if (argument.Type is MaterialShaderArgumentType.MaterialVertexConst or MaterialShaderArgumentType.MaterialPixelConst)
        {
            MaterialConstantDef? materialConstant = material?.Constants.FirstOrDefault(value => value.NameHash == raw);
            if (materialConstant is not null)
            {
                MaterialVec4 value = materialConstant.Literal;
                yield return new ShaderConstantDestination(
                    argumentIndex,
                    argument.Type.ToString(),
                    argument.Dest,
                    raw,
                    FormattableString.Invariant($"{value.X:R},{value.Y:R},{value.Z:R},{value.W:R}"),
                    IsOperationallyResolved: true,
                    Value: new ShaderConstantValue(
                        value.X,
                        value.Y,
                        value.Z,
                        value.W));
                yield break;
            }

            yield return new ShaderConstantDestination(
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
            yield return new ShaderConstantDestination(
                argumentIndex,
                argumentType,
                argument.Dest,
                raw,
                $"codeConstantIndex{codeIndex}:firstRow{firstRow}:rows0",
                IsOperationallyResolved: false);
            yield break;
        }

        if (codeIndex >= CodeConstantLayout.Float4Count)
        {
            bool isVertexCodeConstant = argument.Type == MaterialShaderArgumentType.CodeVertexConst;
            if (!isVertexCodeConstant ||
                !TryResolveCodeMatrixSemantic(codeIndex, out CodeMatrixSemantic semantic, out CodeMatrixTransform transform) ||
                firstRow + rowCount > 4)
            {
                yield return new ShaderConstantDestination(
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
                yield return new ShaderConstantDestination(
                    argumentIndex,
                    argumentType,
                    checked((ushort)(argument.Dest + row)),
                    raw,
                    $"{semantic}:{transform}:row{matrixRow}:codeIndex0x{codeIndex:X2}",
                    IsOperationallyResolved:
                        DerivedMatrixSemanticIsOperationallyResolved(
                            semantic),
                    CodeMatrix: new ShaderCodeMatrixBinding(
                        semantic,
                        transform,
                        matrixRow));
            }
            yield break;
        }

        for (int row = 0; row < rowCount; row++)
        {
            int sourceIndex = codeIndex + row;
            ushort destination = checked((ushort)(argument.Dest + row));
            yield return new ShaderConstantDestination(
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
        out CodeMatrixSemantic semantic,
        out CodeMatrixTransform transform)
    {
        const int firstCodeMatrix = CodeConstantLayout.Float4Count;
        int relativeIndex = codeIndex - firstCodeMatrix;
        int semanticIndex = relativeIndex >> 2;
        if (relativeIndex < 0 || semanticIndex >= 14)
        {
            semantic = default;
            transform = default;
            return false;
        }

        semantic = (CodeMatrixSemantic)semanticIndex;
        transform = (CodeMatrixTransform)(relativeIndex & 3);
        return true;
    }

    internal static bool DerivedMatrixSemanticIsOperationallyResolved(
        CodeMatrixSemantic semantic) =>
        // Structural scene readiness and draw-time constant planning must use
        // one capability authority. ShadowLookup is intentionally populated
        // only after the same-revision atlas is ready, but it is still a
        // supported dynamic matrix source rather than an unresolved argument.
        DerivedMatrixResolver.Supports(semantic);

}
