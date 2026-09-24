using System.Text.Json;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Formats.SourceFormat.Technique;

/// <summary>Exchanges the native PS3 material-technique graph as versioned source.</summary>
public sealed class TechniqueExchange
{
    private const string Format = "iw4-ps3-technique";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
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
            Version = 1,
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
        if (document.Format != Format || document.Version != 1 || document.Name != name)
            throw new InvalidDataException($"Technique '{name}' has an unsupported format, version, or name.");
        if (document.Passes is null || document.Passes.Length > ushort.MaxValue)
            throw new InvalidDataException($"Technique '{name}' has an invalid pass count.");
        return new MaterialTechniqueAsset
        {
            Name = name,
            Flags = (MaterialTechniqueFlags)document.Flags,
            PassCount = checked((ushort)document.Passes.Length),
            Passes = document.Passes.Select((pass, index) =>
                ReadPass(pass, name, index)).ToArray()
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

    private static MaterialPassAsset ReadPass(PassDocument pass, string name, int index)
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
                ReadArgument(argument, $"{path} argument {argumentIndex}")).ToArray()
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
        if ((ushort)argument.Type > 7)
            throw new InvalidDataException($"Technique '{name}' pass {passIndex} argument {argumentIndex} has an unsupported type.");
        bool isLiteral = argument.Type is MaterialShaderArgumentType.LiteralVertexConst or
            MaterialShaderArgumentType.LiteralPixelConst;
        if (isLiteral != argument.LiteralConstant.HasValue)
            throw new InvalidDataException($"Technique '{name}' pass {passIndex} argument {argumentIndex} has an invalid literal value.");
        MaterialShaderLiteralConstant literal = argument.LiteralConstant.GetValueOrDefault();
        return new ArgumentDocument
        {
            Type = (ushort)argument.Type,
            Destination = argument.Dest,
            ArgumentRaw = isLiteral ? 0 : argument.ArgumentRaw,
            LiteralBits = isLiteral ?
                [BitConverter.SingleToUInt32Bits(literal.X), BitConverter.SingleToUInt32Bits(literal.Y),
                 BitConverter.SingleToUInt32Bits(literal.Z), BitConverter.SingleToUInt32Bits(literal.W)] : null
        };
    }

    private static MaterialShaderArgumentAsset ReadArgument(ArgumentDocument argument, string path)
    {
        if (argument is null || argument.Type > 7)
            throw new InvalidDataException($"{path} has an unsupported argument type.");
        var type = (MaterialShaderArgumentType)argument.Type;
        bool isLiteral = type is MaterialShaderArgumentType.LiteralVertexConst or
            MaterialShaderArgumentType.LiteralPixelConst;
        if (isLiteral ? argument.LiteralBits is not { Length: 4 } : argument.LiteralBits is not null)
            throw new InvalidDataException($"{path} has an invalid literal payload.");
        MaterialShaderLiteralConstant? literal = isLiteral
            ? new MaterialShaderLiteralConstant(
                BitConverter.UInt32BitsToSingle(argument.LiteralBits![0]),
                BitConverter.UInt32BitsToSingle(argument.LiteralBits[1]),
                BitConverter.UInt32BitsToSingle(argument.LiteralBits[2]),
                BitConverter.UInt32BitsToSingle(argument.LiteralBits[3]))
            : null;
        return new MaterialShaderArgumentAsset(0, type, argument.Destination,
            argument.ArgumentRaw, literal);
    }

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

        public required ushort Type { get; init; }
        public required ushort Destination { get; init; }
        public required int ArgumentRaw { get; init; }
        public uint[]? LiteralBits { get; init; }
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
