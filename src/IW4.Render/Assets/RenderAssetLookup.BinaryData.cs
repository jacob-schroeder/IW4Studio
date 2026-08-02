using IW4.Assets.Zone;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.IO;
using System.Text;

namespace IW4.Render.Assets;

public sealed partial class RenderAssetLookup
{
    private void CollectMaterialImages(MaterialAsset material)
    {
        foreach (MaterialTextureDef texture in material.Textures)
        {
            AddImage(texture.Image, texture.DataPointer.CellAddress);
            AddImage(texture.Water?.Image, texture.Water?.ImagePointer.CellAddress);
        }
    }

    private MaterialVertexDeclarationAsset ReadVertexDeclaration(XBlockAddress address)
    {
        byte streamCount = _blocks.ReadByte(address);
        byte hasOptionalSource = _blocks.ReadByte(address.Add(1));
        var routing = new MaterialVertexStreamRouting[MaterialVertexDeclarationAsset.RoutingCount];
        for (int i = 0; i < routing.Length; i++)
        {
            XBlockAddress routeAddress = address.Add(2 + i * 2);
            routing[i] = new MaterialVertexStreamRouting(
                _blocks.ReadByte(routeAddress),
                _blocks.ReadByte(routeAddress.Add(1)));
        }

        return new MaterialVertexDeclarationAsset
        {
            DestinationAddress = address,
            StreamCount = streamCount,
            HasOptionalSource = hasOptionalSource,
            Routing = routing
        };
    }

