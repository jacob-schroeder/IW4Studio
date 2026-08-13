using System.Buffers.Binary;
using System.Collections.Immutable;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

internal static class RsxShaderTranslator
{
    internal const string CurrentSemanticTranslationVersion =
        "rsx-shader-translator/2";

    public static RsxShaderTranslationResult Translate(
        byte[] vertexData,
        byte[] pixelData,
        MaterialPassAsset pass,
        MaterialAsset? material,
        IReadOnlySet<int>? cubeSamplerDestinations = null,
        IReadOnlySet<int>? shadowSamplerDestinations = null,
        IReadOnlySet<int>? volumeSamplerDestinations = null)
    {
        ArgumentNullException.ThrowIfNull(vertexData);
        ArgumentNullException.ThrowIfNull(pixelData);
        ArgumentNullException.ThrowIfNull(pass);
        return TranslateWithSemanticCache(
            vertexData,
            pixelData,
            pass,
            material,
            RsxProgramSemanticCache.Shared,
            cubeSamplerDestinations,
            shadowSamplerDestinations,
            volumeSamplerDestinations);
    }

    internal static RsxShaderTranslationResult TranslateWithSemanticCache(
        byte[] vertexData,
        byte[] pixelData,
        MaterialPassAsset pass,
        MaterialAsset? material,
        RsxProgramSemanticCache programSemanticCache,
        IReadOnlySet<int>? cubeSamplerDestinations = null,
        IReadOnlySet<int>? shadowSamplerDestinations = null,
        IReadOnlySet<int>? volumeSamplerDestinations = null)
    {
        ArgumentNullException.ThrowIfNull(vertexData);
        ArgumentNullException.ThrowIfNull(pixelData);
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(programSemanticCache);
        RsxProgramSemanticSnapshot programSemantics =
            programSemanticCache.Resolve(vertexData, pixelData);
        return TranslateCore(
            programSemantics,
            pass,
            material,
            cubeSamplerDestinations?.ToHashSet() ?? [],
            shadowSamplerDestinations?.ToHashSet() ?? [],
            volumeSamplerDestinations?.ToHashSet() ?? []);
    }

    internal static RsxShaderTranslationResult Translate(
        RsxShaderTranslationRequestSnapshot request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureCurrentVersions();
        RsxProgramSemanticCache programSemanticCache =
            RsxProgramSemanticCache.Shared;
        return TranslateCore(
            request.ResolveProgramSemantics(programSemanticCache),
            request.CreateTranslationPass(),
            request.CreateTranslationMaterial(),
            request.CreateCubeSamplerDestinations(),
            request.CreateShadowSamplerDestinations(),
            request.CreateVolumeSamplerDestinations());
    }

