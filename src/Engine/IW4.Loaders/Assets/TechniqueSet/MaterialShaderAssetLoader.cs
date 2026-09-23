using IW4.Loaders.Database;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.TechniqueSet;

internal sealed class MaterialShaderAssetLoader : XAssetLoader<MaterialShaderAsset>
{
    private readonly MaterialShaderKind _kind;

    public MaterialShaderAssetLoader(MaterialShaderKind kind)
        : base(MaterialShaderAsset.GetAssetType(kind), MaterialShaderAsset.GetSerializedSize(kind), GetDisplayName(kind))
    {
        _kind = kind;
    }

    protected override MaterialShaderAsset? HandleUnresolvedReference(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (!requireAsset)
            return null;

        return base.HandleUnresolvedReference(pointer, context, requireAsset);
    }

    protected override MaterialShaderAsset LoadInline(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        // The outer wrapper already owns the TEMP scope.
        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);
        return RegisterAsset(ReadInlineBody(cursor, pointer, context), providerRegistration, context);
    }

    protected override MaterialShaderAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        MaterialShaderKind kind = _kind;
        int sourceOffset = cursor.Offset;
        int rootSize = MaterialShaderAsset.GetSerializedSize(kind);
        byte[] rootBytes = context.Blocks.Load(cursor, rootSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"{GetDisplayName(kind)} pointer patched to {expectedRootAddress}, " +
                $"but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        // +0x00: XString name.
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);

        // +0x04: GfxShaderLoadDef programLoadDef (alias-cell pointer + byte count).
        XPointerReference dataPointer = context.PointerReader.ReadCell(
            rootCursor,
            XPointerResolutionMode.AliasCell);
        uint dataSize = rootCursor.ReadUInt32();

        // Pixel programs contain the 0x08-byte load definition plus 0x0C
        // trailing bytes. Vertex programs contain only the load definition.
        byte[] programBytes = rootCursor.ReadBytes(
            MaterialShaderAsset.GetProgramByteCount(kind));

        if (rootCursor.Offset != rootSize)
        {
            throw new InvalidDataException(
                $"{GetDisplayName(kind)} consumed 0x{rootCursor.Offset:X} bytes instead of 0x{rootSize:X}.");
        }

        string? name;
        byte[]? data;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            // The native body keeps LARGE active while the program/load-def
            // reader performs its nested TEMP push for bytecode.
            data = ReadShaderBytecode(
                cursor,
                dataPointer,
                dataSize,
                kind,
                context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new MaterialShaderAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            Kind = kind,
            NamePointer = namePointer,
            Name = name,
            DataPointer = dataPointer.AsPointer<MaterialShaderBytecode>(),
            DataSize = dataSize,
            ProgramBytes = programBytes,
            Data = data
        };
    }

    private static byte[]? ReadShaderBytecode(
        FastFileCursor cursor,
        XPointerReference pointer,
        uint dataSize,
        MaterialShaderKind kind,
        DbLoadExecutionContext context)
    {
        if (dataSize > int.MaxValue)
        {
            throw new InvalidDataException(
                $"{GetDisplayName(kind)} bytecode size 0x{dataSize:X} does not fit in this reader.");
        }

        // FUN_000FAFF8 (pixel) and FUN_000FB368 (vertex) both push stream 0
        // around the GfxShaderLoadDef pointer conversion and payload load.
        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            if (pointer.Type == PointerType.Null)
                return null;

            if (pointer.Type == PointerType.Offset)
            {
                context.PointerReader.ValidateOffsetPointerRange<MaterialShaderBytecode>(
                    pointer,
                    (int)dataSize,
                    $"{GetDisplayName(kind)}Bytecode");
                return ReadExistingBytecode(pointer, (int)dataSize, context);
            }

            if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            {
                throw new InvalidDataException(
                    $"{GetDisplayName(kind)} bytecode pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    $"has unsupported type {pointer.Type}.");
            }

            XBlockAddress? insertCell = pointer.Type == PointerType.Insert
                ? context.Blocks.AllocateInsertPointerCell()
                : null;
            XBlockAddress dataAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 16);
            byte[] data = context.Blocks.Load(cursor, (int)dataSize);

            if (insertCell is { } cell)
                context.Blocks.WriteInt32(cell, XPointerCodec.Encode(dataAddress));

            return data;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static byte[]? ReadExistingBytecode(
        XPointerReference pointer,
        int dataSize,
        DbLoadExecutionContext context)
    {
        XBlockAddress? dataAddress = pointer.ResolutionMode switch
        {
            XPointerResolutionMode.AliasCell => ResolveAliasTarget(pointer, context),
            _ => pointer.PackedAddress
        };
        return dataAddress is { } address
            ? context.Blocks.ReadBytes(address, dataSize)
            : null;
    }

    private static XBlockAddress? ResolveAliasTarget(
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        int aliasedRaw = context.PointerReader.ReadAliasCellRaw(pointer);
        if (aliasedRaw == 0)
            return null;

        return XPointerCodec.TryDecodeBlockAddress(aliasedRaw, out XBlockAddress address)
            ? address
            : throw new InvalidDataException(
                $"Shader bytecode alias cell resolved to non-block pointer 0x{unchecked((uint)aliasedRaw):X8}.");
    }

    internal static string GetDisplayName(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Pixel => "MaterialPixelShader",
        MaterialShaderKind.Vertex => "MaterialVertexShader",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
