using System.Buffers.Binary;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// Shared fixed-slot decoder/cache for one owning block stream. The caller
/// supplies its technique-name diagnostic policy.
/// </summary>
public sealed class MaterialTechniqueGraphCache
{
    private readonly IXAssetSourceMemory _blocks;
    private readonly MaterialGraphResolver _resolver;

    public MaterialTechniqueGraphCache(
        IXAssetSourceMemory blocks,
        Func<XPointerReference, string?> resolveTechniqueName)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(resolveTechniqueName);
        _blocks = blocks;
        _resolver = new MaterialGraphResolver(
            (_, pointer) => resolveTechniqueName(pointer));
    }

    public IReadOnlyList<MaterialTechniqueSlot> ResolveSlots(
        MaterialTechniqueSetAsset techniqueSet)
    {
        ArgumentNullException.ThrowIfNull(techniqueSet);
        return _resolver.ResolveTechniqueSlots(_blocks, techniqueSet);
    }
}

internal sealed class MaterialGraphResolver
{
    private const int ShaderArgumentSize = 0x08;

    private readonly XAssetPool? _assetPool;
    private readonly Func<IXAssetSourceMemory, XPointerReference, string?>
        _resolveTechniqueName;
    private readonly Dictionary<
        (IXAssetSourceMemory Blocks, XBlockAddress Address),
        MaterialTechniqueAsset?> _techniques = [];
    private readonly Dictionary<
        (IXAssetSourceMemory Blocks, XBlockAddress Address, MaterialShaderKind Kind),
        MaterialShaderAsset?> _shaders = [];

    public MaterialGraphResolver(XAssetPool assetPool)
    {
        ArgumentNullException.ThrowIfNull(assetPool);
        _assetPool = assetPool;
        _resolveTechniqueName = static (blocks, pointer) =>
            ReadCString(blocks, pointer.Raw);
    }

    internal MaterialGraphResolver(
        Func<IXAssetSourceMemory, XPointerReference, string?> resolveTechniqueName)
    {
        ArgumentNullException.ThrowIfNull(resolveTechniqueName);
        _resolveTechniqueName = resolveTechniqueName;
    }

    public MaterialTechniqueSetAsset? GetTechniqueSet(MaterialAsset material)
    {
        if (material.TechniqueSet is not null)
            return material.TechniqueSet;

        return _assetPool!.TryResolve(
            material.TechniqueSetPointer.Raw,
            XAssetType.Techset,
            out MaterialTechniqueSetAsset? techset)
            ? techset
            : null;
    }

    public MaterialTechniqueAsset? GetTechnique(MaterialAsset material, int slotIndex)
    {
        MaterialTechniqueSetAsset? techset = GetTechniqueSet(material);
        MaterialTechniqueSlot? slot = techset?.TechniqueSlots.FirstOrDefault(candidate => candidate.Index == slotIndex);
        if (slot is null)
            return null;

        if (!_assetPool!.TryGetEntry(techset!, out XAssetPoolEntry? entry) ||
            entry.SourceBlocks is not { } blocks)
            return slot.Technique;

        if (slot.Technique is { } direct)
        {
            HydrateTechniqueShaders(direct, blocks);
            return direct;
        }

        return ResolveTechnique(blocks, slot);
    }

    internal IReadOnlyList<MaterialTechniqueSlot> ResolveTechniqueSlots(
        IXAssetSourceMemory blocks,
        MaterialTechniqueSetAsset techniqueSet)
    {
        var resolved =
            new MaterialTechniqueSlot[techniqueSet.TechniqueSlots.Count];
        for (int index = 0; index < resolved.Length; index++)
        {
            MaterialTechniqueSlot slot = techniqueSet.TechniqueSlots[index];
            MaterialTechniqueAsset? technique = slot.Technique ??
                ResolveTechnique(blocks, slot);
            resolved[index] = technique is null ||
                              ReferenceEquals(technique, slot.Technique)
                ? slot
                : slot with { Technique = technique };
        }

        return resolved;
    }