    private MaterialShaderLiteralConstant? ReadLiteralFloat4(XPointerReference pointer)
    {
        if (ResolveAddress(pointer) is not { } address)
            return null;

        byte[] bytes;
        try
        {
            bytes = _blocks.ReadBytes(address, LiteralFloat4Size);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var cursor = new FastFileCursor(bytes, address);
        return new MaterialShaderLiteralConstant(
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor));
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private MaterialShaderAsset? ResolveShader(
        XPointerReference pointer,
        MaterialShaderKind kind,
        Dictionary<XBlockAddress, MaterialShaderAsset> cache)
    {
        if (ResolveAliasCellOwner(pointer, GetShaderAliasCellCache(kind)) is { } ownedShader)
            return ownedShader;

        if (ResolveAddress(pointer) is not { } address)
            return null;

        if (cache.TryGetValue(address, out MaterialShaderAsset? shader))
            return shader;

        // TEMP is a loader scratch destination and has been rewound by the time
        // render lookup runs. Never turn its current bytes into a shader binding;
        // shared shaders resolve through their persistent alias owner cell above.
        if (address.BlockType == XFileBlockType.TEMP)
            return null;

        shader = ReadShader(address, kind);
        if (shader is not null)
            cache[address] = shader;
        return shader;
    }

    internal static MaterialShaderAsset? ResolveAliasCellOwner(
        XPointerReference pointer,
        IReadOnlyDictionary<XBlockAddress, MaterialShaderAsset> aliasCellCache)
    {
        return pointer.ResolutionMode == XPointerResolutionMode.AliasCell &&
               pointer.PackedAddress is { } aliasCell &&
               aliasCellCache.TryGetValue(aliasCell, out MaterialShaderAsset? shader)
            ? shader
            : null;
    }

    private Dictionary<XBlockAddress, MaterialShaderAsset> GetShaderAliasCellCache(
        MaterialShaderKind kind) =>
        kind == MaterialShaderKind.Vertex
            ? _vertexShadersByAliasCell
            : _pixelShadersByAliasCell;

    private MaterialShaderAsset? ReadShader(XBlockAddress address, MaterialShaderKind kind)
    {
        int rootSize = kind == MaterialShaderKind.Vertex
            ? MaterialShaderAsset.VertexShaderSerializedSize
            : MaterialShaderAsset.PixelShaderSerializedSize;
        byte[] rootBytes;
        try
        {
            rootBytes = _blocks.ReadBytes(address, rootSize);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var rootCursor = new IW4.Runtime.IO.FastFileCursor(rootBytes, address);
        XPointer<string> namePointer = new(rootCursor.ReadInt32(), XPointerResolutionMode.Direct, address);
        XBlockAddress dataPointerCell = address.Add(4);
        int dataPointerRaw = rootCursor.ReadInt32();
        uint dataSize = rootCursor.ReadUInt32();
        byte[] programBytes = kind == MaterialShaderKind.Pixel
            ? rootCursor.ReadBytes(0x0c)
            : [];

        if (dataSize > int.MaxValue)
            return null;

        XPointerReference dataPointer = XPointerReference.FromRaw(dataPointerRaw, XPointerResolutionMode.AliasCell, dataPointerCell);
        byte[]? data = null;
        if (ResolveAddress(dataPointer) is { } dataAddress)
        {
            try
            {
                data = _blocks.ReadBytes(dataAddress, (int)dataSize);
            }
            catch (InvalidDataException)
            {
                data = null;
            }
        }

        return new MaterialShaderAsset
        {
            Offset = address.Offset,
            RuntimeAddress = XRuntimeAddress.FromBlock(address),
            Kind = kind,
            NamePointer = namePointer,
            Name = ReadCString(namePointer.Untyped),
            DataPointer = dataPointer.AsPointer<MaterialShaderBytecode>(),
            DataSize = dataSize,
            ProgramBytes = programBytes,
            Data = data
        };
    }

    private void HydratePrecompiledShaderNames(XBlockAddress techniqueAddress, IReadOnlyList<MaterialPassAsset> passes)
    {
        XBlockAddress cursor = techniqueAddress.Add(
            MaterialTechniqueAsset.SerializedSize +
            checked(passes.Count * MaterialPassAsset.SerializedSize));
        foreach (MaterialPassAsset pass in passes)
        {
            if (ResolveAddress(pass.ArgsPointer.Untyped) is not { } argsAddress ||
                argsAddress.BlockType != cursor.BlockType ||
                argsAddress.Offset < cursor.Offset)
            {
                continue;
            }

            IReadOnlyList<string> shaderNames = argsAddress.Offset > cursor.Offset
                ? ReadShaderNames(cursor, argsAddress.Offset - cursor.Offset)
                : [];
            HydratePrecompiledShaderReferences(
                pass,
                shaderNames,
                _assetPool);

            int argBytes = checked((pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount) * ShaderArgSize);
            cursor = argsAddress.Add(argBytes);
        }
    }

    /// <summary>
    /// Recovers the shader objects referenced by a materialized pass without
    /// reinterpreting its patched runtime pointer as serialized source data.
    /// Typed active pool providers are authoritative. The serialized name gap
    /// is used only for one supported stage shape: two reference names written
    /// in loader child order (vertex, then pixel) while neither stage otherwise
    /// has an executable or canonical provider.
    /// </summary>
    internal static void HydratePrecompiledShaderReferences(
        MaterialPassAsset pass,
        IReadOnlyList<string> shaderNames,
        XAssetPool? assetPool)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(shaderNames);

        bool vertexProviderResolved = TryResolveActiveShaderProvider(
            assetPool,
            pass.VertexShaderPointer.Raw,
            XAssetType.VertexShader,
            MaterialShaderKind.Vertex,
            out MaterialShaderAsset? vertexProvider);
        if (vertexProviderResolved)
            pass.VertexShader = vertexProvider;

        bool pixelProviderResolved = TryResolveActiveShaderProvider(
            assetPool,
            pass.PixelShaderPointer.Raw,
            XAssetType.PixelShader,
            MaterialShaderKind.Pixel,
            out MaterialShaderAsset? pixelProvider);
        if (pixelProviderResolved)
            pass.PixelShader = pixelProvider;

        bool vertexStageResolved = vertexProviderResolved ||
            pass.VertexShader?.Data is { Length: > 0 };
        bool pixelStageResolved = pixelProviderResolved ||
            pass.PixelShader?.Data is { Length: > 0 };
        if (vertexStageResolved ||
            pixelStageResolved ||
            shaderNames.Count != 2 ||
            !XAssetPool.IsReferenceName(shaderNames[0]) ||
            !XAssetPool.IsReferenceName(shaderNames[1]))
        {
            return;
        }

        pass.VertexShader = CreateNamedShader(
            MaterialShaderKind.Vertex,
            shaderNames[0]);
        pass.PixelShader = CreateNamedShader(
            MaterialShaderKind.Pixel,
            shaderNames[1]);
    }

    private static bool TryResolveActiveShaderProvider(
        XAssetPool? assetPool,
        int rawPointer,
        XAssetType expectedType,
        MaterialShaderKind expectedKind,
        out MaterialShaderAsset? shader)
    {
        shader = null;
        if (assetPool?.TryResolve(
                rawPointer,
                expectedType,
                out MaterialShaderAsset? candidate) != true ||
            candidate is null ||
            candidate.Kind != expectedKind ||
            !TryGetActiveProviderIdentity(
                candidate,
                assetPool,
                out _))
        {
            return false;
        }

        shader = candidate;
        return true;
    }

    private IReadOnlyList<string> ReadShaderNames(XBlockAddress address, int byteCount)
    {
        if (byteCount <= 0)
            return [];

        var names = new List<string>();
        int offset = 0;
        while (offset < byteCount)
        {
            if (ReadBoundedCString(address.Add(offset), byteCount - offset) is { } value &&
                IsShaderName(value))
            {
                names.Add(value);
                offset += value.Length + 1;
                continue;
            }

            offset++;
        }

        return names;
    }

    private static MaterialShaderAsset CreateNamedShader(MaterialShaderKind kind, string name)
    {
        return new MaterialShaderAsset
        {
            Kind = kind,
            Name = XAssetPool.CanonicalizeName(name),
            DataSize = 0,
            ProgramBytes = [],
            Data = null
        };
    }

    private string? ReadCString(XPointerReference pointer)
    {
        return ResolveAddress(pointer) is { } address
            ? ReadBoundedCString(address, 256)
            : null;
    }

    private string? ReadBoundedCString(XBlockAddress address, int maxBytes)
    {
        if (maxBytes <= 0)
            return null;

        Span<byte> scratch = stackalloc byte[Math.Min(maxBytes, 256)];
        int count = 0;
        try
        {
            while (count < scratch.Length)
            {
                byte value = _blocks.ReadByte(address.Add(count));
                if (value == 0)
                    return count == 0 ? null : Encoding.ASCII.GetString(scratch[..count]);
                if (value < 0x20 || value > 0x7e)
                    return null;

                scratch[count++] = value;
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return null;
    }

    private static bool IsShaderName(string value)
    {
        return value.EndsWith(".hlsl", StringComparison.Ordinal);
    }

    private void CollectGfxWorldMaterials(GfxWorldAsset gfxWorld, XAssetPool? assetPool)
    {
        foreach (MaterialMemory row in gfxWorld.MaterialMemory)
        {
            MaterialAsset? material = row.Material ?? ResolveMaterial(row.MaterialPointer);
            if (material is not null)
            {
                MaterialAsset? activeMaterial = AddPooledMaterialGraph(material, assetPool);
                AddMaterial(activeMaterial, row.MaterialPointer.CellAddress);
            }
        }

        foreach (GfxStaticModelDrawInst drawInst in gfxWorld.Dpvs.SModelDrawInsts)
        {
            if (drawInst.Model is not { } model)
                continue;
            foreach (MaterialAsset? material in model.Materials)
            {
                if (material is not null)
                    AddPooledMaterialGraph(material, assetPool);
            }
        }
    }

    private void CollectGfxWorldImages(GfxWorldAsset gfxWorld)
    {
        AddImage(gfxWorld.OutdoorImage);

        foreach (GfxSky sky in gfxWorld.Skies)
            AddImage(sky.SkyImage);

        foreach (GfxImageAsset? image in gfxWorld.WorldDraw.ReflectionProbeImages)
            AddImage(image);

        foreach (GfxLightmapArray lightmap in gfxWorld.WorldDraw.Lightmaps)
        {
            AddImage(lightmap.Primary);
            AddImage(lightmap.Secondary);
        }
    }

    private XBlockAddress? ResolveAddress(XPointerReference pointer)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset && pointer.ResolutionMode == XPointerResolutionMode.AliasCell)
        {
            if (pointer.PackedAddress is not { } cell)
                return null;

            try
            {
                int aliasedRaw = _blocks.ReadInt32(cell);
                return XPointerCodec.TryDecodeBlockAddress(aliasedRaw, out XBlockAddress address)
                    ? address
                    : null;
            }
            catch (InvalidDataException)
            {
                // The pointer can belong to a canonical asset loaded from an
                // earlier dependency zone with its own block streams.
                return null;
            }
        }

        return pointer.Type == PointerType.Offset
            ? pointer.PackedAddress
            : null;
    }
}
