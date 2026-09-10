using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using MapConverter.Game.IW3.PC.Materials;
using MapConverter.Game.IW3.PC.Shaders;

namespace MapConverter.Game.IW3.PC.Techniques;

internal sealed record Iw3TechniqueSetCompilation(
    MaterialTechniqueSetAsset TechniqueSet,
    IReadOnlyList<MaterialShaderAsset> Shaders);

/// <summary>
/// Converts an extracted IW3 PC technique graph into the owned PS3 IW4 graph
/// consumed by IW4.Linker. Source bindings that do not have an established
/// target engine semantic are rejected instead of being substituted.
/// </summary>
internal sealed class Iw3TechniqueCompiler
{
    private const uint CgConstantRegisterResource = 0x0882;
    private const uint CgTextureUnitResourceBase = 0x0800;

    private readonly Dictionary<ShaderKey, CachedShader> _shaderCache = [];
    private readonly string _shaderDirectory;
    private readonly string _shaderDirectoryPrefix;
    private readonly Iw3PcShaderCompiler _shaderCompiler;

    internal Iw3TechniqueCompiler(
        string shaderDirectory,
        Iw3PcShaderCompiler shaderCompiler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderDirectory);
        ArgumentNullException.ThrowIfNull(shaderCompiler);

        _shaderDirectory = Path.GetFullPath(shaderDirectory);
        if (!Directory.Exists(_shaderDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The extracted shader directory does not exist: '{_shaderDirectory}'.");
        }

        _shaderDirectoryPrefix = _shaderDirectory.EndsWith(
            Path.DirectorySeparatorChar)
            ? _shaderDirectory
            : _shaderDirectory + Path.DirectorySeparatorChar;
        _shaderCompiler = shaderCompiler;
    }