    private MaterialTechniqueAsset? ResolveTechnique(
        IXAssetSourceMemory blocks,
        MaterialTechniqueSlot slot)
    {
        if (slot.Technique is { } direct)
            return direct;

        // MaterialTechniqueSetLoader proves these 37 cells use direct offset
        // resolution. Do not reinterpret a different pointer mode here.
        if (slot.Pointer.Type != PointerType.Offset ||
            slot.Pointer.ResolutionMode != XPointerResolutionMode.Direct ||
            slot.Pointer.PackedAddress is not { } address)
        {
            return null;
        }

        var key = (blocks, address);
        if (_techniques.TryGetValue(key, out MaterialTechniqueAsset? cached))
            return cached;

        MaterialTechniqueAsset? technique = ReadTechnique(blocks, address);
        if (technique is not null || _assetPool is not null)
            _techniques[key] = technique;
        return technique;
    }

    private MaterialTechniqueAsset? ReadTechnique(IXAssetSourceMemory blocks, XBlockAddress address)
    {
        byte[] root;
        try
        {
            root = blocks.ReadBytes(address, MaterialTechniqueAsset.SerializedSize);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        XPointer<string> namePointer = ReadPointer<string>(
            root, 0, XPointerResolutionMode.Direct, address);
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(root.AsSpan(4, 2));
        ushort passCount = BinaryPrimitives.ReadUInt16BigEndian(root.AsSpan(6, 2));
        if (passCount > 128)
            return null;

        var passes = new MaterialPassAsset[passCount];
        for (int index = 0; index < passes.Length; index++)
        {
            XBlockAddress passAddress = address.Add(
                MaterialTechniqueAsset.SerializedSize +
                index * MaterialPassAsset.SerializedSize);
            byte[] pass;
            try
            {
                pass = blocks.ReadBytes(passAddress, MaterialPassAsset.SerializedSize);
            }
            catch (InvalidDataException)
            {
                return null;
            }

            passes[index] = new MaterialPassAsset
            {
                Offset = passAddress.Offset,
                VertexDeclPointer = ReadPointer<MaterialVertexDeclarationAsset>(
                    pass, 0, XPointerResolutionMode.Direct, passAddress),
                VertexShaderPointer = ReadPointer<MaterialShaderAsset>(
                    pass, 4, XPointerResolutionMode.AliasCell, passAddress),
                PixelShaderPointer = ReadPointer<MaterialShaderAsset>(
                    pass, 8, XPointerResolutionMode.AliasCell, passAddress),
                PerPrimArgCount = pass[12],
                PerObjArgCount = pass[13],
                StableArgCount = pass[14],
                CustomSamplerFlags = pass[15],
                PrecompiledIndex = pass[16],
                ArgsPointer = ReadPointer<MaterialShaderArgumentAsset[]>(
                    pass, 20, XPointerResolutionMode.Direct, passAddress)
            };
        }

        var result = new MaterialTechniqueAsset
        {
            Offset = address.Offset,
            NamePointer = namePointer,
            Name = _resolveTechniqueName(blocks, namePointer.Untyped),
            Flags = flags,
            PassCount = passCount,
            Passes = passes
        };
        if (_assetPool is not null)
            HydrateTechniqueShaders(result, blocks);
        return result;
    }

    private static XPointer<T> ReadPointer<T>(
        byte[] bytes,
        int offset,
        XPointerResolutionMode mode,
        XBlockAddress rootAddress) =>
        new(
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4)),
            mode,
            rootAddress.Add(offset));

    private void HydrateTechniqueShaders(MaterialTechniqueAsset technique, IXAssetSourceMemory blocks)
    {
        foreach (MaterialPassAsset pass in technique.Passes)
        {
            pass.VertexShader ??= ReadShader(blocks, pass.VertexShaderPointer.Untyped, MaterialShaderKind.Vertex);
            pass.PixelShader ??= ReadShader(blocks, pass.PixelShaderPointer.Untyped, MaterialShaderKind.Pixel);
            if (pass.Args.Count == 0)
                pass.Args = ReadShaderArguments(blocks, pass);
        }
    }

    private static IReadOnlyList<MaterialShaderArgumentAsset> ReadShaderArguments(
        IXAssetSourceMemory blocks,
        MaterialPassAsset pass)
    {
        int count = pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount;
        if (count == 0 || pass.ArgsPointer.PackedAddress is not { } address)
            return [];

        byte[] bytes;
        try
        {
            bytes = blocks.ReadBytes(address, checked(count * ShaderArgumentSize));
        }
        catch (InvalidDataException)
        {
            return [];
        }

        var arguments = new MaterialShaderArgumentAsset[count];
        for (int index = 0; index < arguments.Length; index++)
        {
            int offset = index * ShaderArgumentSize;
            var type = (MaterialShaderArgumentType)BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            ushort destination = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2, 2));
            int raw = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4, 4));
            MaterialShaderLiteralConstant? literal = type is
                MaterialShaderArgumentType.LiteralVertexConst or
                MaterialShaderArgumentType.LiteralPixelConst
                ? ReadLiteralConstant(blocks, raw)
                : null;
            arguments[index] = new MaterialShaderArgumentAsset(
                address.Offset + offset,
                type,
                destination,
                raw,
                literal);
        }

        return arguments;
    }

    private static MaterialShaderLiteralConstant? ReadLiteralConstant(
        IXAssetSourceMemory blocks,
        int raw)
    {
        if (!XPointerCodec.TryDecodeBlockAddress(raw, out XBlockAddress address))
            return null;

        try
        {
            byte[] bytes = blocks.ReadBytes(address, 16);
            return new MaterialShaderLiteralConstant(
                ReadSingleBigEndian(bytes.AsSpan(0, 4)),
                ReadSingleBigEndian(bytes.AsSpan(4, 4)),
                ReadSingleBigEndian(bytes.AsSpan(8, 4)),
                ReadSingleBigEndian(bytes.AsSpan(12, 4)));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static float ReadSingleBigEndian(ReadOnlySpan<byte> bytes)
    {
        int raw = BinaryPrimitives.ReadInt32BigEndian(bytes);
        return BitConverter.Int32BitsToSingle(raw);
    }

    private MaterialShaderAsset? ReadShader(
        IXAssetSourceMemory blocks,
        XPointerReference pointer,
        MaterialShaderKind kind)
    {
        XAssetType assetType = kind == MaterialShaderKind.Vertex
            ? XAssetType.VertexShader
            : XAssetType.PixelShader;
        XAssetPool assetPool = _assetPool!;
        if (assetPool.TryResolve(pointer.Raw, assetType, out MaterialShaderAsset? canonical))
            return canonical;

        if (pointer.ResolutionMode == XPointerResolutionMode.AliasCell &&
            pointer.PackedAddress is { } aliasCell)
        {
            try
            {
                int aliasRaw = blocks.ReadInt32(aliasCell);
                if (assetPool.TryResolve(aliasRaw, assetType, out canonical))
                    return canonical;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        XBlockAddress? address = ResolveAddress(blocks, pointer);
        if (address is null)
            return null;

        var key = (blocks, address.Value, kind);
        if (_shaders.TryGetValue(key, out MaterialShaderAsset? cached))
            return cached;

        int size = kind == MaterialShaderKind.Vertex
            ? MaterialShaderAsset.VertexShaderSerializedSize
            : MaterialShaderAsset.PixelShaderSerializedSize;
        MaterialShaderAsset? shader;
        try
        {
            byte[] root = blocks.ReadBytes(address.Value, size);
            int nameRaw = BinaryPrimitives.ReadInt32BigEndian(root.AsSpan(0, 4));
            shader = new MaterialShaderAsset
            {
                Offset = address.Value.Offset,
                Kind = kind,
                Name = ReadCString(blocks, nameRaw)
            };
        }
        catch (InvalidDataException)
        {
            shader = null;
        }

        _shaders[key] = shader;
        return shader;
    }

    private static XBlockAddress? ResolveAddress(IXAssetSourceMemory blocks, XPointerReference pointer)
    {
        if (pointer.Type != PointerType.Offset || pointer.PackedAddress is not { } address)
            return null;

        if (pointer.ResolutionMode != XPointerResolutionMode.AliasCell)
            return address;

        try
        {
            int aliasedRaw = blocks.ReadInt32(address);
            return XPointerCodec.TryDecodeBlockAddress(aliasedRaw, out XBlockAddress aliased)
                ? aliased
                : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string? ReadCString(IXAssetSourceMemory blocks, int raw)
    {
        if (!XPointerCodec.TryDecodeBlockAddress(raw, out XBlockAddress address))
            return null;

        try
        {
            return blocks.ReadCString(address);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
