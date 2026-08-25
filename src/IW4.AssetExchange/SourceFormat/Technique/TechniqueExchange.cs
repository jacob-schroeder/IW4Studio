using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using IW4.AssetExchange.SourceFormat.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.AssetExchange.SourceFormat.Technique;

/// <summary>
/// Writes inline PS3 IW4 material techniques in the native .tech source
/// format used alongside technique sets.
/// </summary>
public sealed class TechniqueExchange
{
    private const uint CgConstantRegisterResource = 0x0882;
    private const uint CgTextureUnitResourceBase = 0x0800;
    private const int CgHeaderSize = 0x20;
    private const int CgParameterSize = 0x30;

    private static readonly string[] DirectConstantAccessors =
    [
        "light.position",
        "light.diffuse",
        "light.specular",
        "light.spotDir",
        "light.spotFactors",
        "light.falloffPlacement",
        "particleCloudColor",
        "gameTime",
        "pixelCostFracs",
        "pixelCostDecode",
        "filterTap[0]",
        "filterTap[1]",
        "filterTap[2]",
        "filterTap[3]",
        "filterTap[4]",
        "filterTap[5]",
        "filterTap[6]",
        "filterTap[7]",
        "colorMatrixR",
        "colorMatrixG",
        "colorMatrixB",
        "renderTargetSize",
        "dofEquationViewModelAndFarBlur",
        "dofEquationScene",
        "dofLerpScale",
        "dofLerpBias",
        "dofRowDelta",
        "motionMatrixX",
        "motionMatrixY",
        "motionMatrixW",
        "shadowmapSwitchPartition",
        "shadowmapScale",
        "zNear",
        "lightingLookupScale",
        "debugBumpmap",
        "materialColor",
        "fogConsts",
        "fogColorLinear",
        "fogColorGamma",
        "fogSunConsts",
        "fogSunColorLinear",
        "fogSunColorGamma",
        "fogSunDir",
        "glowSetup",
        "glowApply",
        "colorBias",
        "colorTintBase",
        "colorTintDelta",
        "colorTintQuadraticDelta",
        "outdoorFeatherParms",
        "envMapParms",
        "sunShadowmapPixelAdjust",
        "spotShadowmapPixelAdjust",
        "fullscreenDistortion",
        "fadeEffect",
        "viewportDimensions",
        "framebufferRead",
        "baseLightingCoords",
        "lightprobeAmbient",
        "nearPlane.org",
        "nearPlane.dx",
        "nearPlane.dy",
        "clipSpaceLookupScale",
        "clipSpaceLookupOffset",
        "particleCloudMatrix",
        "particleCloudMatrix1",
        "particleCloudMatrix2",
        "particleCloudSparkColor0",
        "particleCloudSparkColor1",
        "particleCloudSparkColor2",
        "particleFountainParms0",
        "particleFountainParms1",
        "depthFromClip",
        "codeMeshArg[0]",
        "codeMeshArg[1]"
    ];

    private static readonly string[] SamplerAccessors =
    [
        "black",
        "white",
        "identityNormalMap",
        "modelLightingSampler",
        "lightmap.primary",
        "lightmap.secondary",
        "shadowmapSun",
        "shadowmapSpot",
        "feedback",
        "resolvedPostSun",
        "resolvedScene",
        "postEffect0",
        "postEffect1",
        "light.attenuation",
        "outdoor",
        "floatZ",
        "processedFloatZ",
        "rawFloatZ",
        "halfParticleColorSampler",
        "halfParticleDepthSampler",
        "caseTexture",
        "cinematicYSampler",
        "cinematicCrSampler",
        "cinematicCbSampler",
        "cinematicASampler",
        "reflectionProbeSampler",
        "alternateSceneSampler"
    ];

    private readonly Dictionary<string, MaterialShaderAsset> _vertexShaders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MaterialShaderAsset> _pixelShaders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string> _reflectedPropertyNames = [];

    static TechniqueExchange()
    {
        if (DirectConstantAccessors.Length !=
            (int)MaterialConstantSource.FirstCodeMatrix)
        {
            throw new InvalidOperationException(
                "The technique source constant-accessor table does not match the PS3 IW4 constant layout.");
        }
        if (SamplerAccessors.Length != (int)MaterialTextureSource.Count)
        {
            throw new InvalidOperationException(
                "The technique source sampler-accessor table does not match the PS3 IW4 sampler layout.");
        }
    }

