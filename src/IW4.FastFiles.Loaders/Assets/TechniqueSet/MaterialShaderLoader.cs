using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.TechniqueSet;

/// <summary>
/// Reproduces the shared PS3 pointer-wrapper and body shapes for the
/// MaterialPixelShader and MaterialVertexShader XAsset families.
/// </summary>
public sealed class MaterialShaderLoader
{
    public MaterialShaderAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        MaterialShaderKind kind,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(
                cursor,
                pointer,
                kind,
                context,
                requireAsset: true)
            ?? throw new InvalidDataException($"Top-level {GetDisplayName(kind)} pointer resolved to null.");
    }

    public MaterialShaderAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        MaterialShaderKind kind,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(
            cursor,
            pointer,
            kind,
            context,
            requireAsset: false);
    }

    private static MaterialShaderAsset? LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        MaterialShaderKind kind,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        XAssetType assetType = GetAssetType(kind);
        int rootSize = GetRootSize(kind);

        // The native pointer wrappers push stream 0 before resolving null,
        // packed, inline, or insert-pointer cases. Nested calls can originate
        // in LARGE while their shader roots remain TEMP staging allocations.
        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            if (pointer.Type == PointerType.Null)
            {
                if (requireAsset)
                    throw new InvalidDataException($"Top-level {GetDisplayName(kind)} pointer is null.");

                return null;
            }

            if (pointer.Type == PointerType.Offset)
            {
                context.PointerReader.ValidateOffsetPointerRange<MaterialShaderAsset>(
                    pointer,
                    rootSize,
                    GetDisplayName(kind));
                MaterialShaderAsset? canonical = context.ResolveCanonicalAsset<MaterialShaderAsset>(
                    pointer,
                    assetType);
                if (canonical is null)
                {
                    if (!requireAsset)
                        return null;

                    throw new InvalidDataException(
                        $"Top-level {GetDisplayName(kind)} pointer " +
                        $"0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical {assetType} asset.");
                }

                PatchCanonicalPointerCell(pointer, canonical, context);
                return canonical;
            }

            if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            {
                throw new InvalidDataException(
                    $"{GetDisplayName(kind)} pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    $"has unsupported type {pointer.Type}.");
            }

            // Provider occurrence capture owns the optional durable insert
            // cell and preserves this source pointer's TEMP lifetime.
            ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            MaterialShaderAsset shader = ReadShader(cursor, rootAddress, kind, context);
            MaterialShaderAsset canonicalShader = context.DB_AddXAsset(
                assetType,
                shader.Name,
                shader,
                providerRegistration);

            return canonicalShader;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static MaterialShaderAsset ReadShader(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        MaterialShaderKind kind,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        int rootSize = GetRootSize(kind);
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
            XPointerOffsetMode.AliasCell);
        uint dataSize = rootCursor.ReadUInt32();

        // Pixel programs contain the 0x08-byte load definition plus 0x0C
        // trailing bytes. Vertex programs contain only the load definition.
        byte[] programBytes = kind == MaterialShaderKind.Pixel
            ? rootCursor.ReadBytes(0x0c)
            : [];

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

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        MaterialShaderAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException($"Packed {GetDisplayName(canonical.Kind)} pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException($"Canonical {GetDisplayName(canonical.Kind)} has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }

    private static XAssetType GetAssetType(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Pixel => XAssetType.PixelShader,
        MaterialShaderKind.Vertex => XAssetType.VertexShader,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static int GetRootSize(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Pixel => MaterialShaderAsset.PixelShaderSerializedSize,
        MaterialShaderKind.Vertex => MaterialShaderAsset.VertexShaderSerializedSize,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string GetDisplayName(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Pixel => "MaterialPixelShader",
        MaterialShaderKind.Vertex => "MaterialVertexShader",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