    private static RsxShaderTranslationResult TranslateCore(
        RsxProgramSemanticSnapshot programSemantics,
        MaterialPassAsset pass,
        MaterialAsset? material,
        IReadOnlySet<int> cubeSamplerDestinations,
        IReadOnlySet<int> shadowSamplerDestinations,
        IReadOnlySet<int> volumeSamplerDestinations)
    {
        ArgumentNullException.ThrowIfNull(programSemantics);
        byte[] vertexData = programSemantics.CloneVertexProgramData();
        byte[] pixelData = programSemantics.CloneFragmentProgramData();
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        IReadOnlySet<int> cubeSamplers = cubeSamplerDestinations;
        IReadOnlySet<int> shadowSamplers = shadowSamplerDestinations;
        IReadOnlySet<int> volumeSamplers = volumeSamplerDestinations;
        foreach (int destination in cubeSamplers.Where(shadowSamplers.Contains))
        {
            blockers.Add(
                $"fragmentSamplerDest{destination}=unsupportedAmbiguousCubeAndShadowShape");
        }
        foreach (int destination in volumeSamplers.Where(destination =>
                     cubeSamplers.Contains(destination) ||
                     shadowSamplers.Contains(destination)))
        {
            blockers.Add(
                $"fragmentSamplerDest{destination}=unsupportedAmbiguousVolumeShape");
        }
        byte[] patchedPixelData = BuildStaticPatchedFragmentProgram(
            pixelData,
            programSemantics.FragmentProgram,
            pass,
            material,
            blockers,
            out StaticFragmentConstantPatch[] staticFragmentPatches,
            out FragmentCodePixelConstantPatchCandidate[]
                codePixelPatchCandidates);
        RsxVertexProgramIr vertexProgramIr = programSemantics.VertexProgram
            .ProgramIr ?? throw new InvalidOperationException(
                "Translation requires a captured vertex-program data cell.");
        int vertexOffset = vertexProgramIr.UploadOffset;
        RsxFragmentProgramSemanticSnapshot decodedFragmentProgram =
            programSemantics.FragmentProgram;
        int pixelOffset = decodedFragmentProgram.UploadOffset;
        if (vertexOffset < 0)
            blockers.Add("vertexUploadHeader=invalid");
        if (pixelOffset < 0)
            blockers.Add("pixelUploadHeader=invalid");

        IReadOnlyList<RsxVertexInstruction> vertexInstructions =
            vertexProgramIr.Instructions;
        RsxVertexProgramEmbeddedConstantDecodeResult embeddedVertexConstants =
            RsxVertexProgramEmbeddedConstantDecoder.Decode(vertexData);
        blockers.UnionWith(embeddedVertexConstants.Blockers);
        ImmutableArray<RsxFragmentInstruction> translatedPixelInstructions =
            decodedFragmentProgram.SpecializeInlineConstants(
                patchedPixelData);
        CodePixelConstantPatchPlan[] codePixelPatchPlans;
        if (codePixelPatchCandidates.Length == 0)
        {
            codePixelPatchPlans = [];
        }
        else
        {
            List<RsxFragmentInstruction> specializedInstructions =
                translatedPixelInstructions.ToList();
            codePixelPatchPlans = ResolveCodePixelConstantPatches(
                specializedInstructions,
                codePixelPatchCandidates,
                blockers);
            translatedPixelInstructions =
                specializedInstructions.ToImmutableArray();
        }
        RsxFragmentProgramControl fragmentProgramControl =
            ReadFragmentProgramControl(patchedPixelData);
        bool hasFragmentProgramControl = fragmentProgramControl.IsValid;
        if (!hasFragmentProgramControl)
            blockers.Add("fragmentProgramControl=invalid");
        ImmutableArray<RsxFragmentColorExport> fragmentColorExports =
            hasFragmentProgramControl
                ? ReadFragmentColorExports(
                    translatedPixelInstructions,
                    fragmentProgramControl.EmittedControl)
                : ImmutableArray<RsxFragmentColorExport>.Empty;
        int pixelUploadSize = pixelOffset < 0
            ? 0
            : checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                patchedPixelData.AsSpan(0x18, 4)));
        var samplerFeatureProfile =
            new RsxFragmentSamplerFeatureProfile(
                cubeSamplers,
                shadowSamplers,
                volumeSamplers);
        var fragmentProgramIr = new RsxFragmentProgramIr(
            pixelData,
            patchedPixelData,
            RsxFragmentProgramIr.CurrentDecoderVersion,
            RsxFragmentProgramIr.CurrentSemanticTranslationVersion,
            pixelOffset,
            pixelUploadSize,
            translatedPixelInstructions,
            staticFragmentPatches,
            codePixelPatchPlans,
            fragmentProgramControl,
            samplerFeatureProfile,
            fragmentColorExports);
        IReadOnlyList<RsxFragmentInstruction> pixelInstructions =
            fragmentProgramIr.Instructions;
        if (vertexInstructions.Count == 0)
            blockers.Add("vertexInstructions=missing");
        if (pixelInstructions.Count == 0)
            blockers.Add("pixelInstructions=missing");

        // This layer owns exact decode/specialization only. Whether either
        // backend can lower a decoded operation belongs to that backend and
        // must never be inferred by generating an API-specific shader here.
        bool programIrReady = vertexProgramIr.HasValidUpload &&
                              fragmentProgramIr.HasValidUpload &&
                              hasFragmentProgramControl &&
                              vertexInstructions.Count != 0 &&
                              pixelInstructions.Count != 0 &&
                              !blockers.Any(IsProgramIrBlocker);
        AddUnwrittenVertexOutputBlockers(vertexInstructions, pixelInstructions, blockers);
        return new RsxShaderTranslationResult(
            vertexProgramIr,
            fragmentProgramIr,
            programIrReady,
            ReadVertexInputDestinations(vertexInstructions),
            ReadVertexConstantDestinations(vertexInstructions),
            embeddedVertexConstants.Constants,
            ReadFragmentSamplerDestinations(fragmentProgramIr),
            fragmentProgramControl.EmittedControl,
            fragmentProgramControl.ExportPrecision,
            fragmentProgramControl.DepthExportEnabled,
            fragmentColorExports,
            staticFragmentPatches,
            codePixelPatchPlans,
            blockers.ToArray());
    }

    private static bool IsProgramIrBlocker(string blocker) =>
        blocker.Contains("invalid", StringComparison.Ordinal) ||
        blocker.Contains("missing", StringComparison.Ordinal) ||
        blocker.Contains("unsupported", StringComparison.Ordinal) ||
        blocker.Contains("unmapped", StringComparison.Ordinal);

    private static RsxFragmentProgramControl ReadFragmentProgramControl(
        byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 0x18)
            return default;

        uint descriptorOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x14, 4));
        if (descriptorOffset > int.MaxValue ||
            (ulong)descriptorOffset + 0x16UL > (ulong)data.Length)
        {
            return new RsxFragmentProgramControl(
                IsValid: false,
                descriptorOffset,
                RegisterCount: 0,
                ExportPrecisionRaw: 0,
                DepthExportRaw: 0,
                ControlFlagsRaw: 0,
                EmittedControl: 0);
        }

        int descriptor = (int)descriptorOffset;
        byte registerCount = data[descriptor + 0x12];
        byte exportPrecision = data[descriptor + 0x13];
        byte depthExport = data[descriptor + 0x14];
        byte controlFlags = data[descriptor + 0x15];

        // These four descriptor bytes form NV4097_SET_SHADER_CONTROL (0x1D60).
        // Only the documented RSX export bits are consumed by the translator,
        // but the execution contract retains the exact emitted value.
        uint control = 0;
        if (registerCount > 1)
            control |= (uint)registerCount << 24;
        if (exportPrecision == 0)
            control |= 0x40;
        control |= 0x8000;
        if (controlFlags != 0)
            control |= 0x80;
        if (depthExport > 0)
            control |= 0x0e;
        control |= 0x400;
        return new RsxFragmentProgramControl(
            IsValid: true,
            descriptorOffset,
            registerCount,
            exportPrecision,
            depthExport,
            controlFlags,
            control);
    }

    private static ImmutableArray<RsxFragmentColorExport>
        ReadFragmentColorExports(
        IReadOnlyList<RsxFragmentInstruction> instructions,
        uint fragmentProgramControl)
    {
        bool fp32 = (fragmentProgramControl & 0x40) != 0;
        (bool Fp16, int Register)[] registers = fp32
            ? [(false, 0), (false, 2), (false, 3), (false, 4)]
            : [(true, 0), (true, 4), (true, 6), (true, 8)];

        return registers
            .Select((register, colorTarget) =>
            {
                int writtenMask = instructions
                    .Where(instruction => !instruction.Branch &&
                                          !instruction.NoDest &&
                                          instruction.DestFp16 == register.Fp16 &&
                                          instruction.DestRegister == register.Register)
                    .Aggregate(0, (mask, instruction) => mask | instruction.WriteMask);
                return new RsxFragmentColorExport(
                    colorTarget,
                    register.Fp16,
                    register.Register,
                    (byte)writtenMask,
                    FragmentComponentMask(writtenMask));
            })
            .ToImmutableArray();
    }

    private static string FragmentComponentMask(int mask)
    {
        Span<char> components = stackalloc char[4];
        int count = 0;
        if ((mask & 1) != 0) components[count++] = 'x';
        if ((mask & 2) != 0) components[count++] = 'y';
        if ((mask & 4) != 0) components[count++] = 'z';
        if ((mask & 8) != 0) components[count++] = 'w';
        return new string(components[..count]);
    }

    public static IReadOnlyList<int> ReadVertexInputDestinations(byte[] vertexData)
    {
        ArgumentNullException.ThrowIfNull(vertexData);
        RsxVertexProgramIr vertexProgram =
            RsxProgramSemanticCache.Shared.ResolveVertex(vertexData)
                .ProgramIr ?? throw new InvalidOperationException(
                    "A non-null vertex-program data cell produced no semantic IR.");
        return ReadVertexInputDestinations(vertexProgram);
    }

    internal static IReadOnlyList<int> ReadVertexInputDestinations(
        RsxVertexProgramIr vertexProgram)
    {
        ArgumentNullException.ThrowIfNull(vertexProgram);
        return ReadVertexInputDestinations(vertexProgram.Instructions);
    }

    private static IReadOnlyList<int> ReadVertexInputDestinations(IReadOnlyList<RsxVertexInstruction> instructions)
    {
        var inputs = new SortedSet<int>();
        foreach (RsxVertexInstruction instruction in instructions)
        {
            int vectorSources = RsxVertexInstruction.VectorSourceMask(
                instruction.VecOpcode);
            if ((vectorSources & 1) != 0 && RsxVertexInstruction.SourceRegisterType(instruction.Source0) == 2) inputs.Add(instruction.InputSource);
            if ((vectorSources & 2) != 0 && RsxVertexInstruction.SourceRegisterType(instruction.Source1) == 2) inputs.Add(instruction.InputSource);
            if ((vectorSources & 4) != 0 && RsxVertexInstruction.SourceRegisterType(instruction.Source2) == 2) inputs.Add(instruction.InputSource);
            if (RsxVertexInstruction.ScalarReadsSource2(
                    instruction.ScaOpcode) &&
                RsxVertexInstruction.SourceRegisterType(
                    instruction.Source2) == 2)
                inputs.Add(instruction.InputSource);
        }
        return inputs.ToArray();
    }

    private static IReadOnlyList<int> ReadVertexConstantDestinations(
        IReadOnlyList<RsxVertexInstruction> instructions)
    {
        var constants = new SortedSet<int>();
        foreach (RsxVertexInstruction instruction in instructions)
        {
            int vectorSources = RsxVertexInstruction.VectorSourceMask(
                instruction.VecOpcode);
            bool readsConstant =
                ((vectorSources & 1) != 0 && RsxVertexInstruction.SourceRegisterType(instruction.Source0) == 3) ||
                ((vectorSources & 2) != 0 && RsxVertexInstruction.SourceRegisterType(instruction.Source1) == 3) ||
                ((vectorSources & 4) != 0 && RsxVertexInstruction.SourceRegisterType(instruction.Source2) == 3) ||
                (RsxVertexInstruction.ScalarReadsSource2(
                     instruction.ScaOpcode) &&
                 RsxVertexInstruction.SourceRegisterType(instruction.Source2) == 3);
            if (readsConstant)
                constants.Add(instruction.ConstSource);
        }
        return constants.ToArray();
    }

    private static IReadOnlyList<int> ReadFragmentSamplerDestinations(
        RsxFragmentProgramIr fragmentProgram)
    {
        ArgumentNullException.ThrowIfNull(fragmentProgram);
        return fragmentProgram.SamplerUses
            .Select(use => use.Destination)
            .Distinct()
            .Order()
            .ToArray();
    }

    private static void AddUnwrittenVertexOutputBlockers(
        IReadOnlyList<RsxVertexInstruction> vertexInstructions,
        IReadOnlyList<RsxFragmentInstruction> fragmentInstructions,
        ISet<string> blockers)
    {
        var writtenOutputs = new HashSet<int>();
        foreach (RsxVertexInstruction instruction in vertexInstructions)
        {
            if (instruction.VecOpcode != 0 && instruction.VecWriteMask != 0 &&
                instruction.VecResult && instruction.ResultIndex != 0x1f)
            {
                writtenOutputs.Add(instruction.ResultIndex);
            }
            if (instruction.ScaOpcode != 0 && instruction.ScaWriteMask != 0 &&
                instruction.ScaResult && instruction.ResultIndex != 0x1f)
            {
                writtenOutputs.Add(instruction.ResultIndex);
            }
        }

        foreach (RsxFragmentInstruction instruction in fragmentInstructions.Where(instruction => !instruction.Branch))
        {
            int operandCount = RsxProgramDecoder.FragmentOperandCount(
                instruction.Opcode);
            bool readsInput =
                (operandCount > 0 && RsxFragmentInstruction.SourceRegisterType(instruction.Src0) == 1) ||
                (operandCount > 1 && RsxFragmentInstruction.SourceRegisterType(instruction.Src1) == 1) ||
                (operandCount > 2 && RsxFragmentInstruction.SourceRegisterType(instruction.Src2) == 1);
            if (!readsInput || FragmentInputOutputIndex(instruction.SourceAttribute) is not { } outputIndex ||
                writtenOutputs.Contains(outputIndex))
            {
                continue;
            }

            blockers.Add(
                $"vertexOutput{outputIndex}=DEFAULT_0_0_0_1_NO_SELECTED_PROGRAM_WRITER");
        }
    }

    private static int? FragmentInputOutputIndex(int input) => input switch
    {
        1 => 1,
        2 => 2,
        3 => 5,
        >= 4 and <= 11 => input + 3,
        _ => null
    };

    private static byte[] BuildStaticPatchedFragmentProgram(
        byte[] data,
        RsxFragmentProgramSemanticSnapshot decodedProgram,
        MaterialPassAsset pass,
        MaterialAsset? material,
        ISet<string> blockers,
        out StaticFragmentConstantPatch[] staticFragmentPatches,
        out FragmentCodePixelConstantPatchCandidate[]
            codePixelPatchCandidates)
    {
        ArgumentNullException.ThrowIfNull(decodedProgram);
        var appliedPatches = new List<StaticFragmentConstantPatch>();
        var codePixelCandidates = new List<
            FragmentCodePixelConstantPatchCandidate>();
        byte[] patched = data.ToArray();
        if (!TryReadRuntimeInfo(data, out RuntimeInfo runtimeInfo))
        {
            blockers.Add("fragmentRuntimePatchTable=invalid");
            staticFragmentPatches = [];
            codePixelPatchCandidates = [];
            return patched;
        }

        int parameterCount = checked((int)runtimeInfo.ParameterCount);
        for (int destination = 0;
             destination < parameterCount;
             destination++)
        {
            ushort dest = checked((ushort)destination);
            if (!TryReadPatchEntry(
                    data,
                    runtimeInfo,
                    dest,
                    out PatchEntry entry))
            {
                blockers.Add($"fragmentRuntimePatchDest{dest}=invalidEntry");
                continue;
            }
            if (entry.DefaultConstantOffset == 0)
                continue;
            if ((ulong)entry.DefaultConstantOffset + 0x10UL >
                (ulong)data.Length)
            {
                blockers.Add(
                    $"fragmentRuntimePatchDest{dest}=invalidDefaultConstant");
                continue;
            }
            if (!ValidateInlineConstantPatchTargets(
                    decodedProgram,
                    runtimeInfo,
                    entry,
                    dest,
                    blockers))
            {
                continue;
            }

            foreach (ushort patchOffset in entry.PatchOffsets)
            {
                int target = checked((int)runtimeInfo.UploadOffset + patchOffset);
                data.AsSpan((int)entry.DefaultConstantOffset, 16).CopyTo(patched.AsSpan(target, 16));
                for (int wordOffset = 0; wordOffset < 16; wordOffset += 4)
                {
                    uint value = BinaryPrimitives.ReadUInt32BigEndian(patched.AsSpan(target + wordOffset, 4));
                    BinaryPrimitives.WriteUInt32BigEndian(
                        patched.AsSpan(target + wordOffset, 4),
                        RsxProgramDecoder.FragmentWord(value));
                }
            }
        }

        Dictionary<uint, MaterialConstantDef> constants = material?.Constants
            .GroupBy(value => value.NameHash)
            .ToDictionary(group => group.Key, group => group.First()) ?? [];
        int stableStart = pass.PerPrimArgCount + pass.PerObjArgCount;
        for (int ordinal = 0;
             ordinal < Math.Min(stableStart, pass.Args.Count);
             ordinal++)
        {
            MaterialShaderArgumentAsset argument = pass.Args[ordinal];
            if (argument.Type != MaterialShaderArgumentType.CodePixelConst)
                continue;

            ushort codeIndex = checked((ushort)(
                unchecked((uint)argument.ArgumentRaw) >> 16));
            codePixelCandidates.Add(new(
                ordinal,
                argument.Dest,
                argument.ArgumentRaw,
                codeIndex,
                CodePixelConstantPatchStatus.NonStableScopeDeferred,
                [],
                checked((int)runtimeInfo.UploadOffset),
                "PS3 0x003A7738 does not consume per-primitive or per-object arguments."));
        }
        for (int ordinal = stableStart; ordinal < pass.Args.Count; ordinal++)
        {
            MaterialShaderArgumentAsset argument = pass.Args[ordinal];
            if (argument.Type is not (MaterialShaderArgumentType.CodePixelConst or
                                      MaterialShaderArgumentType.MaterialPixelConst or
                                      MaterialShaderArgumentType.LiteralPixelConst))
            {
                continue;
            }

            uint raw = unchecked((uint)argument.ArgumentRaw);
            if (argument.Type == MaterialShaderArgumentType.CodePixelConst)
            {
                // The stable argument slice stores its direct-table index as
                // a big-endian u16 at argument+0x04. Retain the exact
                // destination/value pair; the two union-tail bytes are not a
                // fragment row shape.
                ushort codeIndex = checked((ushort)(raw >> 16));
                if (codeIndex >= CodeConstantLayout.Float4Count)
                {
                    codePixelCandidates.Add(new(
                        ordinal,
                        argument.Dest,
                        argument.ArgumentRaw,
                        codeIndex,
                        CodePixelConstantPatchStatus
                            .DerivedSourceDeferred,
                        [],
                        checked((int)runtimeInfo.UploadOffset),
                        "CodePixel source index is outside the supported direct table."));
                    continue;
                }
                if (!TryReadPatchEntry(
                        data,
                        runtimeInfo,
                        argument.Dest,
                        out PatchEntry codeEntry))
                {
                    codePixelCandidates.Add(new(
                        ordinal,
                        argument.Dest,
                        argument.ArgumentRaw,
                        codeIndex,
                        CodePixelConstantPatchStatus
                            .DestinationUnmapped,
                        [],
                        checked((int)runtimeInfo.UploadOffset),
                        "Fragment runtime table has no valid destination entry."));
                    continue;
                }
                if (codeEntry.PatchOffsets.Count == 0)
                {
                    codePixelCandidates.Add(new(
                        ordinal,
                        argument.Dest,
                        argument.ArgumentRaw,
                        codeIndex,
                        CodePixelConstantPatchStatus
                            .DefaultOnlyPatchEntry,
                        [],
                        checked((int)runtimeInfo.UploadOffset),
                        "Fragment destination has no runtime patch sites."));
                    continue;
                }

                codePixelCandidates.Add(new(
                    ordinal,
                    argument.Dest,
                    argument.ArgumentRaw,
                    codeIndex,
                    DeferredStatus: null,
                    codeEntry.PatchOffsets.ToArray(),
                    checked((int)runtimeInfo.UploadOffset),
                    Detail: null));
                continue;
            }

            if (!TryReadPatchEntry(
                    data,
                    runtimeInfo,
                    argument.Dest,
                    out PatchEntry entry))
            {
                blockers.Add($"fragmentPatchDest{argument.Dest}=unmapped");
                continue;
            }
            if (!ValidateInlineConstantPatchTargets(
                    decodedProgram,
                    runtimeInfo,
                    entry,
                    argument.Dest,
                    blockers))
            {
                continue;
            }

            switch (argument.Type)
            {
                case MaterialShaderArgumentType.MaterialPixelConst when constants.TryGetValue(raw, out MaterialConstantDef? constant):
                    ApplyFragmentConstant(patched, runtimeInfo, entry, constant.Literal.X, constant.Literal.Y, constant.Literal.Z, constant.Literal.W);
                    appliedPatches.Add(new StaticFragmentConstantPatch(
                        ordinal,
                        SelectedPassConstantKind.MaterialPixel,
                        argument.Dest,
                        argument.ArgumentRaw,
                        new ShaderConstantValue(
                            constant.Literal.X,
                            constant.Literal.Y,
                            constant.Literal.Z,
                            constant.Literal.W),
                        entry.PatchOffsets.Count));
                    break;
                case MaterialShaderArgumentType.LiteralPixelConst when argument.LiteralConstant is { } literal:
                    ApplyFragmentConstant(patched, runtimeInfo, entry, literal.X, literal.Y, literal.Z, literal.W);
                    appliedPatches.Add(new StaticFragmentConstantPatch(
                        ordinal,
                        SelectedPassConstantKind.LiteralPixel,
                        argument.Dest,
                        argument.ArgumentRaw,
                        new ShaderConstantValue(
                            literal.X,
                            literal.Y,
                            literal.Z,
                            literal.W),
                        entry.PatchOffsets.Count));
                    break;
                default:
                    blockers.Add($"fragmentStaticConstantDest{argument.Dest}=valueMissing");
                    break;
            }
        }

        staticFragmentPatches = appliedPatches.ToArray();
        codePixelPatchCandidates = codePixelCandidates.ToArray();
        return patched;
    }

    private static bool ValidateInlineConstantPatchTargets(
        RsxFragmentProgramSemanticSnapshot decodedProgram,
        RuntimeInfo runtimeInfo,
        PatchEntry entry,
        ushort destination,
        ISet<string> blockers)
    {
        bool valid = true;
        foreach (ushort relativeOffset in entry.PatchOffsets)
        {
            int programOffset = checked(
                (int)runtimeInfo.UploadOffset + relativeOffset);
            if (decodedProgram.IsExactInlineConstantPayloadOffset(
                    programOffset))
            {
                continue;
            }

            blockers.Add(
                $"fragmentRuntimePatchDest{destination}" +
                $"Offset0x{relativeOffset:X4}=invalidInlineConstantTarget");
            valid = false;
        }

        return valid;
    }

    private static CodePixelConstantPatchPlan[]
        ResolveCodePixelConstantPatches(
            IList<RsxFragmentInstruction> instructions,
            IReadOnlyList<FragmentCodePixelConstantPatchCandidate> candidates,
            ISet<string> blockers)
    {
        var instructionsByPayloadOffset = instructions
            .Where(instruction =>
                instruction.ByteCount == 0x20 && instruction.Constant.HasValue)
            .ToDictionary(instruction => instruction.Offset + 0x10);
        FragmentCodePixelConstantPatchCandidate[] pending = candidates
            .Where(candidate => !candidate.DeferredStatus.HasValue)
            .ToArray();
        var ambiguousOrdinals = new HashSet<int>();
        foreach (IGrouping<ushort, FragmentCodePixelConstantPatchCandidate>
                 duplicateDestination in candidates
                     .GroupBy(candidate => candidate.Destination)
                     .Where(group => group.Count() > 1))
        {
            foreach (FragmentCodePixelConstantPatchCandidate candidate in
                     duplicateDestination.Where(candidate =>
                         !candidate.DeferredStatus.HasValue))
            {
                ambiguousOrdinals.Add(candidate.ArgumentOrdinal);
            }
        }
        foreach (FragmentCodePixelConstantPatchCandidate candidate in pending)
        {
            if (candidate.RelativePatchOffsets.Count !=
                candidate.RelativePatchOffsets.Distinct().Count())
            {
                ambiguousOrdinals.Add(candidate.ArgumentOrdinal);
            }

            foreach (FragmentCodePixelConstantPatchCandidate other in pending)
            {
                if (other.ArgumentOrdinal <= candidate.ArgumentOrdinal)
                    continue;
                bool sharesDestination =
                    other.Destination == candidate.Destination;
                bool sharesPatchSite = candidate.RelativePatchOffsets
                    .Select(offset => candidate.UploadOffset + offset)
                    .Intersect(other.RelativePatchOffsets.Select(offset =>
                        other.UploadOffset + offset))
                    .Any();
                if (!sharesDestination && !sharesPatchSite)
                    continue;

                ambiguousOrdinals.Add(candidate.ArgumentOrdinal);
                ambiguousOrdinals.Add(other.ArgumentOrdinal);
            }
        }

        var plans = new List<CodePixelConstantPatchPlan>(
            candidates.Count);
        foreach (FragmentCodePixelConstantPatchCandidate candidate in
                 candidates.OrderBy(candidate => candidate.ArgumentOrdinal))
        {
            CodePixelConstantPatchSite[] sites = candidate
                .RelativePatchOffsets
                .Distinct()
                .Select(relativeOffset =>
                {
                    int programOffset = checked(
                        candidate.UploadOffset + relativeOffset);
                    return new CodePixelConstantPatchSite(
                        relativeOffset,
                        programOffset,
                        instructionsByPayloadOffset.TryGetValue(
                            programOffset,
                            out RsxFragmentInstruction instruction)
                            ? instruction.Index
                            : null);
                })
                .ToArray();
            CodePixelConstantPatchStatus status =
                candidate.DeferredStatus ??
                (ambiguousOrdinals.Contains(candidate.ArgumentOrdinal)
                    ? CodePixelConstantPatchStatus.PatchSiteAmbiguous
                    : sites.Any(site => !site.InstructionIndex.HasValue)
                        ? CodePixelConstantPatchStatus
                            .PatchSiteUnmatched
                        : CodePixelConstantPatchStatus
                            .DirectSourceResolved);
            string? detail = candidate.Detail ?? status switch
            {
                CodePixelConstantPatchStatus.PatchSiteAmbiguous =>
                    "Destination or fragment patch site has multiple authored CodePixel owners.",
                CodePixelConstantPatchStatus.PatchSiteUnmatched =>
                    "A fragment patch offset is not an exact decoded inline-constant payload.",
                _ => null
            };
            var plan = new CodePixelConstantPatchPlan(
                candidate.ArgumentOrdinal,
                candidate.Destination,
                candidate.ArgumentRaw,
                candidate.CodeIndex,
                status,
                sites,
                detail);
            plans.Add(plan);

            if (!plan.IsDirectSourceResolved)
            {
                blockers.Add(
                    $"fragmentCodeConstantArg{plan.ArgumentOrdinal}Dest{plan.Destination}=unlowered:{plan.Status}");
                continue;
            }

            foreach (CodePixelConstantPatchSite site in
                     plan.PatchSites)
            {
                int instructionIndex = site.InstructionIndex!.Value;
                RsxFragmentInstruction instruction = instructions[instructionIndex];
                instructions[instructionIndex] = instruction with
                {
                    DirectCodeConstantIndex = candidate.CodeIndex
                };
            }
        }

        return plans.ToArray();
    }

    private static void ApplyFragmentConstant(
        byte[] data,
        RuntimeInfo runtimeInfo,
        PatchEntry entry,
        float x,
        float y,
        float z,
        float w)
    {
        Span<byte> encoded = stackalloc byte[16];
        WriteFragmentFloat(encoded, 0, x);
        WriteFragmentFloat(encoded, 4, y);
        WriteFragmentFloat(encoded, 8, z);
        WriteFragmentFloat(encoded, 12, w);
        foreach (ushort patchOffset in entry.PatchOffsets)
        {
            int target = checked((int)runtimeInfo.UploadOffset + patchOffset);
            encoded.CopyTo(data.AsSpan(target, 16));
        }
    }

    private static void WriteFragmentFloat(Span<byte> destination, int offset, float value)
    {
        uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        BinaryPrimitives.WriteUInt32BigEndian(
            destination[offset..],
            RsxProgramDecoder.FragmentWord(bits));
    }

    private static bool TryReadRuntimeInfo(byte[] data, out RuntimeInfo info)
    {
        info = default;
        if (data.Length < 0x20)
            return false;
        uint tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));
        if (tag is not (0x1807 or 0x1b59 or 0x1b5b or 0x1b5c or 0x1b5d or 0x1b5e))
            return false;
        uint parameterCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x0c, 4));
        uint parameterTableOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x10, 4));
        uint uploadSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x18, 4));
        uint uploadOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x1c, 4));
        if ((ulong)parameterTableOffset + parameterCount * 0x30UL > (ulong)data.Length ||
            (ulong)uploadOffset + uploadSize > (ulong)data.Length ||
            parameterCount > ushort.MaxValue)
        {
            return false;
        }
        info = new RuntimeInfo(parameterCount, parameterTableOffset, uploadSize, uploadOffset);
        return true;
    }

    private static bool TryReadPatchEntry(byte[] data, RuntimeInfo info, ushort dest, out PatchEntry entry)
    {
        entry = default;
        if (dest >= info.ParameterCount)
            return false;
        uint entryOffset = info.ParameterTableOffset + dest * 0x30u;
        if (entryOffset + 0x1c > data.Length)
            return false;
        uint defaultConstantOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)entryOffset + 0x14, 4));
        uint patchListOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)entryOffset + 0x18, 4));
        if (patchListOffset == 0)
        {
            entry = new PatchEntry(defaultConstantOffset, []);
            return true;
        }
        if (patchListOffset + 4 > data.Length)
            return false;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)patchListOffset, 4));
        if (count > ushort.MaxValue || patchListOffset + 4 + count * 4UL > (ulong)data.Length)
            return false;
        var offsets = new ushort[(int)count];
        for (int index = 0; index < offsets.Length; index++)
        {
            uint rawOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)patchListOffset + 4 + index * 4, 4));
            if (rawOffset > ushort.MaxValue || rawOffset + 16 > info.UploadSize)
                return false;
            offsets[index] = (ushort)rawOffset;
        }
        entry = new PatchEntry(defaultConstantOffset, offsets);
        return true;
    }
}
