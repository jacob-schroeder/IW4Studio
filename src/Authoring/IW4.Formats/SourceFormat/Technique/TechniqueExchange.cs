using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Formats.SourceFormat.Technique;

/// <summary>Exchanges the native PS3 material-technique graph as versioned source.</summary>
public sealed class TechniqueExchange
{
    private const string Format = "iw4-ps3-technique";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<MaterialTextureSource>() }
    };

    public IReadOnlyList<string> Unlink(string sourceDirectory, MaterialTechniqueAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string name = SourceOutput.NormalizeOwnedAssetName(asset.Name, "Technique");
        if (asset.PassCount != asset.Passes.Count)
            throw new InvalidDataException($"Technique '{name}' pass count does not match its materialized passes.");
        var document = new Document
        {
            Format = Format,
            Version = 2,
            Name = name,
            Flags = (ushort)asset.Flags,
            Passes = asset.Passes.Select((pass, index) =>
                WritePass(pass ?? throw new InvalidDataException(
                    $"Technique '{name}' pass {index} is null."), name, index)).ToArray()
        };
        string json = JsonSerializer.Serialize(document, JsonOptions);
        return new SourceOutput(sourceDirectory).WriteTextBatch([
            ($"techniques/{name}.tech.json", writer => writer.WriteLine(json))
        ]);
    }

    public MaterialTechniqueAsset Link(string sourceDirectory, string assetName)
    {
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, "Technique");
        string path = NativeSourcePath.Resolve(
            sourceDirectory, "techniques", name, ".tech.json");
        using FileStream stream = File.OpenRead(path);
        Document document = JsonSerializer.Deserialize<Document>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Technique '{name}' has an empty source document.");
        if (document.Format != Format || document.Version is not (1 or 2) || document.Name != name)
            throw new InvalidDataException($"Technique '{name}' has an unsupported format, version, or name.");
        if (document.Passes is null || document.Passes.Length > ushort.MaxValue)
            throw new InvalidDataException($"Technique '{name}' has an invalid pass count.");
        return new MaterialTechniqueAsset
        {
            Name = name,
            Flags = (MaterialTechniqueFlags)document.Flags,
            PassCount = checked((ushort)document.Passes.Length),
            Passes = document.Passes.Select((pass, index) =>
                ReadPass(pass, name, index, document.Version)).ToArray()
        };
    }

    private static PassDocument WritePass(MaterialPassAsset pass, string name, int index)
    {
        int count = checked(pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount);
        if (pass.Args.Count != count)
            throw new InvalidDataException($"Technique '{name}' pass {index} argument counts do not match.");
        if ((pass.VertexShader is null && pass.VertexShaderPointer.Type != IW4.Game.Pointers.PointerType.Null) ||
            (pass.PixelShader is null && pass.PixelShaderPointer.Type != IW4.Game.Pointers.PointerType.Null) ||
            (pass.VertexDeclaration is null && pass.VertexDeclPointer.Type != IW4.Game.Pointers.PointerType.Null))
            throw new InvalidDataException($"Technique '{name}' pass {index} has an unresolved shader or vertex declaration.");
        return new PassDocument
        {
            PerPrimArgCount = pass.PerPrimArgCount,
            PerObjArgCount = pass.PerObjArgCount,
            StableArgCount = pass.StableArgCount,
            CustomSamplerFlags = (byte)pass.CustomSamplerFlags,
            PrecompiledVertexShader = (byte)pass.PrecompiledVertexShader,
            VertexShader = ShaderName(pass.VertexShader, MaterialShaderKind.Vertex, name, index),
            PixelShader = ShaderName(pass.PixelShader, MaterialShaderKind.Pixel, name, index),
            VertexDeclaration = pass.VertexDeclaration is null
                ? null : WriteDeclaration(pass.VertexDeclaration, name, index),
            Arguments = pass.Args.Select((argument, argumentIndex) =>
                WriteArgument(argument ?? throw new InvalidDataException(
                    $"Technique '{name}' pass {index} argument {argumentIndex} is null."),
                    name, index, argumentIndex)).ToArray()
        };
    }

    private static MaterialPassAsset ReadPass(PassDocument pass, string name, int index, int version)
    {
        string path = $"Technique '{name}' pass {index}";
        if (pass is null || pass.Arguments is null)
            throw new InvalidDataException($"{path} is incomplete.");
        int count = checked(pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount);
        if (pass.Arguments.Length != count)
            throw new InvalidDataException($"{path} argument counts do not match.");
        if ((pass.CustomSamplerFlags & ~0x07) != 0 ||
            !Enum.IsDefined((MaterialPrecompiledVertexShader)pass.PrecompiledVertexShader))
            throw new InvalidDataException($"{path} has unsupported sampler or precompiled-shader flags.");
        return new MaterialPassAsset
        {
            PerPrimArgCount = pass.PerPrimArgCount,
            PerObjArgCount = pass.PerObjArgCount,
            StableArgCount = pass.StableArgCount,
            CustomSamplerFlags = (MaterialCustomSamplerFlags)pass.CustomSamplerFlags,
            PrecompiledVertexShader = (MaterialPrecompiledVertexShader)pass.PrecompiledVertexShader,
            VertexShader = ReadShader(pass.VertexShader, MaterialShaderKind.Vertex, path),
            PixelShader = ReadShader(pass.PixelShader, MaterialShaderKind.Pixel, path),
            VertexDeclaration = pass.VertexDeclaration is null
                ? null : ReadDeclaration(pass.VertexDeclaration, path),
            Args = pass.Arguments.Select((argument, argumentIndex) =>
                ReadArgument(argument, $"{path} argument {argumentIndex}", version)).ToArray()
        };
    }

    private static string? ShaderName(
        MaterialShaderAsset? shader, MaterialShaderKind kind, string name, int index)
    {
        if (shader is null)
            return null;
        if (shader.Kind != kind)
            throw new InvalidDataException($"Technique '{name}' pass {index} has a shader in the wrong stage.");
        return SourceOutput.NormalizeReferencedAssetName(
            shader.Name, $"Technique '{name}' pass {index} {kind} shader");
    }

    private static MaterialShaderAsset? ReadShader(
        string? shaderName, MaterialShaderKind kind, string path) =>
        shaderName is null ? null : new MaterialShaderAsset
        {
            Name = SourceOutput.NormalizeReferencedAssetName(shaderName, $"{path} {kind} shader"),
            Kind = kind
        };

    private static DeclarationDocument WriteDeclaration(
        MaterialVertexDeclarationAsset declaration, string name, int index)
    {
        if (declaration.Routing.Count != MaterialVertexDeclarationAsset.RoutingCount ||
            declaration.StreamCount > declaration.Routing.Count)
            throw new InvalidDataException($"Technique '{name}' pass {index} has incomplete vertex routing.");
        return new DeclarationDocument
        {
            StreamCount = declaration.StreamCount,
            HasOptionalSourceRaw = declaration.HasOptionalSourceRaw,
            Routing = declaration.Routing.Select(route => new RouteDocument
            {
                Source = (byte)route.Source,
                Destination = (byte)route.Dest
            }).ToArray()
        };
    }

    private static MaterialVertexDeclarationAsset ReadDeclaration(
        DeclarationDocument declaration, string path)
    {
        if (declaration.Routing is null ||
            declaration.Routing.Length != MaterialVertexDeclarationAsset.RoutingCount ||
            declaration.StreamCount > declaration.Routing.Length)
            throw new InvalidDataException($"{path} has incomplete vertex routing.");
        return new MaterialVertexDeclarationAsset
        {
            StreamCount = declaration.StreamCount,
            HasOptionalSourceRaw = declaration.HasOptionalSourceRaw,
            Routing = declaration.Routing.Select(route => route is null
                ? throw new InvalidDataException($"{path} has a null vertex route.")
                : new MaterialVertexStreamRouting(
                    (MaterialStreamSource)route.Source,
                    (MaterialStreamDestination)route.Destination)).ToArray()
        };
    }

    private static ArgumentDocument WriteArgument(
        MaterialShaderArgumentAsset argument, string name, int passIndex, int argumentIndex)
    {
        string path = $"Technique '{name}' pass {passIndex} argument {argumentIndex}";
        bool isLiteral = argument.Type is MaterialShaderArgumentType.LiteralVertexConst or
            MaterialShaderArgumentType.LiteralPixelConst;
        if (isLiteral != argument.LiteralConstant.HasValue)
            throw new InvalidDataException($"{path} has an invalid literal value.");
        var document = new ArgumentDocument
        {
            Type = JsonSerializer.SerializeToElement(argument.TypeName),
            Destination = argument.Dest
        };
        switch (argument.Type)
        {
            case MaterialShaderArgumentType.CodeVertexConst:
            case MaterialShaderArgumentType.CodePixelConst:
                var code = argument.CodeConstant;
                document.CodeConstant = new CodeConstantDocument
                {
                    Source = code.SourceName, FirstRow = code.FirstRow, RowCount = code.RowCount
                };
                break;
            case MaterialShaderArgumentType.CodePixelSampler:
                document.CodeSampler = argument.CodeTextureSource;
                break;
            case MaterialShaderArgumentType.MaterialPixelSampler:
            case MaterialShaderArgumentType.MaterialVertexConst:
            case MaterialShaderArgumentType.MaterialPixelConst:
                document.NameHash = argument.MaterialNameHash;
                break;
            case MaterialShaderArgumentType.LiteralVertexConst:
            case MaterialShaderArgumentType.LiteralPixelConst:
                var literal = argument.LiteralConstant.GetValueOrDefault();
                document.LiteralBits =
                    [BitConverter.SingleToUInt32Bits(literal.X), BitConverter.SingleToUInt32Bits(literal.Y),
                     BitConverter.SingleToUInt32Bits(literal.Z), BitConverter.SingleToUInt32Bits(literal.W)];
                break;
            default:
                throw new InvalidDataException($"{path} has an unsupported type.");
        }
        return document;
    }

    private static MaterialShaderArgumentAsset ReadArgument(ArgumentDocument argument, string path, int version)
    {
        if (argument is null)
            throw new InvalidDataException($"{path} is missing.");
        MaterialShaderArgumentType type;
        int raw;
        if (version == 1)
        {
            if (argument.Type.ValueKind != JsonValueKind.Number || !argument.Type.TryGetUInt16(out ushort id) ||
                id > 7 || argument.ArgumentRaw is not int legacyRaw ||
                argument.CodeConstant is not null || argument.CodeSampler.HasValue || argument.NameHash.HasValue)
                throw new InvalidDataException($"{path} has an invalid version 1 argument.");
            type = (MaterialShaderArgumentType)id;
            raw = legacyRaw;
        }
        else
        {
            if (argument.Type.ValueKind != JsonValueKind.String ||
                !TryParseName(argument.Type.GetString(), out type) || argument.ArgumentRaw.HasValue)
                throw new InvalidDataException($"{path} has an invalid version 2 argument.");
            raw = (type, argument.CodeConstant, argument.CodeSampler, argument.NameHash) switch
            {
                (MaterialShaderArgumentType.CodeVertexConst or MaterialShaderArgumentType.CodePixelConst,
                    { } code, null, null) when TryParseName(code.Source, out MaterialConstantSource source) =>
                    new MaterialCodeConstantArgument(source, code.FirstRow, code.RowCount).Raw,
                (MaterialShaderArgumentType.CodePixelSampler, null, { } sampler, null) =>
                    unchecked((int)(uint)sampler),
                (MaterialShaderArgumentType.MaterialVertexConst or MaterialShaderArgumentType.MaterialPixelSampler or
                    MaterialShaderArgumentType.MaterialPixelConst, null, null, { } hash) => unchecked((int)hash),
                (MaterialShaderArgumentType.LiteralVertexConst or MaterialShaderArgumentType.LiteralPixelConst,
                    null, null, null) => 0,
                _ => throw new InvalidDataException($"{path} has an invalid payload for {type}.")
            };
        }

        MaterialShaderLiteralConstant? literal = null;
        if (type is MaterialShaderArgumentType.LiteralVertexConst or MaterialShaderArgumentType.LiteralPixelConst)
        {
            if (argument.LiteralBits is not { Length: 4 } bits)
                throw new InvalidDataException($"{path} requires four literal words.");
            literal = new MaterialShaderLiteralConstant(
                BitConverter.UInt32BitsToSingle(bits[0]), BitConverter.UInt32BitsToSingle(bits[1]),
                BitConverter.UInt32BitsToSingle(bits[2]), BitConverter.UInt32BitsToSingle(bits[3]));
        }
        else if (argument.LiteralBits is not null)
            throw new InvalidDataException($"{path} has an invalid literal payload.");
        return new MaterialShaderArgumentAsset(0, type, argument.Destination, raw, literal);
    }

    private static bool TryParseName<T>(string? text, out T value) where T : struct, Enum =>
        Enum.TryParse(text, out value) && text?.Contains(',') == false;

    private sealed class Document
    {
        public Document() { }

        public required string Format { get; init; }
        public required int Version { get; init; }
        public required string Name { get; init; }
        public required ushort Flags { get; init; }
        public required PassDocument[] Passes { get; init; }
    }

    private sealed class PassDocument
    {
        public PassDocument() { }

        public required byte PerPrimArgCount { get; init; }
        public required byte PerObjArgCount { get; init; }
        public required byte StableArgCount { get; init; }
        public required byte CustomSamplerFlags { get; init; }
        public required byte PrecompiledVertexShader { get; init; }
        public string? VertexShader { get; init; }
        public string? PixelShader { get; init; }
        public DeclarationDocument? VertexDeclaration { get; init; }
        public required ArgumentDocument[] Arguments { get; init; }
    }

    private sealed class DeclarationDocument
    {
        public DeclarationDocument() { }

        public required byte StreamCount { get; init; }
        public required byte HasOptionalSourceRaw { get; init; }
        public required RouteDocument[] Routing { get; init; }
    }

    private sealed class RouteDocument
    {
        public RouteDocument() { }

        public required byte Source { get; init; }
        public required byte Destination { get; init; }
    }

    private sealed class ArgumentDocument
    {
        public ArgumentDocument() { }

        public required JsonElement Type { get; init; }
        public required ushort Destination { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ArgumentRaw { get; init; } // Version 1 only.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CodeConstantDocument? CodeConstant { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MaterialTextureSource? CodeSampler { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint? NameHash { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint[]? LiteralBits { get; set; }
    }

    private sealed class CodeConstantDocument
    {
        public required string Source { get; init; }
        public required byte FirstRow { get; init; }
        public required byte RowCount { get; init; }
    }
}

internal static class NativeSourcePath
{
    public static string Resolve(string sourceDirectory, string directory, string name, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string root = Path.GetFullPath(sourceDirectory);
        string path = Path.GetFullPath(Path.Combine(root, directory, name + extension));
        string relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException($"Source path for '{name}' escapes the source directory.");
        return path;
    }
}
