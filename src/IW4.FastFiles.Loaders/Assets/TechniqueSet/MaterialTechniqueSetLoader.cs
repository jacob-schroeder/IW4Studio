using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.TechniqueSet;

public sealed class MaterialTechniqueSetLoader
{
    private const int TechniqueSlotCount = 37;
    private const int TechniqueSetSize = 0x9c;
    private const int TechniqueSize = 0x08;
    private const int PassSize = 0x18;
    private const int ShaderArgSize = 0x08;
    private const int LiteralFloat4Size = 0x10;
    private static readonly MaterialShaderLoader ShaderLoader = new();

    public MaterialTechniqueSetAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromAssetPointer(cursor, pointer, context, out _);
    }

    public MaterialTechniqueSetAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        out MaterialTechniqueSetAsset? incomingDefinition)
    {
        incomingDefinition = null;
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level Techset pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<MaterialTechniqueSetAsset>(
                pointer,
                MaterialTechniqueSetAsset.SerializedSize,
                "MaterialTechniqueSet");
            MaterialTechniqueSetAsset canonical = context.ResolveTechniqueSet(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level Techset pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical Techset asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed Techset pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical Techset has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException(
                $"Top-level Techset pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            MaterialTechniqueSetAsset techniqueSet = ReadTechniqueSet(cursor, rootAddress, context);
            incomingDefinition = techniqueSet;
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline Techset pointer has no destination cell.");
            MaterialTechniqueSetAsset canonical = context.DB_AddXAsset(techniqueSet, pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical Techset has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static MaterialTechniqueSetAsset ReadTechniqueSet(
        FastFileCursor cursor,
        XBlockAddress targetAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, TechniqueSetSize, out XBlockAddress rootAddress);
        if (rootAddress != targetAddress)
            throw new InvalidDataException($"MaterialTechniqueSet pointer patched to {targetAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<string> namePointer = ReadXStringPointer(rootCursor, context);
        var worldVertFormat = (MaterialWorldVertexFormat)rootCursor.ReadByte();
        rootCursor.Align(4);

        var techniquePointers = new XPointerReference[TechniqueSlotCount];
        for (int i = 0; i < techniquePointers.Length; i++)
            techniquePointers[i] = ReadDeferredCell(rootCursor, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != TechniqueSetSize)
            throw new InvalidDataException($"MaterialTechniqueSet consumed 0x{rootCursor.Offset:X} bytes instead of 0x{TechniqueSetSize:X}.");

        int inlineTechniqueCount = techniquePointers.Count(x => x.Raw == -1);
        int offsetTechniqueCount = techniquePointers.Count(x => x.Raw != 0 && x.Raw != -1 && x.Raw != -2);

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            string? name = ReadXString(cursor, namePointer, context);

            var slots = new MaterialTechniqueSlot[TechniqueSlotCount];
            for (int i = 0; i < techniquePointers.Length; i++)
            {
                XPointerReference techniquePointer = techniquePointers[i];
                MaterialTechniqueAsset? technique = ReadTechniquePointer(cursor, techniquePointer, context);
                slots[i] = new MaterialTechniqueSlot(
                    i,
                    techniquePointer.AsPointer<MaterialTechniqueAsset>(),
                    technique);
            }

            return new MaterialTechniqueSetAsset
            {
                Offset = offset,
                RuntimeAddress = rootAddress,
                NamePointer = namePointer,
                Name = name,
                WorldVertexFormat = worldVertFormat,
                TechniqueSlots = slots
            };
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static MaterialTechniqueAsset? ReadTechniquePointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<MaterialTechniqueAsset>(pointer, TechniqueSize, "MaterialTechnique");
            return context.ResolveMaterialTechnique(pointer);
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            throw new InvalidDataException(
                $"MaterialTechnique pointer 0x{unchecked((uint)pointer.Raw):X8} is neither null, packed direct, nor a supported inline sentinel.");
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        return ReadTechnique(cursor, context);
    }

    private static XPointerReference ReadDeferredCell(
        FastFileCursor cursor,
        XPointerResolutionMode resolutionMode)
    {
        int cellOffset = cursor.Offset;
        return XPointerReference.FromRaw(
            cursor.ReadInt32(),
            resolutionMode,
            cursor.AddressAt(cellOffset));
    }

    private static MaterialTechniqueAsset ReadTechnique(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, TechniqueSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<string> namePointer = ReadXStringPointer(rootCursor, context);
        ushort flags = rootCursor.ReadUInt16();
        ushort passCount = rootCursor.ReadUInt16();

        if (rootCursor.Offset != TechniqueSize)
            throw new InvalidDataException($"MaterialTechnique consumed 0x{rootCursor.Offset:X} bytes instead of 0x{TechniqueSize:X}.");


        var passes = new MaterialPassAsset[passCount];
        for (int i = 0; i < passes.Length; i++)
            passes[i] = ReadPassRoot(cursor, context);

        for (int i = 0; i < passes.Length; i++)
            ReadPassChildren(cursor, passes[i], context);

        string? name = ReadXString(cursor, namePointer, context);

        var technique = new MaterialTechniqueAsset
        {
            Offset = offset,
            DestinationAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            Flags = flags,
            PassCount = passCount,
            Passes = passes
        };
        return context.RegisterMaterialTechnique(rootAddress, technique);
    }

    private static MaterialPassAsset ReadPassRoot(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, PassSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<MaterialVertexDeclarationAsset> vertexDecl = context.PointerReader.ReadPointer<MaterialVertexDeclarationAsset>(rootCursor, XPointerResolutionMode.Direct);
        XPointer<MaterialShaderAsset> vertexShader = context.PointerReader.ReadPointer<MaterialShaderAsset>(rootCursor, XPointerResolutionMode.AliasCell);
        XPointer<MaterialShaderAsset> pixelShader = context.PointerReader.ReadPointer<MaterialShaderAsset>(rootCursor, XPointerResolutionMode.AliasCell);
        byte perPrimArgCount = rootCursor.ReadByte();
        byte perObjArgCount = rootCursor.ReadByte();
        byte stableArgCount = rootCursor.ReadByte();
        byte customSamplerFlags = rootCursor.ReadByte();
        byte precompiledIndex = rootCursor.ReadByte();
        rootCursor.Skip(3);
        XPointer<MaterialShaderArgumentAsset[]> args = context.PointerReader.ReadPointer<MaterialShaderArgumentAsset[]>(rootCursor, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != PassSize)
            throw new InvalidDataException($"MaterialPass consumed 0x{rootCursor.Offset:X} bytes instead of 0x{PassSize:X}.");


        return new MaterialPassAsset
        {
            Offset = offset,
            VertexDeclPointer = vertexDecl,
            VertexShaderPointer = vertexShader,
            PixelShaderPointer = pixelShader,
            PerPrimArgCount = perPrimArgCount,
            PerObjArgCount = perObjArgCount,
            StableArgCount = stableArgCount,
            CustomSamplerFlags = customSamplerFlags,
            PrecompiledIndex = precompiledIndex,
            ArgsPointer = args
        };
    }

    private static void ReadPassChildren(
        FastFileCursor cursor,
        MaterialPassAsset pass,
        DbLoadExecutionContext context)
    {
        pass.VertexDeclaration = ReadVertexDeclPointer(cursor, pass.VertexDeclPointer.Untyped, context);
        pass.VertexShader = ShaderLoader.LoadFromPointer(
            cursor,
            pass.VertexShaderPointer.Untyped,
            MaterialShaderKind.Vertex,
            context,
            out MaterialShaderAsset? incomingVertexShader);
        pass.IncomingVertexShader = incomingVertexShader;
        pass.PixelShader = ShaderLoader.LoadFromPointer(
            cursor,
            pass.PixelShaderPointer.Untyped,
            MaterialShaderKind.Pixel,
            context,
            out MaterialShaderAsset? incomingPixelShader);
        pass.IncomingPixelShader = incomingPixelShader;
        pass.Args = ReadShaderArgs(cursor, pass.ArgsPointer.Untyped, pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount, context);
    }

    private static MaterialVertexDeclarationAsset? ReadVertexDeclPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<MaterialVertexDeclarationAsset>(
                pointer,
                MaterialVertexDeclarationAsset.SerializedSize,
                "MaterialVertexDeclaration");
            return context.ResolveMaterialVertexDeclaration(pointer);
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            return null;

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] rootBytes = context.Blocks.Load(cursor, MaterialVertexDeclarationAsset.SerializedSize);
        if (insertCell is { } cell)
            context.Blocks.WriteInt32(cell, XPointerCodec.Encode(rootAddress));

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        byte streamCount = rootCursor.ReadByte();
        byte hasOptionalSource = rootCursor.ReadByte();
        var routing = new MaterialVertexStreamRouting[MaterialVertexDeclarationAsset.RoutingCount];
        for (int i = 0; i < routing.Length; i++)
            routing[i] = new MaterialVertexStreamRouting(rootCursor.ReadByte(), rootCursor.ReadByte());

        if (rootCursor.Offset != MaterialVertexDeclarationAsset.SerializedSize)
            throw new InvalidDataException($"MaterialVertexDeclaration consumed 0x{rootCursor.Offset:X} bytes instead of 0x{MaterialVertexDeclarationAsset.SerializedSize:X}.");


        var declaration = new MaterialVertexDeclarationAsset
        {
            DestinationAddress = rootAddress,
            StreamCount = streamCount,
            HasOptionalSource = hasOptionalSource,
            Routing = routing
        };
        return context.RegisterMaterialVertexDeclaration(
            rootAddress,
            declaration);
    }

    private static IReadOnlyList<MaterialShaderArgumentAsset> ReadShaderArgs(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative shader arg count {count}.");

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<MaterialShaderArgumentAsset[]>(pointer, checked(count * ShaderArgSize), "MaterialShaderArgument[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] argBytes = context.Blocks.Load(cursor, checked(count * ShaderArgSize), out XBlockAddress argAddress);
        var argCursor = new FastFileCursor(argBytes, argAddress);
        var args = new MaterialShaderArgumentAsset[count];
        var argumentPointers = new XPointerReference[count];

        for (int i = 0; i < args.Length; i++)
        {
            int offset = cursor.Offset - argBytes.Length + i * ShaderArgSize;
            int argStart = argCursor.Offset;
            var type = (MaterialShaderArgumentType)argCursor.ReadUInt16();
            ushort dest = argCursor.ReadUInt16();
            int valueCellOffset = argCursor.Offset;
            XPointerReference argumentPointer = XPointerReference.FromRaw(
                argCursor.ReadInt32(),
                XPointerResolutionMode.Direct,
                argCursor.AddressAt(valueCellOffset));

            if (argCursor.Offset - argStart != ShaderArgSize)
                throw new InvalidDataException($"MaterialShaderArgument consumed 0x{argCursor.Offset - argStart:X} bytes instead of 0x{ShaderArgSize:X}.");

            argumentPointers[i] = argumentPointer;
            args[i] = new MaterialShaderArgumentAsset(
                offset,
                type,
                dest,
                argumentPointer.Raw,
                LiteralConstant: null,
                ArgumentPointer: argumentPointer);
        }

        for (int i = 0; i < args.Length; i++)
        {
            MaterialShaderLiteralConstant? literal = null;
            XBlockAddress? literalDestinationAddress = null;
            XPointerReference argumentPointer = argumentPointers[i];
            if (args[i].Type is MaterialShaderArgumentType.LiteralVertexConst or MaterialShaderArgumentType.LiteralPixelConst)
            {
                literal = ReadLiteralFloat4Pointer(
                    cursor,
                    argumentPointer,
                    context,
                    out literalDestinationAddress);
            }

            args[i] = args[i] with
            {
                LiteralConstant = literal,
                LiteralDestinationAddress = literalDestinationAddress
            };
        }

        return args;
    }

    private static MaterialShaderLiteralConstant? ReadLiteralFloat4Pointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        out XBlockAddress? destinationAddress)
    {
        destinationAddress = null;
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.PackedAddress is { } packedAddress)
        {
            context.PointerReader.ValidateOffsetPointerRange<MaterialShaderLiteralConstant>(
                pointer,
                LiteralFloat4Size,
                "MaterialShaderLiteralConstant");
            destinationAddress = packedAddress;
            return ReadLiteralFloat4(context.Blocks.ReadBytes(packedAddress, LiteralFloat4Size), packedAddress);
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            return null;

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        XBlockAddress literalAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 16);
        byte[] literalBytes = context.Blocks.Load(cursor, LiteralFloat4Size);
        if (insertCell is { } cell)
            context.Blocks.WriteInt32(cell, XPointerCodec.Encode(literalAddress));

        destinationAddress = literalAddress;
        return ReadLiteralFloat4(literalBytes, literalAddress);
    }

    private static MaterialShaderLiteralConstant ReadLiteralFloat4(byte[] literalBytes, XBlockAddress literalAddress)
    {
        var literalCursor = new FastFileCursor(literalBytes, literalAddress);
        var literal = new MaterialShaderLiteralConstant(
            ReadSingle(literalCursor),
            ReadSingle(literalCursor),
            ReadSingle(literalCursor),
            ReadSingle(literalCursor));

        if (literalCursor.Offset != LiteralFloat4Size)
            throw new InvalidDataException($"MaterialShaderLiteralConstant consumed 0x{literalCursor.Offset:X} bytes instead of 0x{LiteralFloat4Size:X}.");

        return literal;
    }

    private static XPointer<string> ReadXStringPointer(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadPointer<string>(cursor, XPointerResolutionMode.Direct);
    }

    private static string? ReadXString(
        FastFileCursor cursor,
        XPointer<string> pointer,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.LoadXString(cursor, pointer);
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }
}
