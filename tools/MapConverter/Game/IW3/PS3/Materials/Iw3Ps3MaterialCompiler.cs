using System.Buffers.Binary;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using MapConverter.Game.IW3.PC.Materials;
using MapConverter.Game.IW3.PS3.Techniques;

namespace MapConverter.Game.IW3.PS3.Materials;

internal sealed class Iw3Ps3MaterialCompiler(
    Iw3Ps3TechniqueCompiler techniques,
    Func<Iw3Ps3MaterialSource, Iw3Ps3MaterialSource> resolveMaterial,
    Func<string, GfxImageAsset> resolveImage)
{
    private readonly Func<Iw3Ps3MaterialSource, Iw3Ps3MaterialSource> _resolveMaterial =
        resolveMaterial ?? throw new ArgumentNullException(nameof(resolveMaterial));
    private readonly Func<string, GfxImageAsset> _resolveImage =
        resolveImage ?? throw new ArgumentNullException(nameof(resolveImage));
    private readonly Dictionary<Iw3Ps3MaterialSource, MaterialAsset> _materials =
        new(ReferenceEqualityComparer.Instance);

    internal MaterialAsset Compile(Iw3Ps3MaterialSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Name.StartsWith(','))
        {
            Iw3Ps3MaterialSource resolved = _resolveMaterial(source) ??
                throw new InvalidDataException($"IW3 PS3 material '{source.Name}' is unresolved.");
            if (resolved.Name.StartsWith(',') ||
                !string.Equals(resolved.Name, source.Name[1..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"IW3 PS3 material '{source.Name}' resolved to an incompatible definition.");
            source = resolved;
        }
        if (_materials.TryGetValue(source, out MaterialAsset? cached))
            return cached;
        ReadOnlySpan<byte> root = source.Root.Span;
        if (root.Length != 0x80 || string.IsNullOrWhiteSpace(source.Name) || source.Name[0] == ',')
            throw new InvalidDataException("An IW3 PS3 material requires an owned 0x80-byte root.");
        if (source.Textures.Count != root[0x32] ||
            source.Constants.Count != root[0x33] ||
            source.States.Count != root[0x34])
            throw new InvalidDataException($"IW3 PS3 material '{source.Name}' has inconsistent child counts.");

        Iw3Ps3TechniqueSetSource sourceTechniqueSet = source.TechniqueSet ??
            throw new InvalidDataException(
                $"IW3 PS3 material '{source.Name}' has no source technique set.");
        MaterialTechniqueSetAsset techniqueSet = techniques.Compile(sourceTechniqueSet).TechniqueSet;
        var entries = new MaterialStateBitsEntry[MaterialAsset.TechniqueSlotCount];
        var inlineStates = new ushort[MaterialAsset.TechniqueSlotCount];
        for (int targetIndex = 0; targetIndex < entries.Length; targetIndex++)
        {
            int? sourceIndex = Iw3Ps3TechniqueCompiler.SourceSlotIndexForTarget(targetIndex);
            if (sourceIndex is not { } index)
            {
                entries[targetIndex] = new MaterialStateBitsEntry(byte.MaxValue);
                continue;
            }
            byte stateIndex = root[0x18 + index];
            if (stateIndex != byte.MaxValue && stateIndex >= source.States.Count)
                throw new InvalidDataException(
                    $"IW3 PS3 material '{source.Name}' slot {index} refers to missing state {stateIndex}.");
            entries[targetIndex] = new MaterialStateBitsEntry(stateIndex);
            inlineStates[targetIndex] = BinaryPrimitives.ReadUInt16BigEndian(root[(0x38 + index * 2)..]);
        }
        GfxCameraRegionType sourceCameraRegion = root[0x36] switch
        {
            0 => GfxCameraRegionType.LitOpaque,
            1 => GfxCameraRegionType.LitTrans,
            2 => GfxCameraRegionType.Emissive,
            3 => GfxCameraRegionType.None,
            byte value => throw new InvalidDataException(
                $"IW3 PS3 material '{source.Name}' has unsupported source camera region {value}.")
        };
        (MaterialSortKey sortKey, GfxCameraRegionType cameraRegion) =
            Iw3MaterialCompiler.ConvertSortAndCameraRegion(root[5], sourceCameraRegion,
                entries, sourceTechniqueSet.Name.TrimStart(','), source.Name);

        MaterialTextureDef[] textures = source.Textures.Select((texture, index) =>
            CompileTexture(texture, source.Name, index)).ToArray();
        MaterialConstantDef[] constants = source.Constants.Select((constant, index) =>
            CompileConstant(constant, source.Name, index)).ToArray();
        GfxStateBits[] states = source.States.Select((state, index) =>
            CompileState(state, source.Name, index)).ToArray();
        var result = new MaterialAsset
        {
            Info = new MaterialInfo
            {
                Name = techniques.OwnedNamePrefix + source.Name,
                GameFlags = (MaterialGameFlags)root[4],
                SortKey = sortKey,
                TextureAtlasRowCount = root[6],
                TextureAtlasColumnCount = root[7],
                // IW4 rebuilds this engine routing key after material sorting.
                DrawSurf = default,
                SurfaceTypeBits = (MaterialSurfaceTypeBits)BinaryPrimitives.ReadUInt32BigEndian(root[16..]),
                HashIndex = 0
            },
            StateBitsEntries = Array.AsReadOnly(entries),
            TextureCount = root[0x32],
            ConstantCount = root[0x33],
            StateBitsCount = root[0x34],
            StateFlags = (MaterialStateFlags)root[0x35],
            CameraRegion = cameraRegion,
            Pad43 = root[0x37],
            InlineTechniqueSlotStateBits = Array.AsReadOnly(inlineStates),
            RuntimeTechniqueSlotStateBits = BinaryPrimitives.ReadUInt32BigEndian(root[0x6c..]) == 0
                ? []
                : Array.AsReadOnly(new ushort[MaterialAsset.TechniqueSlotCount]),
            TechniqueSet = techniqueSet,
            Textures = Array.AsReadOnly(textures),
            Constants = Array.AsReadOnly(constants),
            StateBits = Array.AsReadOnly(states)
        };
        _materials.Add(source, result);
        return result;
    }

    private MaterialTextureDef CompileTexture(
        Iw3Ps3TextureSource source,
        string materialName,
        int index)
    {
        ReadOnlySpan<byte> root = source.Root.Span;
        if (root.Length != 12)
            throw new InvalidDataException($"IW3 PS3 material '{materialName}' texture {index} has an invalid root.");
        if (root[7] == (byte)TextureSemantic.WaterMap)
            throw new InvalidDataException($"IW3 PS3 material '{materialName}' has an unsupported water texture.");
        GfxImageAsset? image = source.Image ?? (source.ImageName is null
            ? null : _resolveImage(source.ImageName));
        if (BinaryPrimitives.ReadUInt32BigEndian(root[8..]) != 0 && image is null)
            throw new InvalidDataException($"IW3 PS3 material '{materialName}' texture {index} has no resolved image.");
        return new MaterialTextureDef
        {
            NameHash = BinaryPrimitives.ReadUInt32BigEndian(root),
            NameStart = root[4],
            NameEnd = root[5],
            SamplerState = (MaterialSamplerState)root[6],
            Semantic = (TextureSemantic)root[7],
            Image = image
        };
    }

    private static MaterialConstantDef CompileConstant(
        ReadOnlyMemory<byte> bytes,
        string materialName,
        int index)
    {
        ReadOnlySpan<byte> root = bytes.Span;
        if (root.Length != 32)
            throw new InvalidDataException($"IW3 PS3 material '{materialName}' constant {index} has an invalid root.");
        return new MaterialConstantDef
        {
            NameHash = BinaryPrimitives.ReadUInt32BigEndian(root),
            NameBytes = root[4..16].ToArray(),
            Literal = new MaterialVec4(
                BinaryPrimitives.ReadSingleBigEndian(root[16..]),
                BinaryPrimitives.ReadSingleBigEndian(root[20..]),
                BinaryPrimitives.ReadSingleBigEndian(root[24..]),
                BinaryPrimitives.ReadSingleBigEndian(root[28..]))
        };
    }

    private static GfxStateBits CompileState(
        ReadOnlyMemory<byte> bytes,
        string materialName,
        int index)
    {
        ReadOnlySpan<byte> root = bytes.Span;
        if (root.Length != 8)
            throw new InvalidDataException($"IW3 PS3 material '{materialName}' state {index} has an invalid root.");
        return new GfxStateBits
        {
            LoadBits = [
                BinaryPrimitives.ReadUInt32BigEndian(root),
                BinaryPrimitives.ReadUInt32BigEndian(root[4..])
            ]
        };
    }
}