    internal Iw3TechniqueSetCompilation Compile(Iw3TechniqueSetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateOwnedName(source.Name, "technique set");
        ValidateSourceSlots(source);
        string sourceName = source.Name[(source.Name.LastIndexOf('/') + 1)..];
        bool isWorld = Iw3MaterialCompiler.GetWorldTechniqueFamily(sourceName) is not null;

        var usedShaders = new Dictionary<ShaderKey, MaterialShaderAsset>();
        var compiledTechniques = new Dictionary<string, CompiledTechnique>(
            StringComparer.Ordinal);
        MaterialTechniqueSlot[] targetSlots = Iw3TechniqueSlotMapping.Iw4Slots
            .Select((sourceSlot, targetIndex) =>
            {
                if (sourceSlot is null)
                {
                    return new MaterialTechniqueSlot(
                        (MaterialTechniqueType)targetIndex,
                        default,
                        null);
                }

                Iw3TechniqueSlotSource slot = source.Slots[(int)sourceSlot.Value];
                MaterialTechniqueAsset? technique = slot.Technique is null
                    ? null
                    : CompileTechnique(
                        slot.Technique,
                        isWorld,
                        compiledTechniques,
                        usedShaders);
                return new MaterialTechniqueSlot(
                    (MaterialTechniqueType)targetIndex,
                    default,
                    technique);
            })
            .ToArray();

        var techniqueSet = new MaterialTechniqueSetAsset
        {
            Name = source.Name,
            WorldVertexFormat = DetermineWorldVertexFormat(source.Name),
            TechniqueSlots = Array.AsReadOnly(targetSlots)
        };
        MaterialShaderAsset[] shaders = usedShaders
            .OrderBy(pair => pair.Key.Stage)
            .ThenBy(pair => pair.Key.ShaderModel.Major)
            .ThenBy(pair => pair.Key.ShaderModel.Minor)
            .ThenBy(pair => pair.Key.ProgramName, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToArray();
        return new Iw3TechniqueSetCompilation(
            techniqueSet,
            Array.AsReadOnly(shaders));
    }

    private MaterialTechniqueAsset CompileTechnique(
        Iw3TechniqueSource source,
        bool isWorld,
        IDictionary<string, CompiledTechnique> compiledTechniques,
        IDictionary<ShaderKey, MaterialShaderAsset> usedShaders)
    {
        ValidateOwnedName(source.Name, "technique");
        if (compiledTechniques.TryGetValue(source.Name, out CompiledTechnique? cached))
        {
            if (!ReferenceEquals(cached.Source, source))
            {
                throw new InvalidDataException(
                    $"Technique '{source.Name}' was supplied by more than one parsed definition.");
            }

            AddUsedShaders(cached.ShaderKeys, usedShaders);
            return cached.Asset;
        }
        if (source.Passes.Count == 0)
            throw TechniqueError(source.Name, "must contain at least one pass");
        if (source.Passes.Count > ushort.MaxValue)
            throw TechniqueError(source.Name, "contains too many passes for the IW4 wire format");

        var shaderKeys = new HashSet<ShaderKey>();
        var passes = new MaterialPassAsset[source.Passes.Count];
        MaterialTechniqueFlags flags = MaterialTechniqueFlags.None;
        for (int index = 0; index < source.Passes.Count; index++)
        {
            CompiledPass compiled = CompilePass(
                source.Name,
                index,
                isWorld,
                source.Passes[index] ?? throw TechniqueError(
                    source.Name,
                    $"pass {index} is null"));
            passes[index] = compiled.Asset;
            flags |= compiled.Flags;
            shaderKeys.Add(compiled.VertexShader);
            shaderKeys.Add(compiled.PixelShader);
        }
        // ZPrepass permits IW4 to substitute its global depth material. Slot 0
        // alone does not prove position invariance with that native shader;
        // compiled prepasses must retain their own matching position program.
        var asset = new MaterialTechniqueAsset
        {
            Name = source.Name,
            Flags = flags,
            PassCount = checked((ushort)passes.Length),
            Passes = Array.AsReadOnly(passes)
        };
        var compiledTechnique = new CompiledTechnique(
            source,
            asset,
            Array.AsReadOnly(shaderKeys
                .OrderBy(key => key.Stage)
                .ThenBy(key => key.ShaderModel.Major)
                .ThenBy(key => key.ShaderModel.Minor)
                .ThenBy(key => key.ProgramName, StringComparer.Ordinal)
                .ToArray()));
        compiledTechniques.Add(source.Name, compiledTechnique);
        AddUsedShaders(compiledTechnique.ShaderKeys, usedShaders);
        return asset;
    }

    private CompiledPass CompilePass(
        string techniqueName,
        int passIndex,
        bool isWorld,
        Iw3TechniquePassSource source)
    {
        string passPath = $"Technique '{techniqueName}' pass {passIndex}";
        if (!string.Equals(source.StateMapName, "passthrough", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{passPath} uses state map '{source.StateMapName}', but only the extracted " +
                "passthrough state map has a proven IW4 representation.");
        }

        ShaderKey vertexKey = new(
            Iw3ShaderStage.Vertex,
            source.VertexShader.ShaderModel,
            source.VertexShader.ProgramName);
        ShaderKey pixelKey = new(
            Iw3ShaderStage.Pixel,
            source.PixelShader.ShaderModel,
            source.PixelShader.ProgramName);
        MaterialVertexDeclarationAsset declaration = CompileDeclaration(
            source.VertexRouting,
            passPath,
            isWorld);
        ushort signedNormalInputMask = 0;
        foreach (MaterialVertexStreamRouting route in declaration.Routing.Take(declaration.StreamCount))
        {
            if (route.Source is MaterialStreamSource.Normal or MaterialStreamSource.Tangent)
                signedNormalInputMask |= checked((ushort)(1 << (int)route.Dest));
        }
        Iw3ShaderCompilation vertex = CompileShader(source.VertexShader, passPath, signedNormalInputMask);
        Iw3ShaderCompilation pixel = CompileShader(source.PixelShader, passPath, 0);
        CompiledArguments arguments = CompileArguments(
            source.VertexShader,
            vertex,
            source.PixelShader,
            pixel,
            passPath);

        MaterialTechniqueFlags flags = arguments.Flags;
        if (declaration.HasOptionalSource)
            flags |= MaterialTechniqueFlags.DeclarationHasOptionalSource;

        return new CompiledPass(
            new MaterialPassAsset
            {
                VertexDeclaration = declaration,
                VertexShader = vertex.Asset,
                PixelShader = pixel.Asset,
                PerPrimArgCount = arguments.PerPrimitiveCount,
                PerObjArgCount = arguments.PerObjectCount,
                StableArgCount = arguments.RarelyCount,
                CustomSamplerFlags = arguments.CustomSamplerFlags,
                PrecompiledVertexShader = MaterialPrecompiledVertexShader.None,
                Args = arguments.Arguments
            },
            flags,
            vertexKey,
            pixelKey);
    }

    private Iw3ShaderCompilation CompileShader(
        Iw3ShaderSource source,
        string passPath,
        ushort signedNormalInputMask)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateOwnedName(source.ProgramName, "shader program");
        var key = new ShaderKey(source.Stage, source.ShaderModel, source.ProgramName);
        if (_shaderCache.TryGetValue(key, out CachedShader? cached))
        {
            if (cached.SignedNormalInputMask != signedNormalInputMask)
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' is reused with conflicting signed-normal " +
                    $"input masks 0x{cached.SignedNormalInputMask:X4} and 0x{signedNormalInputMask:X4}.");
            }
            var codeSamplerMasks = Iw3PcShaderCompiler.GetCodeSamplerMasks(source, cached.Compilation.Parameters);
            if (cached.CodeSamplerMasks != codeSamplerMasks)
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' is reused with conflicting code " +
                    $"sampler masks {cached.CodeSamplerMasks} and {codeSamplerMasks}.");
            }
            var positionMatrices = Iw3PcShaderCompiler.GetPositionMatrixRegisters(source, cached.Compilation.Parameters);
            if (cached.PositionMatrices != positionMatrices)
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' is reused with conflicting position " +
                    $"matrix registers {cached.PositionMatrices} and {positionMatrices}.");
            }
            var sunShadow = Iw3PcShaderCompiler.GetSunShadowRegisters(source, cached.Compilation.Parameters);
            int fogRegister = Iw3PcShaderCompiler.GetFogRegister(source, cached.Compilation.Parameters);
            int sunSpecular = Iw3PcShaderCompiler.GetSunSpecularRegister(source, cached.Compilation.Parameters);
            if (cached.SunShadow != sunShadow || cached.FogRegister != fogRegister || cached.SunSpecularRegister != sunSpecular)
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' is reused with conflicting Sun receiver, fog or sun-specular bindings.");
            }
            return cached.Compilation;
        }

        string stagePrefix = source.Stage == Iw3ShaderStage.Vertex ? "vs_" : "ps_";
        string programPath = Path.GetFullPath(
            Path.Combine(_shaderDirectory, stagePrefix + source.ProgramName + ".cso"));
        if (!programPath.StartsWith(_shaderDirectoryPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{passPath} shader name '{source.ProgramName}' resolves outside the extracted shader directory.");
        }

        Iw3ShaderCompilation compilation = _shaderCompiler.Compile(source, programPath, signedNormalInputMask);
        MaterialShaderKind expectedKind = source.Stage == Iw3ShaderStage.Vertex
            ? MaterialShaderKind.Vertex
            : MaterialShaderKind.Pixel;
        if (compilation.Asset.Kind != expectedKind ||
            !string.Equals(compilation.Asset.Name, source.ProgramName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{passPath} shader compiler returned a mismatched asset for '{source.ProgramName}'.");
        }
        if (compilation.Asset.Data is not { Length: > 0 } data ||
            compilation.Asset.DataSize != data.Length)
        {
            throw new InvalidDataException(
                $"{passPath} shader compiler returned no complete bytecode for '{source.ProgramName}'.");
        }
        EnsureUniqueParameters(compilation.Parameters, source.ProgramName);

        _shaderCache.Add(key, new CachedShader(compilation, signedNormalInputMask,
            Iw3PcShaderCompiler.GetCodeSamplerMasks(source, compilation.Parameters),
            Iw3PcShaderCompiler.GetPositionMatrixRegisters(source, compilation.Parameters),
            Iw3PcShaderCompiler.GetSunShadowRegisters(source, compilation.Parameters),
            Iw3PcShaderCompiler.GetFogRegister(source, compilation.Parameters),
            Iw3PcShaderCompiler.GetSunSpecularRegister(source, compilation.Parameters)));
        return compilation;
    }

    private static void EnsureUniqueParameters(
        IReadOnlyList<Iw3ShaderParameter> parameters,
        string shaderName)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < parameters.Count; index++)
        {
            Iw3ShaderParameter parameter = parameters[index] ??
                throw new InvalidDataException(
                    $"Shader '{shaderName}' parameter {index} is null.");
            if (string.IsNullOrWhiteSpace(parameter.Name) || !names.Add(parameter.Name))
            {
                throw new InvalidDataException(
                    $"Shader '{shaderName}' has an empty or duplicate reflected parameter at index {index}.");
            }
        }
    }

    private static MaterialVertexDeclarationAsset CompileDeclaration(
        IReadOnlyList<Iw3VertexStreamRoutingSource> source,
        string passPath,
        bool isWorld)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count > MaterialVertexDeclarationAsset.RoutingCount)
        {
            throw new InvalidDataException(
                $"{passPath} has {source.Count} vertex routes; IW4 supports at most " +
                $"{MaterialVertexDeclarationAsset.RoutingCount}.");
        }

        var target = new MaterialVertexStreamRouting[
            MaterialVertexDeclarationAsset.RoutingCount];
        var destinations = new HashSet<MaterialStreamDestination>();
        bool hasOptionalSource = false;
        for (int index = 0; index < source.Count; index++)
        {
            Iw3VertexStreamRoutingSource route = source[index] ??
                throw new InvalidDataException($"{passPath} vertex route {index} is null.");
            MaterialStreamSource streamSource = ParseStreamSource(route.Source, passPath);
            MaterialStreamDestination destination = ParseStreamDestination(
                route.Destination,
                passPath);
            if (!destinations.Add(destination))
            {
                throw new InvalidDataException(
                    $"{passPath} routes more than one source to vertex destination '{Format(route.Destination)}'.");
            }

            target[index] = new MaterialVertexStreamRouting(streamSource, destination);
            hasOptionalSource |= isWorld
                ? streamSource != MaterialStreamSource.Position
                : streamSource >= MaterialStreamSource.OptionalBegin;
        }

        return new MaterialVertexDeclarationAsset
        {
            StreamCount = checked((byte)source.Count),
            HasOptionalSourceRaw = hasOptionalSource ? (byte)1 : (byte)0,
            Routing = Array.AsReadOnly(target)
        };
    }

    private static MaterialStreamSource ParseStreamSource(
        Iw3IndexedName source,
        string passPath) => (source.Name, source.Index) switch
    {
        ("position", null) => MaterialStreamSource.Position,
        ("color", null) => MaterialStreamSource.Color,
        ("texcoord", 0) => MaterialStreamSource.TexCoord0,
        ("normal", null) => MaterialStreamSource.Normal,
        ("tangent", null) => MaterialStreamSource.Tangent,
        ("texcoord", 1) => MaterialStreamSource.TexCoord1,
        ("texcoord", 2) => MaterialStreamSource.TexCoord2,
        ("normalTransform", 0) => MaterialStreamSource.NormalTransform0,
        ("normalTransform", 1) => MaterialStreamSource.NormalTransform1,
        _ => throw new InvalidDataException(
            $"{passPath} uses unsupported IW3 vertex source '{Format(source)}'.")
    };

    private static MaterialStreamDestination ParseStreamDestination(
        Iw3IndexedName destination,
        string passPath) => (destination.Name, destination.Index) switch
    {
        ("position", null) => MaterialStreamDestination.Position,
        ("normal", null) => MaterialStreamDestination.Normal,
        ("color", 0) => MaterialStreamDestination.Color0,
        ("color", 1) => MaterialStreamDestination.Color1,
        ("texcoord", 0) => MaterialStreamDestination.TexCoord0,
        ("texcoord", 1) => MaterialStreamDestination.TexCoord1,
        ("texcoord", 2) => MaterialStreamDestination.TexCoord2,
        ("texcoord", 3) => MaterialStreamDestination.TexCoord3,
        ("texcoord", 4) => MaterialStreamDestination.TexCoord4,
        ("texcoord", 5) => MaterialStreamDestination.TexCoord5,
        ("texcoord", 6) => MaterialStreamDestination.TexCoord6,
        ("texcoord", 7) => MaterialStreamDestination.TexCoord7,
        _ => throw new InvalidDataException(
            $"{passPath} uses unsupported IW3 vertex destination '{Format(destination)}'.")
    };

    private static CompiledArguments CompileArguments(
        Iw3ShaderSource vertexSource,
        Iw3ShaderCompilation vertex,
        Iw3ShaderSource pixelSource,
        Iw3ShaderCompilation pixel,
        string passPath)
    {
        var arguments = new List<PendingArgument>();
        MaterialTechniqueFlags flags = MaterialTechniqueFlags.None;
        MaterialCustomSamplerFlags customSamplerFlags =
            MaterialCustomSamplerFlags.None;
        CompileStageArguments(
            vertexSource,
            vertex,
            passPath,
            arguments,
            ref flags,
            ref customSamplerFlags);
        CompileStageArguments(
            pixelSource,
            pixel,
            passPath,
            arguments,
            ref flags,
            ref customSamplerFlags);

        arguments.Sort(CompareArguments);
        byte perPrimitiveCount = checked((byte)arguments.Count(
            argument => argument.Frequency == MaterialUpdateFrequency.PerPrimitive));
        byte perObjectCount = checked((byte)arguments.Count(
            argument => argument.Frequency == MaterialUpdateFrequency.PerObject));
        byte rarelyCount = checked((byte)arguments.Count(
            argument => argument.Frequency == MaterialUpdateFrequency.Rarely));
        if (arguments.Any(argument => argument.Frequency == MaterialUpdateFrequency.Custom))
            throw new InvalidOperationException("Custom shader samplers cannot be retained as IW4 arguments.");

        MaterialShaderArgumentAsset[] retained = arguments
            .Select(argument => argument.Asset)
            .ToArray();
        return new CompiledArguments(
            Array.AsReadOnly(retained),
            perPrimitiveCount,
            perObjectCount,
            rarelyCount,
            customSamplerFlags,
            flags);
    }

    private static void CompileStageArguments(
        Iw3ShaderSource source,
        Iw3ShaderCompilation compilation,
        string passPath,
        ICollection<PendingArgument> arguments,
        ref MaterialTechniqueFlags flags,
        ref MaterialCustomSamplerFlags customSamplerFlags)
    {
        MaterialShaderKind expectedKind = source.Stage == Iw3ShaderStage.Vertex
            ? MaterialShaderKind.Vertex
            : MaterialShaderKind.Pixel;
        if (compilation.Asset.Kind != expectedKind)
            throw new InvalidDataException($"{passPath} has a mismatched compiled {source.Stage} shader.");

        var parameters = new Dictionary<string, (Iw3ShaderParameter Parameter, int Index)>(
            StringComparer.Ordinal);
        for (int index = 0; index < compilation.Parameters.Count; index++)
        {
            Iw3ShaderParameter parameter = compilation.Parameters[index];
            parameters.Add(parameter.Name, (parameter, index));
        }

        var assigned = new HashSet<int>();
        foreach (Iw3ShaderArgumentSource argument in source.Arguments)
        {
            if (argument.Destination.Index is not null)
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' uses indexed destination " +
                    $"'{Format(argument.Destination)}'; the IW4 pixel-parameter encoding for " +
                    "partial arrays is not established.");
            }
            if (!parameters.TryGetValue(
                    argument.Destination.Name,
                    out (Iw3ShaderParameter Parameter, int Index) target))
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' has no reflected destination " +
                    $"'{argument.Destination.Name}'.");
            }
            if (!target.Parameter.IsReferenced)
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' assigns unreferenced parameter " +
                    $"'{target.Parameter.Name}'.");
            }
            if (!assigned.Add(target.Index))
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' assigns parameter " +
                    $"'{target.Parameter.Name}' more than once.");
            }

            CompileExplicitArgument(
                source,
                target.Parameter,
                target.Index,
                argument,
                passPath,
                arguments,
                ref flags,
                ref customSamplerFlags);
        }

        for (int index = 0; index < compilation.Parameters.Count; index++)
        {
            Iw3ShaderParameter parameter = compilation.Parameters[index];
            if (!parameter.IsReferenced || assigned.Contains(index))
                continue;

            if (parameter.Kind == Iw3ShaderParameterKind.Constant)
            {
                if (!Iw3TechniqueBindingFacts.TryGetConstant(
                        parameter.Name,
                        out Iw3CodeConstantBinding binding))
                {
                    throw MissingImplicitBinding(passPath, source.ProgramName, parameter.Name, "constant");
                }
                AddCodeConstant(
                    source.Stage,
                    parameter,
                    index,
                    binding,
                    elementIndex: null,
                    passPath,
                    arguments,
                    ref flags);
            }
            else if (parameter.Kind == Iw3ShaderParameterKind.Sampler)
            {
                if (!Iw3TechniqueBindingFacts.TryGetSampler(
                        parameter.Name,
                        out Iw3CodeSamplerBinding binding))
                {
                    throw MissingImplicitBinding(passPath, source.ProgramName, parameter.Name, "sampler");
                }
                AddCodeSampler(
                    source.Stage,
                    parameter,
                    binding,
                    passPath,
                    arguments,
                    ref flags,
                    ref customSamplerFlags);
            }
            else
            {
                throw new InvalidDataException(
                    $"{passPath} shader '{source.ProgramName}' parameter '{parameter.Name}' has " +
                    $"unsupported reflected kind {parameter.Kind}.");
            }
        }
    }

    private static void CompileExplicitArgument(
        Iw3ShaderSource shader,
        Iw3ShaderParameter parameter,
        int parameterIndex,
        Iw3ShaderArgumentSource source,
        string passPath,
        ICollection<PendingArgument> arguments,
        ref MaterialTechniqueFlags flags,
        ref MaterialCustomSamplerFlags customSamplerFlags)
    {
        switch (source.Value)
        {
            case Iw3CodeShaderValueSource code
                when code.Kind == Iw3CodeShaderValueKind.Constant:
                if (!Iw3TechniqueBindingFacts.TryGetConstant(
                        code.Accessor,
                        out Iw3CodeConstantBinding constantBinding))
                {
                    throw UnsupportedBinding(passPath, code.Accessor, "constant");
                }
                AddCodeConstant(
                    shader.Stage,
                    parameter,
                    parameterIndex,
                    constantBinding,
                    code.ElementIndex,
                    passPath,
                    arguments,
                    ref flags);
                return;

            case Iw3CodeShaderValueSource code
                when code.Kind == Iw3CodeShaderValueKind.Sampler:
                if (code.ElementIndex is not null)
                    throw UnsupportedBinding(passPath, code.Accessor, "indexed sampler");
                if (!Iw3TechniqueBindingFacts.TryGetSampler(
                        code.Accessor,
                        out Iw3CodeSamplerBinding samplerBinding))
                {
                    throw UnsupportedBinding(passPath, code.Accessor, "sampler");
                }
                AddCodeSampler(
                    shader.Stage,
                    parameter,
                    samplerBinding,
                    passPath,
                    arguments,
                    ref flags,
                    ref customSamplerFlags);
                return;

            case Iw3LiteralShaderValueSource literal:
                RequireSingleConstant(shader, parameter, passPath);
                arguments.Add(new PendingArgument(
                    new MaterialShaderArgumentAsset(
                        Offset: 0,
                        Type: shader.Stage == Iw3ShaderStage.Vertex
                            ? MaterialShaderArgumentType.LiteralVertexConst
                            : MaterialShaderArgumentType.LiteralPixelConst,
                        Dest: GetConstantDestination(shader.Stage, parameter, parameterIndex, passPath),
                        ArgumentRaw: 0,
                        LiteralConstant: new MaterialShaderLiteralConstant(
                            literal.X,
                            literal.Y,
                            literal.Z,
                            literal.W)),
                    MaterialUpdateFrequency.Rarely));
                return;

            case Iw3MaterialShaderValueSource material:
                AddMaterialArgument(
                    shader.Stage,
                    parameter,
                    parameterIndex,
                    material,
                    passPath,
                    arguments);
                return;

            default:
                throw new InvalidDataException(
                    $"{passPath} shader '{shader.ProgramName}' has unsupported argument value " +
                    $"{source.Value?.GetType().Name ?? "<null>"}.");
        }
    }

    private static void AddMaterialArgument(
        Iw3ShaderStage stage,
        Iw3ShaderParameter parameter,
        int parameterIndex,
        Iw3MaterialShaderValueSource source,
        string passPath,
        ICollection<PendingArgument> arguments)
    {
        if (parameter.RegisterCount != 1)
        {
            throw new InvalidDataException(
                $"{passPath} material binding for '{parameter.Name}' spans " +
                $"{parameter.RegisterCount} registers; partial material arrays are not established.");
        }
        uint hash = source.PropertyHash ?? Iw3MaterialPropertyName.Hash(
            source.PropertyName ?? throw new InvalidDataException(
                $"{passPath} material binding for '{parameter.Name}' has no property name or hash."));

        MaterialShaderArgumentType type;
        ushort destination;
        if (parameter.Kind == Iw3ShaderParameterKind.Constant)
        {
            type = stage == Iw3ShaderStage.Vertex
                ? MaterialShaderArgumentType.MaterialVertexConst
                : MaterialShaderArgumentType.MaterialPixelConst;
            destination = GetConstantDestination(stage, parameter, parameterIndex, passPath);
        }
        else if (parameter.Kind == Iw3ShaderParameterKind.Sampler &&
                 stage == Iw3ShaderStage.Pixel)
        {
            type = MaterialShaderArgumentType.MaterialPixelSampler;
            destination = GetSamplerDestination(parameter, passPath);
        }
        else
        {
            throw new InvalidDataException(
                $"{passPath} cannot bind material property 0x{hash:X8} to {stage} " +
                $"parameter '{parameter.Name}' of kind {parameter.Kind}.");
        }

        arguments.Add(new PendingArgument(
            new MaterialShaderArgumentAsset(
                Offset: 0,
                Type: type,
                Dest: destination,
                ArgumentRaw: unchecked((int)hash),
                LiteralConstant: null),
            MaterialUpdateFrequency.Rarely));
    }

    private static void AddCodeConstant(
        Iw3ShaderStage stage,
        Iw3ShaderParameter parameter,
        int parameterIndex,
        Iw3CodeConstantBinding binding,
        int? elementIndex,
        string passPath,
        ICollection<PendingArgument> arguments,
        ref MaterialTechniqueFlags flags)
    {
        if (parameter.Kind != Iw3ShaderParameterKind.Constant)
        {
            throw new InvalidDataException(
                $"{passPath} cannot bind a code constant to sampler parameter '{parameter.Name}'.");
        }

        int sourceElement = elementIndex ?? 0;
        if (sourceElement < 0 || sourceElement >= binding.ArrayCount)
        {
            throw new InvalidDataException(
                $"{passPath} code constant element {sourceElement} for '{parameter.Name}' is " +
                $"outside its {binding.ArrayCount}-element source.");
        }

        MaterialConstantSource targetSource = binding.Source;
        byte firstRow = 0;
        byte rowCount;
        if (binding.IsMatrix)
        {
            const ushort direct3DMatrixRows = 2;
            const ushort direct3DMatrixColumns = 3;
            if (stage != Iw3ShaderStage.Vertex)
            {
                throw new InvalidDataException(
                    $"{passPath} matrix binding for '{parameter.Name}' requires a vertex shader; " +
                    "IW4 pixel matrix uploads are not established.");
            }
            if (parameter.Class is not (direct3DMatrixRows or direct3DMatrixColumns) ||
                parameter.Rows != 4 || parameter.Columns != 4 || parameter.Elements != 1)
            {
                throw new InvalidDataException(
                    $"{passPath} matrix binding for '{parameter.Name}' requires one row- or column-packed " +
                    $"float4x4; CTAB declares class {parameter.Class}, " +
                    $"{parameter.Rows}x{parameter.Columns}, {parameter.Elements} elements.");
            }
            if (parameter.RegisterCount == 0 || sourceElement + parameter.RegisterCount > 4)
            {
                throw new InvalidDataException(
                    $"{passPath} matrix binding for '{parameter.Name}' has invalid row range " +
                    $"{sourceElement}+{parameter.RegisterCount} for a float4x4 source.");
            }
            if (parameter.Class == direct3DMatrixColumns)
            {
                // IW4 uploads stored rows. Toggle the relative matrix transpose bit
                // for column-packed CTAB data, preserving the family and inverse bit.
                int matrixIndex = (int)binding.Source - (int)MaterialConstantSource.FirstCodeMatrix;
                targetSource = (MaterialConstantSource)(
                    (int)MaterialConstantSource.FirstCodeMatrix + (matrixIndex ^ 2));
            }
            firstRow = checked((byte)sourceElement);
            rowCount = checked((byte)parameter.RegisterCount);
        }
        else
        {
            if (parameter.RegisterCount != 1)
            {
                throw new InvalidDataException(
                    $"{passPath} code constant '{parameter.Name}' spans {parameter.RegisterCount} " +
                    "registers, but donor-free partial-array encoding is not established.");
            }
            targetSource = (MaterialConstantSource)checked(
                (ushort)((ushort)binding.Source + sourceElement));
            if (!Enum.IsDefined(targetSource))
            {
                throw new InvalidDataException(
                    $"{passPath} code constant '{parameter.Name}' maps outside the IW4 constant table.");
            }
            rowCount = 1;
        }

        MaterialShaderArgumentType type = stage == Iw3ShaderStage.Vertex
            ? MaterialShaderArgumentType.CodeVertexConst
            : MaterialShaderArgumentType.CodePixelConst;
        var code = new MaterialCodeConstantArgument(
            targetSource,
            firstRow,
            rowCount);
        arguments.Add(new PendingArgument(
            new MaterialShaderArgumentAsset(
                Offset: 0,
                Type: type,
                Dest: GetConstantDestination(stage, parameter, parameterIndex, passPath),
                ArgumentRaw: code.Raw,
                LiteralConstant: null),
            binding.Frequency));
        flags |= binding.Flags;
    }

    private static void AddCodeSampler(
        Iw3ShaderStage stage,
        Iw3ShaderParameter parameter,
        Iw3CodeSamplerBinding binding,
        string passPath,
        ICollection<PendingArgument> arguments,
        ref MaterialTechniqueFlags flags,
        ref MaterialCustomSamplerFlags customSamplerFlags)
    {
        if (stage != Iw3ShaderStage.Pixel ||
            parameter.Kind != Iw3ShaderParameterKind.Sampler ||
            parameter.RegisterCount != 1)
        {
            throw new InvalidDataException(
                $"{passPath} code sampler '{parameter.Name}' is not a single pixel sampler.");
        }

        flags |= binding.Flags;
        customSamplerFlags |= binding.CustomFlags;
        if (binding.Frequency == MaterialUpdateFrequency.Custom)
            return;

        arguments.Add(new PendingArgument(
            new MaterialShaderArgumentAsset(
                Offset: 0,
                Type: MaterialShaderArgumentType.CodePixelSampler,
                Dest: GetSamplerDestination(parameter, passPath),
                ArgumentRaw: unchecked((int)(uint)binding.Source),
                LiteralConstant: null),
            binding.Frequency));
    }

    private static ushort GetConstantDestination(
        Iw3ShaderStage stage,
        Iw3ShaderParameter parameter,
        int parameterIndex,
        string passPath)
    {
        if (parameter.Kind != Iw3ShaderParameterKind.Constant)
            throw new InvalidDataException($"{passPath} parameter '{parameter.Name}' is not a constant.");

        if (stage == Iw3ShaderStage.Pixel)
            return checked((ushort)parameterIndex);
        if (parameter.Resource != CgConstantRegisterResource)
        {
            throw new InvalidDataException(
                $"{passPath} vertex constant '{parameter.Name}' uses unsupported Cg resource " +
                $"0x{parameter.Resource:X}.");
        }
        return checked((ushort)parameter.ResourceIndex);
    }

    private static ushort GetSamplerDestination(
        Iw3ShaderParameter parameter,
        string passPath)
    {
        if (parameter.Kind != Iw3ShaderParameterKind.Sampler ||
            parameter.Resource < CgTextureUnitResourceBase)
        {
            throw new InvalidDataException(
                $"{passPath} sampler '{parameter.Name}' has unsupported Cg resource " +
                $"0x{parameter.Resource:X}.");
        }
        return checked((ushort)(parameter.Resource - CgTextureUnitResourceBase));
    }

    private static void RequireSingleConstant(
        Iw3ShaderSource shader,
        Iw3ShaderParameter parameter,
        string passPath)
    {
        if (parameter.Kind != Iw3ShaderParameterKind.Constant ||
            parameter.RegisterCount != 1)
        {
            throw new InvalidDataException(
                $"{passPath} literal for shader '{shader.ProgramName}' parameter " +
                $"'{parameter.Name}' is not one float4 constant.");
        }
    }

    private static int CompareArguments(PendingArgument left, PendingArgument right)
    {
        int order = left.Frequency.CompareTo(right.Frequency);
        if (order != 0)
            return order;
        order = ((ushort)left.Asset.Type).CompareTo((ushort)right.Asset.Type);
        if (order != 0)
            return order;

        bool material = left.Asset.Type is
            MaterialShaderArgumentType.MaterialVertexConst or
            MaterialShaderArgumentType.MaterialPixelSampler or
            MaterialShaderArgumentType.MaterialPixelConst;
        if (material)
        {
            order = unchecked((uint)left.Asset.ArgumentRaw).CompareTo(
                unchecked((uint)right.Asset.ArgumentRaw));
            if (order != 0)
                return order;
        }
        return left.Asset.Dest.CompareTo(right.Asset.Dest);
    }

    private static MaterialWorldVertexFormat DetermineWorldVertexFormat(string name)
    {
        int textureCount = 1;
        int normalCount = 1;
        for (int index = 1; index < name.Length - 1; index++)
        {
            char previous = name[index - 1];
            if (previous != '_' && !char.IsAsciiDigit(previous))
                continue;
            if (!char.IsAsciiDigit(name[index + 1]))
                continue;
            if (index + 2 < name.Length && char.IsAsciiDigit(name[index + 2]))
            {
                throw new InvalidDataException(
                    $"Technique set '{name}' uses a multi-digit texture-coordinate index; " +
                    "the IW4 world-vertex name encoding supports single-digit indices.");
            }

            int sourceIndex = name[index + 1] - '0';
            switch (char.ToLowerInvariant(name[index]))
            {
                case 'c':
                    textureCount = Math.Max(textureCount, sourceIndex + 1);
                    break;
                case 'n':
                    normalCount = Math.Max(normalCount, sourceIndex + 1);
                    break;
            }
        }
        return (textureCount, normalCount) switch
        {
            (1, 1) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_1_NRM_1,
            (2, 1) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_2_NRM_1,
            (2, 2) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_2_NRM_2,
            (3, 1) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_3_NRM_1,
            (3, 2) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_3_NRM_2,
            (3, 3) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_3_NRM_3,
            (4, 1) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_4_NRM_1,
            (4, 2) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_4_NRM_2,
            (4, 3) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_4_NRM_3,
            (5, 1) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_5_NRM_1,
            (5, 2) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_5_NRM_2,
            (5, 3) => MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_5_NRM_3,
            _ => throw new InvalidDataException(
                $"Technique set '{name}' encodes the unsupported world-vertex format " +
                $"{textureCount} texture coordinate(s), {normalCount} normal(s).")
        };
    }

    private static void ValidateSourceSlots(Iw3TechniqueSetSource source)
    {
        if (source.Slots.Count != Iw3TechniqueFormatParser.TechniqueSlotCount)
        {
            throw new InvalidDataException(
                $"Technique set '{source.Name}' has {source.Slots.Count} IW3 slots; expected " +
                $"{Iw3TechniqueFormatParser.TechniqueSlotCount}.");
        }

        for (int index = 0; index < source.Slots.Count; index++)
        {
            Iw3TechniqueSlotSource slot = source.Slots[index] ??
                throw new InvalidDataException(
                    $"Technique set '{source.Name}' slot {index} is null.");
            if ((int)slot.Slot != index)
            {
                throw new InvalidDataException(
                    $"Technique set '{source.Name}' slot {index} declares {slot.Slot}.");
            }
            if ((slot.TechniqueName is null) != (slot.Technique is null) ||
                (slot.Technique is not null && !string.Equals(
                    slot.TechniqueName,
                    slot.Technique.Name,
                    StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Technique set '{source.Name}' slot {slot.DisplayName} has an inconsistent technique reference.");
            }
        }
    }

    private void AddUsedShaders(
        IEnumerable<ShaderKey> keys,
        IDictionary<ShaderKey, MaterialShaderAsset> destination)
    {
        foreach (ShaderKey key in keys)
            destination.TryAdd(key, _shaderCache[key].Compilation.Asset);
    }

    private static void ValidateOwnedName(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name) || name[0] == ',')
            throw new InvalidDataException($"The IW3 {description} name must identify an owned asset.");
    }

    private static InvalidDataException TechniqueError(
        string name,
        string message) => new($"Technique '{name}' {message}.");

    private static InvalidDataException MissingImplicitBinding(
        string passPath,
        string shaderName,
        string parameterName,
        string kind) => new(
            $"{passPath} shader '{shaderName}' {kind} '{parameterName}' has no explicit " +
            "assignment and no proven IW3-to-IW4 PS3 code binding.");

    private static InvalidDataException UnsupportedBinding(
        string passPath,
        string accessor,
        string kind) => new(
            $"{passPath} uses IW3 {kind} '{accessor}', which has no proven IW4 PS3 binding.");

    private static string Format(Iw3IndexedName value) => value.Index is { } index
        ? $"{value.Name}[{index}]"
        : value.Name;

    private readonly record struct ShaderKey(
        Iw3ShaderStage Stage,
        Iw3ShaderModel ShaderModel,
        string ProgramName);

    private sealed record CachedShader(
        Iw3ShaderCompilation Compilation,
        ushort SignedNormalInputMask,
        (ushort Comparison, ushort ReflectionProbe) CodeSamplerMasks,
        (int World, int ViewProjection) PositionMatrices,
        (int Sampler, int PrimarySampler, int Switch, int Scale) SunShadow,
        int FogRegister,
        int SunSpecularRegister);

    private sealed record CompiledTechnique(
        Iw3TechniqueSource Source,
        MaterialTechniqueAsset Asset,
        IReadOnlyList<ShaderKey> ShaderKeys);

    private sealed record CompiledPass(
        MaterialPassAsset Asset,
        MaterialTechniqueFlags Flags,
        ShaderKey VertexShader,
        ShaderKey PixelShader);

    private sealed record PendingArgument(
        MaterialShaderArgumentAsset Asset,
        MaterialUpdateFrequency Frequency);

    private sealed record CompiledArguments(
        IReadOnlyList<MaterialShaderArgumentAsset> Arguments,
        byte PerPrimitiveCount,
        byte PerObjectCount,
        byte RarelyCount,
        MaterialCustomSamplerFlags CustomSamplerFlags,
        MaterialTechniqueFlags Flags);
}