    public TechniqueExchange(IEnumerable<MaterialShaderAsset> shaderProviders)
    {
        ArgumentNullException.ThrowIfNull(shaderProviders);
        foreach (MaterialShaderAsset shader in shaderProviders)
        {
            if (shader is null || shader.Data is not { Length: > 0 })
                continue;

            Dictionary<string, MaterialShaderAsset> providers;
            if (shader.Kind == MaterialShaderKind.Vertex)
                providers = _vertexShaders;
            else if (shader.Kind == MaterialShaderKind.Pixel)
                providers = _pixelShaders;
            else
                continue;

            string name;
            try
            {
                name = SourceOutput.NormalizeReferencedAssetName(
                    shader.Name,
                    $"{shader.Kind} shader provider");
            }
            catch (InvalidDataException)
            {
                // An unusable provider must not prevent unrelated techniques
                // from being dumped. A selected linked shader is validated by
                // WriteShader with its technique and pass context.
                continue;
            }
            providers.TryAdd(name, shader);

            try
            {
                HarvestPropertyNames(ReadParameters(shader.Data, name));
            }
            catch (InvalidDataException)
            {
                // A malformed provider is reported if a technique actually
                // selects it. It cannot contribute reflection names here.
            }
        }
    }

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        MaterialTechniqueAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "Technique");
        if (asset.PassCount != asset.Passes.Count)
        {
            throw new InvalidDataException(
                $"Technique '{assetName}' declares {asset.PassCount} passes but has {asset.Passes.Count} materialized passes.");
        }

        return new SourceOutput(sourceDirectory).WriteTextBatch([
            ($"techniques/{assetName}.tech", writer =>
                WriteSource(writer, asset, assetName))
        ]);
    }

    private void WriteSource(
        TextWriter writer,
        MaterialTechniqueAsset technique,
        string techniqueName)
    {
        for (int index = 0; index < technique.Passes.Count; index++)
        {
            MaterialPassAsset pass = technique.Passes[index] ??
                throw new InvalidDataException(
                    $"Technique '{techniqueName}' pass {index} is null.");
            WritePass(writer, pass, techniqueName, index);
        }
    }

    private void WritePass(
        TextWriter writer,
        MaterialPassAsset pass,
        string techniqueName,
        int passIndex)
    {
        int declaredArgCount = checked(
            pass.PerPrimArgCount +
            pass.PerObjArgCount +
            pass.StableArgCount);
        if (declaredArgCount != pass.Args.Count)
        {
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} declares {declaredArgCount} shader arguments but has {pass.Args.Count} materialized arguments.");
        }
        foreach (MaterialShaderArgumentAsset argument in pass.Args)
            _ = GetArgumentStage(argument.Type, techniqueName, passIndex);

        writer.WriteLine("{");
        writer.WriteLine("  stateMap \"passthrough\"; // TODO");
        WriteShader(
            writer,
            pass.VertexShader,
            MaterialShaderKind.Vertex,
            pass.Args,
            techniqueName,
            passIndex);
        WriteShader(
            writer,
            pass.PixelShader,
            MaterialShaderKind.Pixel,
            pass.Args,
            techniqueName,
            passIndex);
        WriteVertexRoutes(
            writer,
            pass.VertexDeclaration,
            techniqueName,
            passIndex);
        writer.WriteLine("}");
    }

    private void WriteShader(
        TextWriter writer,
        MaterialShaderAsset? linkedShader,
        MaterialShaderKind kind,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        string techniqueName,
        int passIndex)
    {
        if (linkedShader is null)
        {
            int argumentCount = arguments.Count(argument =>
                GetArgumentStage(
                    argument.Type,
                    techniqueName,
                    passIndex) == kind);
            if (argumentCount != 0)
            {
                throw new InvalidDataException(
                    $"Technique '{techniqueName}' pass {passIndex} has {argumentCount} {kind.ToString().ToLowerInvariant()} shader arguments but no {kind.ToString().ToLowerInvariant()} shader.");
            }
            return;
        }
        if (linkedShader.Kind != kind)
        {
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} has a {linkedShader.Kind} shader in its {kind} slot.");
        }

        string shaderName = SourceOutput.NormalizeReferencedAssetName(
            linkedShader.Name,
            $"Technique '{techniqueName}' pass {passIndex} {kind} shader");
        if (shaderName.Contains('"'))
        {
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} shader name cannot be represented in a quoted source field.");
        }

        MaterialShaderAsset shader = ResolveShader(linkedShader, shaderName);
        if (shader.Data is not { Length: > 0 } data)
        {
            writer.Write("  // Cannot dump ");
            writer.Write(kind == MaterialShaderKind.Vertex
                ? "vertex"
                : "pixel");
            writer.Write(" shader ");
            writer.Write(shaderName);
            writer.WriteLine(" due to being a referenced asset");
            return;
        }

        ShaderParameter[] parameters = ReadParameters(data, shaderName);
        HarvestPropertyNames(parameters);
        writer.WriteLine();
        writer.Write("  ");
        writer.Write(kind == MaterialShaderKind.Vertex
            ? "vertexShader"
            : "pixelShader");
        writer.Write(" 3.0 \"");
        writer.Write(shaderName);
        writer.WriteLine("\"");
        writer.WriteLine("  {");
        foreach (MaterialShaderArgumentAsset argument in arguments)
        {
            if (GetArgumentStage(argument.Type, techniqueName, passIndex) != kind)
                continue;

            ShaderParameter destination = ResolveDestination(
                parameters,
                argument,
                kind,
                techniqueName,
                passIndex,
                shaderName);
            (string expression, bool omitWhenMatching) = ResolveExpression(
                argument,
                destination,
                techniqueName,
                passIndex);
            if (omitWhenMatching && string.Equals(
                    destination.Name,
                    expression[(expression.IndexOf('.') + 1)..],
                    StringComparison.Ordinal))
            {
                continue;
            }

            writer.Write("    ");
            writer.Write(destination.Name);
            writer.Write(" = ");
            writer.Write(expression);
            writer.WriteLine(';');
        }
        writer.WriteLine("  }");
    }

    private MaterialShaderAsset ResolveShader(
        MaterialShaderAsset linkedShader,
        string shaderName)
    {
        if (linkedShader.Data is { Length: > 0 })
            return linkedShader;

        Dictionary<string, MaterialShaderAsset> providers =
            linkedShader.Kind == MaterialShaderKind.Vertex
                ? _vertexShaders
                : _pixelShaders;
        return providers.TryGetValue(shaderName, out MaterialShaderAsset? provider)
            ? provider
            : linkedShader;
    }

    private static MaterialShaderKind GetArgumentStage(
        MaterialShaderArgumentType type,
        string techniqueName,
        int passIndex) => (ushort)type switch
    {
        0 or 1 or 3 => MaterialShaderKind.Vertex,
        2 or 4 or 5 or 6 or 7 => MaterialShaderKind.Pixel,
        _ => throw new InvalidDataException(
            $"Technique '{techniqueName}' pass {passIndex} has unsupported shader argument type 0x{(ushort)type:X4}.")
    };

    private static ShaderParameter ResolveDestination(
        IReadOnlyList<ShaderParameter> parameters,
        MaterialShaderArgumentAsset argument,
        MaterialShaderKind kind,
        string techniqueName,
        int passIndex,
        string shaderName)
    {
        ushort numericType = (ushort)argument.Type;
        if (kind == MaterialShaderKind.Vertex)
        {
            ShaderParameter[] matches = parameters
                .Where(parameter =>
                    parameter.IsReferenced &&
                    parameter.Variability == 0x1006 &&
                    parameter.Resource == CgConstantRegisterResource &&
                    parameter.ResourceIndex == argument.Dest)
                .OrderBy(parameter => parameter.Name.Contains('[') ? 1 : 0)
                .ToArray();
            if (matches.Length != 0)
                return matches[0];
        }
        else if (numericType is 2 or 4)
        {
            uint resource = checked(CgTextureUnitResourceBase + argument.Dest);
            ShaderParameter[] matches = parameters
                .Where(parameter =>
                    parameter.IsReferenced &&
                    parameter.Variability == 0x1006 &&
                    parameter.Resource == resource)
                .ToArray();
            if (matches.Length == 1)
                return matches[0];
            if (matches.Length > 1)
            {
                return matches
                    .OrderBy(parameter => parameter.Name.Contains('[') ? 1 : 0)
                    .First();
            }
        }
        else if (argument.Dest < parameters.Count)
        {
            ShaderParameter parameter = parameters[argument.Dest];
            if (parameter.IsReferenced && parameter.Variability == 0x1006)
                return parameter;
        }

        throw new InvalidDataException(
            $"Technique '{techniqueName}' pass {passIndex} shader '{shaderName}' has no reflected destination for {argument.Type} argument {argument.Dest}.");
    }

    private (string Expression, bool OmitWhenMatching) ResolveExpression(
        MaterialShaderArgumentAsset argument,
        ShaderParameter destination,
        string techniqueName,
        int passIndex) => (ushort)argument.Type switch
    {
        0 or 2 or 6 =>
            ($"material.{ResolveMaterialProperty(argument.MaterialNameHash, destination.Name)}", false),
        1 or 7 =>
            ($"float4( {FormatLiteral(argument, 0, techniqueName, passIndex)}, " +
             $"{FormatLiteral(argument, 1, techniqueName, passIndex)}, " +
             $"{FormatLiteral(argument, 2, techniqueName, passIndex)}, " +
             $"{FormatLiteral(argument, 3, techniqueName, passIndex)} )", false),
        3 or 5 =>
            ($"constant.{ResolveConstantAccessor(argument, destination, techniqueName, passIndex)}", true),
        4 =>
            ($"sampler.{ResolveSamplerAccessor(argument, techniqueName, passIndex)}", true),
        _ => throw new InvalidDataException(
            $"Technique '{techniqueName}' pass {passIndex} has unsupported shader argument type 0x{(ushort)argument.Type:X4}.")
    };

    private string ResolveMaterialProperty(uint hash, string destination)
    {
        if (MaterialExchange.HashSourcePropertyName(destination) == hash)
            return destination;
        if (_reflectedPropertyNames.TryGetValue(hash, out string? reflected))
            return reflected;
        if (MaterialExchange.TryGetKnownSourcePropertyName(hash, out string known))
            return known;
        return $"#0x{hash:x}";
    }

    private static string ResolveConstantAccessor(
        MaterialShaderArgumentAsset argument,
        ShaderParameter destination,
        string techniqueName,
        int passIndex)
    {
        MaterialCodeConstantArgument source = argument.CodeConstant;
        int sourceIndex = source.SourceIndex;
        bool isMatrixSource = sourceIndex >=
            (int)MaterialConstantSource.FirstCodeMatrix &&
            sourceIndex < (int)MaterialConstantSource.TotalCount;
        if (destination.IsMatrix != isMatrixSource)
        {
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} argument {argument.Dest} matrix shape does not match code source 0x{sourceIndex:X2}.");
        }
        if (source.FirstRow != 0 ||
            (!isMatrixSource && source.RowCount != 1) ||
            (isMatrixSource && source.RowCount <= 1))
        {
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} argument {argument.Dest} has unsupported code-constant rows {source.FirstRow}+{source.RowCount}.");
        }

        if (!isMatrixSource)
        {
            if ((uint)sourceIndex >= DirectConstantAccessors.Length)
            {
                throw new InvalidDataException(
                    $"Technique '{techniqueName}' pass {passIndex} uses unknown code constant 0x{sourceIndex:X2}.");
            }
            return DirectConstantAccessors[sourceIndex];
        }

        return MatrixAccessor(sourceIndex);
    }

    private static string ResolveSamplerAccessor(
        MaterialShaderArgumentAsset argument,
        string techniqueName,
        int passIndex)
    {
        uint source = (uint)argument.CodeTextureSource;
        if (source >= SamplerAccessors.Length)
        {
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} uses unknown code sampler 0x{source:X2}.");
        }
        return SamplerAccessors[source];
    }

    private static string MatrixAccessor(int sourceIndex)
    {
        int relative = sourceIndex -
            (int)MaterialConstantSource.FirstCodeMatrix;
        string matrixName;
        int variant;
        string suffix = string.Empty;
        if (relative < 20)
        {
            string[] names =
            [
                "ViewMatrix",
                "ProjectionMatrix",
                "ViewProjectionMatrix",
                "ShadowLookupMatrix",
                "WorldOutdoorLookupMatrix"
            ];
            matrixName = names[relative / 4];
            variant = relative % 4;
        }
        else
        {
            int worldRelative = relative - 20;
            int instance = worldRelative / 12;
            int withinInstance = worldRelative % 12;
            string[] names =
            [
                "WorldMatrix",
                "WorldViewMatrix",
                "WorldViewProjectionMatrix"
            ];
            matrixName = names[withinInstance / 4];
            variant = withinInstance % 4;
            if (instance != 0)
                suffix = instance.ToString(CultureInfo.InvariantCulture);
        }

        string prefix = (variant ^ 2) switch
        {
            0 => string.Empty,
            1 => "inverse",
            2 => "transpose",
            3 => "inverseTranspose",
            _ => throw new InvalidOperationException(
                "A matrix transform variant is outside its four-value group.")
        };
        if (prefix.Length == 0)
        {
            matrixName = char.ToLowerInvariant(matrixName[0]) +
                matrixName[1..];
        }
        return prefix + matrixName + suffix;
    }

    private static string FormatLiteral(
        MaterialShaderArgumentAsset argument,
        int component,
        string techniqueName,
        int passIndex)
    {
        MaterialShaderLiteralConstant literal = argument.LiteralConstant ??
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} literal argument {argument.Dest} has no materialized value.");
        float value = component switch
        {
            0 => literal.X,
            1 => literal.Y,
            2 => literal.Z,
            3 => literal.W,
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} literal argument {argument.Dest} is not finite.");
        }
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void WriteVertexRoutes(
        TextWriter writer,
        MaterialVertexDeclarationAsset? declaration,
        string techniqueName,
        int passIndex)
    {
        if (declaration is null)
            return;
        if (declaration.StreamCount > declaration.Routing.Count)
        {
            throw new InvalidDataException(
                $"Technique '{techniqueName}' pass {passIndex} vertex declaration requires {declaration.StreamCount} routes but has {declaration.Routing.Count}.");
        }
        if (declaration.StreamCount == 0)
            return;

        writer.WriteLine();
        for (int index = 0; index < declaration.StreamCount; index++)
        {
            MaterialVertexStreamRouting route = declaration.Routing[index];
            writer.Write("  vertex.");
            writer.Write(DestinationName(
                route.Dest,
                techniqueName,
                passIndex));
            writer.Write(" = code.");
            writer.Write(SourceName(
                route.Source,
                techniqueName,
                passIndex));
            writer.WriteLine(';');
        }
    }

    private static string SourceName(
        MaterialStreamSource source,
        string techniqueName,
        int passIndex) => (byte)source switch
    {
        0 => "position",
        1 => "color",
        2 => "texcoord[0]",
        3 => "normal",
        4 => "tangent",
        5 => "texcoord[1]",
        6 => "texcoord[2]",
        7 => "normalTransform[0]",
        8 => "normalTransform[1]",
        _ => throw new InvalidDataException(
            $"Technique '{techniqueName}' pass {passIndex} has unknown vertex source 0x{(byte)source:X2}.")
    };

    private static string DestinationName(
        MaterialStreamDestination destination,
        string techniqueName,
        int passIndex) => (byte)destination switch
    {
        0x0 => "position",
        0x2 => "normal",
        0x3 => "color[0]",
        0x4 => "color[1]",
        0x8 => "texcoord[0]",
        0x9 => "texcoord[1]",
        0xA => "texcoord[2]",
        0xB => "texcoord[3]",
        0xC => "texcoord[4]",
        0xD => "texcoord[5]",
        0xE => "texcoord[6]",
        0xF => "texcoord[7]",
        _ => throw new InvalidDataException(
            $"Technique '{techniqueName}' pass {passIndex} has an unproven PS3 vertex destination 0x{(byte)destination:X2}.")
    };

    private void HarvestPropertyNames(IEnumerable<ShaderParameter> parameters)
    {
        foreach (ShaderParameter parameter in parameters)
        {
            if (!parameter.IsReferenced || parameter.Variability != 0x1006)
                continue;
            AddReflectedPropertyName(parameter.Name);
            int samplerIndex = parameter.Name.LastIndexOf(
                "Sampler",
                StringComparison.Ordinal);
            if (samplerIndex >= 0)
            {
                AddReflectedPropertyName(
                    parameter.Name.Remove(samplerIndex, "Sampler".Length));
            }
        }
    }

    private void AddReflectedPropertyName(string name) =>
        _reflectedPropertyNames.TryAdd(
            MaterialExchange.HashSourcePropertyName(name),
            name);

    private static ShaderParameter[] ReadParameters(
        byte[] data,
        string shaderName)
    {
        if (data.Length < CgHeaderSize)
        {
            throw new InvalidDataException(
                $"Shader '{shaderName}' is too small to contain a PS3 Cg header.");
        }
        uint profile = ReadUInt32(data, 0);
        if (profile is not (0x1807 or 0x1B59 or 0x1B5B or 0x1B5C or 0x1B5D or 0x1B5E))
        {
            throw new InvalidDataException(
                $"Shader '{shaderName}' has unsupported PS3 Cg profile 0x{profile:X}.");
        }

        uint totalSize = ReadUInt32(data, 0x08);
        uint parameterCount = ReadUInt32(data, 0x0C);
        uint parameterOffset = ReadUInt32(data, 0x10);
        if (totalSize < CgHeaderSize || totalSize > data.Length ||
            parameterCount > ushort.MaxValue ||
            (ulong)parameterOffset +
                parameterCount * (ulong)CgParameterSize > totalSize)
        {
            throw new InvalidDataException(
                $"Shader '{shaderName}' has an invalid PS3 Cg parameter table.");
        }

        var parameters = new ShaderParameter[(int)parameterCount];
        for (int index = 0; index < parameters.Length; index++)
        {
            int entry = checked((int)parameterOffset +
                index * CgParameterSize);
            uint nameOffset = ReadUInt32(data, entry + 0x10);
            string name = ReadParameterName(
                data.AsSpan(0, checked((int)totalSize)),
                nameOffset,
                shaderName,
                index);
            parameters[index] = new ShaderParameter(
                ReadUInt32(data, entry),
                ReadUInt32(data, entry + 0x04),
                ReadUInt32(data, entry + 0x08),
                ReadUInt32(data, entry + 0x0C),
                name,
                ReadUInt32(data, entry + 0x28) == 1);
        }
        return parameters;
    }

    private static string ReadParameterName(
        ReadOnlySpan<byte> data,
        uint rawOffset,
        string shaderName,
        int parameterIndex)
    {
        if (rawOffset == 0 || rawOffset >= data.Length)
        {
            throw new InvalidDataException(
                $"Shader '{shaderName}' parameter {parameterIndex} has no valid name offset.");
        }
        ReadOnlySpan<byte> tail = data[(int)rawOffset..];
        int terminator = tail.IndexOf((byte)0);
        if (terminator <= 0)
        {
            throw new InvalidDataException(
                $"Shader '{shaderName}' parameter {parameterIndex} has no valid name.");
        }
        ReadOnlySpan<byte> encoded = tail[..terminator];
        foreach (byte character in encoded)
        {
            bool valid = character is >= (byte)'a' and <= (byte)'z' or
                >= (byte)'A' and <= (byte)'Z' or
                >= (byte)'0' and <= (byte)'9' or
                (byte)'_' or (byte)'.' or (byte)'$' or
                (byte)'[' or (byte)']' or (byte)'-';
            if (!valid)
            {
                throw new InvalidDataException(
                    $"Shader '{shaderName}' parameter {parameterIndex} has a name that cannot be represented in technique source.");
            }
        }
        return Encoding.ASCII.GetString(encoded);
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private readonly record struct ShaderParameter(
        uint Type,
        uint Resource,
        uint Variability,
        uint ResourceIndex,
        string Name,
        bool IsReferenced)
    {
        public bool IsMatrix => Type is 0x423 or 0x424 or 0x427 or 0x428;
    }
}
