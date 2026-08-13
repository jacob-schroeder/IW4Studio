using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen Material provider graph. XAssets are logical provider dependencies;
/// water and tables are direct storage; GfxStateBits load words use durable
/// non-XAsset alias cells; runtime technique state is source-free RUNTIME data.
/// </summary>
internal sealed class MaterialLinkPlan : AssetLinkPlan
{
    private MaterialLinkPlan(
        AssetKey key,
        string originalSerializedName,
        MaterialAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        MaterialInfo info = definition.Info ?? throw new InvalidDataException(
            "Material.Info cannot be null.");
        if ((byte)info.SortKey >= 0x40)
        {
            throw new InvalidDataException(
                "Material.SortKey must fit the engine's six-bit post-load field.");
        }

        byte[] stateBitsEntries = FreezeStateBitsEntries(
            definition.StateBitsEntries);
        ValidateTechniqueStateArray(
            definition.InlineTechniqueSlotStateBits,
            "Material.InlineTechniqueSlotStateBits");
        bool hasRuntimeTechniqueState = ValidateTechniqueStateArray(
            definition.RuntimeTechniqueSlotStateBits,
            "Material.RuntimeTechniqueSlotStateBits");

        MaterialTextureDef[] textures = FreezeItems(
            definition.Textures,
            "Material.Textures");
        MaterialConstantDef[] constants = FreezeItems(
            definition.Constants,
            "Material.Constants");
        GfxStateBits[] stateBits = FreezeItems(
            definition.StateBits,
            "Material.StateBits");
        MaterialXStringEntry[] xstrings = FreezeItems(
            definition.XStrings,
            "Material.XStrings");
        ValidateByteCount(textures.Length, "Material.Textures");
        ValidateByteCount(constants.Length, "Material.Constants");
        ValidateByteCount(stateBits.Length, "Material.StateBits");
        ValidateByteCount(xstrings.Length, "Material.XStrings");
        ValidateDeclaredCount(definition.TextureCount, textures.Length, "Material.Textures");
        ValidateDeclaredCount(definition.ConstantCount, constants.Length, "Material.Constants");
        ValidateDeclaredCount(definition.StateBitsCount, stateBits.Length, "Material.StateBits");
        ValidateDeclaredCount(definition.XStringCount, xstrings.Length, "Material.XStrings");

        var freezer = new StorageFreezer(freeze);
        AssetDependency? techniqueSet = FreezeProviderDependency(
            definition.TechniqueSetPointer.Untyped,
            definition.TechniqueSet,
            XAssetType.Techset,
            "Material.TechniqueSet");
        LinkStorageTarget? textureTable = freezer.FreezeTextureTable(
            definition.TextureTablePointer,
            textures);
        LinkStorageTarget? constantTable = FreezeConstantTable(
            freeze,
            definition.ConstantTablePointer,
            constants);
        LinkStorageTarget? stateBitsTable = freezer.FreezeStateBitsTable(
            definition.StateBitsPointer,
            stateBits);
        LinkStorageTarget? xstringTable = FreezeXStringTable(
            freeze,
            definition.XStringTablePointer,
            xstrings);
        LinkStorageSymbol? runtimeState = hasRuntimeTechniqueState
            ? LinkStorageSymbol.SourceFree(
                XFileBlockType.RUNTIME,
                MaterialAsset.TechniqueSlotCount * sizeof(ushort),
                alignment: 2,
                LinkMaterializationKind.RuntimeZeroFill)
            : null;

        var writer = new LinkTemplateWriter(MaterialAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteByte((byte)info.GameFlags);
        writer.WriteByte((byte)info.SortKey);
        writer.WriteByte(info.TextureAtlasRowCount);
        writer.WriteByte(info.TextureAtlasColumnCount);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32((uint)info.SurfaceTypeBits);
        writer.WriteUInt16(info.HashIndex);
        writer.WriteUInt16(info.Pad16);
        writer.WriteBytes(stateBitsEntries);
        writer.WriteByte(checked((byte)textures.Length));
        writer.WriteByte(checked((byte)constants.Length));
        writer.WriteByte(checked((byte)stateBits.Length));
        writer.WriteByte((byte)definition.StateFlags);
        writer.WriteByte((byte)definition.CameraRegion);
        writer.WriteByte(checked((byte)xstrings.Length));
        writer.WriteByte(definition.Pad43);
        writer.Skip(MaterialAsset.TechniqueSlotCount * sizeof(ushort));
        writer.WriteUInt16(definition.Pad8E);
        writer.Skip(6 * sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => CreateRootOperations(
                root,
                runtimeState,
                techniqueSet,
                textureTable,
                constantTable,
                stateBitsTable,
                xstringTable));
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        MaterialAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.Material,
                originalSerializedName,
                freeze);
        }

        return new MaterialLinkPlan(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private IEnumerable<LinkOperation> CreateRootOperations(
        LinkStorageSymbol root,
        LinkStorageSymbol? runtimeState,
        AssetDependency? techniqueSet,
        LinkStorageTarget? textureTable,
        LinkStorageTarget? constantTable,
        LinkStorageTarget? stateBitsTable,
        LinkStorageTarget? xstringTable)
    {
        yield return NameOperation(root, 0);
        if (runtimeState is not null)
        {
            yield return PresenceOperation(
                root,
                0x90,
                runtimeState,
                "Material.RuntimeTechniqueSlotStateBits");
        }
        if (techniqueSet is { } dependency)
            yield return ProviderOperation(root, 0x94, dependency);
        if (textureTable is not null)
        {
            yield return DirectOperation(
                root,
                0x98,
                textureTable.Value,
                "Material.Textures");
        }
        if (constantTable is not null)
        {
            yield return DirectOperation(
                root,
                0x9c,
                constantTable.Value,
                "Material.Constants");
        }
        if (stateBitsTable is not null)
        {
            yield return DirectOperation(
                root,
                0xa0,
                stateBitsTable.Value,
                "Material.StateBits");
        }
        if (xstringTable is not null)
        {
            yield return new PresenceStorageLinkOperation(
                new LinkStorageCell(root, 0xa4),
                xstringTable.Value.View,
                "Material.XStrings");
        }
    }

    private static byte[] FreezeStateBitsEntries(
        IReadOnlyList<MaterialStateBitsEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0)
            return new byte[MaterialAsset.TechniqueSlotCount];
        if (source.Count != MaterialAsset.TechniqueSlotCount)
        {
            throw new InvalidDataException(
                $"Material requires exactly {MaterialAsset.TechniqueSlotCount} state-bit entries.");
        }

        var values = new byte[source.Count];
        for (int index = 0; index < values.Length; index++)
            values[index] = source[index].StateBitsIndex;
        return values;
    }

    private static bool ValidateTechniqueStateArray(
        IReadOnlyList<ushort> source,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count is not (0 or MaterialAsset.TechniqueSlotCount))
        {
            throw new InvalidDataException(
                $"{fieldPath} must be absent or contain exactly " +
                $"{MaterialAsset.TechniqueSlotCount} runtime-derived entries.");
        }
        return source.Count != 0;
    }

    private static T[] FreezeItems<T>(
        IReadOnlyList<T> source,
        string fieldPath)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        return source
            .Select((value, index) => value ?? throw new InvalidDataException(
                $"{fieldPath}[{index}] cannot be null."))
            .ToArray();
    }

    private static void ValidateByteCount(int count, string fieldPath)
    {
        if ((uint)count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"{fieldPath} contains too many entries for its byte count.");
        }
    }

    private static void ValidateDeclaredCount(
        byte declaredCount,
        int retainedCount,
        string fieldPath)
    {
        if (declaredCount != retainedCount)
        {
            throw new InvalidDataException(
                $"{fieldPath} declares {declaredCount} row(s), but retains {retainedCount}.");
        }
    }

    private static LinkStorageTarget? FreezeConstantTable(
        LinkAssetFreezeScope freeze,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        IReadOnlyList<MaterialConstantDef> constants)
    {
        if (constants.Count == 0 &&
            pointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
            return null;

        var writer = new LinkTemplateWriter(
            checked(constants.Count * MaterialConstantDef.SerializedSize));
        for (int index = 0; index < constants.Count; index++)
        {
            MaterialConstantDef constant = constants[index];
            IReadOnlyList<byte> sourceName = constant.NameBytes ??
                throw new InvalidDataException(
                    $"Material.Constants[{index}].NameBytes cannot be null.");
            byte[] name = sourceName.ToArray();
            if (name.Length != 0x0c)
            {
                throw new InvalidDataException(
                    $"Material.Constants[{index}].NameBytes requires exactly 12 bytes.");
            }

            writer.WriteUInt32(constant.NameHash);
            writer.WriteBytes(name);
            WriteVec4(writer, constant.Literal);
        }
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 16,
            operations: null,
            "Material.Constants");
    }

    private static LinkStorageTarget? FreezeXStringTable(
        LinkAssetFreezeScope freeze,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        IReadOnlyList<MaterialXStringEntry> xstrings)
    {
        if (xstrings.Count == 0 &&
            pointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
            return null;

        var values = new LinkStorageSymbol?[xstrings.Count];
        for (int index = 0; index < xstrings.Count; index++)
        {
            MaterialXStringEntry entry = xstrings[index];
            if (entry.Index != index)
            {
                throw new InvalidDataException(
                    $"Material.XStrings[{index}] declares table index {entry.Index}.");
            }
            values[index] = freeze.FreezeOptionalXString(
                entry.Value,
                entry.Pointer.Untyped,
                $"Material.XStrings[{index}]");
        }

        return freeze.FreezeStorage(
            pointer,
            new byte[checked(values.Length * sizeof(int))],
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) => CreateXStringOperations(table, addend, values),
            "Material.XStrings");
    }

    private static IEnumerable<LinkOperation> CreateXStringOperations(
        LinkStorageSymbol table,
        int addend,
        IReadOnlyList<LinkStorageSymbol?> values)
    {
        for (int index = 0; index < values.Count; index++)
        {
            LinkStorageSymbol? value = values[index];
            if (value is null)
                continue;
            yield return XStringOperation(
                table,
                checked(addend + index * sizeof(int)),
                value,
                $"Material.XStrings[{index}]");
        }
    }

    private static void ValidateReferenceShape(MaterialAsset definition)
    {
        MaterialInfo info = definition.Info ?? throw new InvalidDataException(
            "A comma-prefixed Material provider requires Material.Info.");
        bool hasNonzeroInfo =
            info.GameFlags != MaterialGameFlags.None ||
            info.SortKey != 0 ||
            info.TextureAtlasRowCount != 0 ||
            info.TextureAtlasColumnCount != 0 ||
            info.DrawSurf.Packed != 0 ||
            info.SurfaceTypeBits != 0 ||
            info.HashIndex != 0 ||
            info.Pad16 != 0;
        bool hasNonzeroRoot =
            definition.TextureCount != 0 ||
            definition.ConstantCount != 0 ||
            definition.StateBitsCount != 0 ||
            definition.StateFlags != MaterialStateFlags.None ||
            definition.CameraRegion != GfxCameraRegionType.LitOpaque ||
            definition.XStringCount != 0 ||
            definition.Pad43 != 0 ||
            definition.Pad8E != 0;
        if (hasNonzeroInfo || hasNonzeroRoot)
        {
            throw new InvalidDataException(
                "A comma-prefixed Material provider must have a zeroed reference body.");
        }

        byte[] stateEntries = FreezeStateBitsEntries(
            definition.StateBitsEntries);
        if (stateEntries.Any(value => value != 0))
        {
            throw new InvalidDataException(
                "A comma-prefixed Material provider must have zeroed state-bit entries.");
        }
        ValidateZeroTechniqueState(
            definition.InlineTechniqueSlotStateBits,
            "inline technique-slot state bits");
        IReadOnlyList<ushort> runtimeState =
            definition.RuntimeTechniqueSlotStateBits ??
            throw new InvalidDataException(
                "Material runtime technique-slot state bits cannot be null.");
        if (runtimeState.Count != 0)
        {
            throw new InvalidDataException(
                "A comma-prefixed Material provider cannot contain a RUNTIME technique-state allocation.");
        }
        if (definition.TechniqueSetPointer.Raw != 0 ||
            definition.TechniqueSet is not null ||
            HasItems(definition.Textures, "Material.Textures") ||
            HasItems(definition.Constants, "Material.Constants") ||
            HasItems(definition.StateBits, "Material.StateBits") ||
            HasItems(definition.XStrings, "Material.XStrings"))
        {
            throw new InvalidDataException(
                "A comma-prefixed Material provider cannot contain referenced or structural data.");
        }
    }

    private static void ValidateZeroTechniqueState(
        IReadOnlyList<ushort> values,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != 0 &&
            (values.Count != MaterialAsset.TechniqueSlotCount ||
             values.Any(value => value != 0)))
        {
            throw new InvalidDataException(
                $"A comma-prefixed Material provider must have empty or zeroed {fieldName}.");
        }
    }

    private static bool HasItems<T>(
        IReadOnlyList<T> values,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(values, fieldName);
        return values.Count != 0;
    }


    private static void WriteVec4(
        LinkTemplateWriter writer,
        MaterialVec4 value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
        writer.WriteSingle(value.W);
    }

    private sealed class StorageFreezer
    {
        private readonly LinkAssetFreezeScope _freeze;

        public StorageFreezer(LinkAssetFreezeScope freeze) =>
            _freeze = freeze ?? throw new ArgumentNullException(nameof(freeze));

        public LinkStorageTarget? FreezeTextureTable(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            IReadOnlyList<MaterialTextureDef> textures)
        {
            if (textures.Count == 0 &&
                pointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
                return null;

            var frozen = new FrozenTexture[textures.Count];
            var writer = new LinkTemplateWriter(
                checked(textures.Count * MaterialTextureDef.SerializedSize));
            for (int index = 0; index < textures.Count; index++)
            {
                MaterialTextureDef texture = textures[index];
                string path = $"Material.Textures[{index}]";
                writer.WriteUInt32(texture.NameHash);
                writer.WriteByte(texture.NameStart);
                writer.WriteByte(texture.NameEnd);
                writer.WriteByte((byte)texture.SamplerState);
                writer.WriteByte((byte)texture.Semantic);
                writer.Skip(sizeof(int));

                if (texture.Semantic == TextureSemantic.WaterMap)
                {
                    if (texture.Image is not null)
                    {
                        throw new InvalidDataException(
                            $"{path} is water semantic 0x0B and cannot retain an outer Image provider.");
                    }
                    frozen[index] = new FrozenTexture(
                        texture.Water is null
                            ? null
                            : FreezeWater(
                                texture.DataPointer,
                                texture.Water,
                                $"{path}.Water"),
                        Image: null,
                        path);
                }
                else
                {
                    if (texture.Water is not null)
                    {
                        throw new InvalidDataException(
                            $"{path} is not water semantic 0x0B and cannot retain MaterialWater data.");
                    }
                    frozen[index] = new FrozenTexture(
                        Water: null,
                        FreezeProviderDependency(
                            texture.DataPointer,
                            texture.Image,
                            XAssetType.Image,
                            $"{path}.Image"),
                        path);
                }
            }

            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                (table, addend) => CreateTextureOperations(
                    table,
                    addend,
                    frozen),
                "Material.Textures");
        }

        public LinkStorageTarget? FreezeStateBitsTable(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            IReadOnlyList<GfxStateBits> stateBits)
        {
            if (stateBits.Count == 0 &&
                pointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
                return null;

            var aliases = new LinkAliasCellSymbol?[stateBits.Count];
            var writer = new LinkTemplateWriter(
                checked(stateBits.Count * GfxStateBits.SerializedSize));
            for (int index = 0; index < stateBits.Count; index++)
            {
                GfxStateBits state = stateBits[index];
                IReadOnlyList<uint> loadBits = state.LoadBits ??
                    throw new InvalidDataException(
                        $"Material.StateBits[{index}].LoadBits cannot be null.");
                if (loadBits.Count is not (0 or 2))
                {
                    throw new InvalidDataException(
                        $"Material.StateBits[{index}].LoadBits must be absent or contain exactly two words.");
                }
                writer.Skip(sizeof(int));
                writer.WriteUInt32(state.CommandWordCount);
                if (loadBits.Count != 0)
                    aliases[index] = FreezeLoadBits(
                        state.LoadBitsPointer,
                        loadBits,
                        $"Material.StateBits[{index}].LoadBits");
            }

            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                (table, addend) => CreateStateBitsOperations(
                    table,
                    addend,
                    aliases),
                "Material.StateBits");
        }

        private LinkStorageTarget FreezeWater(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            MaterialWater definition,
            string fieldPath)
        {
            if (definition.M < 0 || definition.N < 0)
                throw new InvalidDataException($"{fieldPath} dimensions cannot be negative.");
            int elementCount = checked(definition.M * definition.N);

            LinkStorageTarget? h0x = FreezeSpectrum(
                definition.H0XPointer,
                definition.H0X,
                elementCount,
                $"{fieldPath}.H0X");
            LinkStorageTarget? h0y = FreezeSpectrum(
                definition.H0YPointer,
                definition.H0Y,
                elementCount,
                $"{fieldPath}.H0Y");
            LinkStorageTarget? wTerm = FreezeSpectrum(
                definition.WTermPointer,
                definition.WTerm,
                elementCount,
                $"{fieldPath}.WTerm");
            AssetDependency? image = FreezeProviderDependency(
                definition.ImagePointer.Untyped,
                definition.Image,
                XAssetType.Image,
                $"{fieldPath}.Image");

            var writer = new LinkTemplateWriter(MaterialWater.SerializedSize);
            writer.WriteUInt32(definition.Writable.RawValue);
            writer.Skip(3 * sizeof(int));
            writer.WriteInt32(definition.M);
            writer.WriteInt32(definition.N);
            writer.WriteSingle(definition.Lx);
            writer.WriteSingle(definition.Lz);
            writer.WriteSingle(definition.Gravity);
            writer.WriteSingle(definition.WindVelocity);
            writer.WriteSingle(definition.WindDirection.X);
            writer.WriteSingle(definition.WindDirection.Y);
            writer.WriteSingle(definition.Amplitude);
            WriteVec4(writer, definition.CodeConstant);
            writer.Skip(sizeof(int));
            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                (root, addend) => CreateWaterOperations(
                    root,
                    addend,
                    h0x,
                    h0y,
                    wTerm,
                    image,
                    fieldPath),
                fieldPath);
        }

        private LinkStorageTarget? FreezeSpectrum(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            IReadOnlyList<float> source,
            int expectedCount,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.Count != expectedCount)
            {
                throw new InvalidDataException(
                    $"{fieldPath} contains {source.Count} value(s); its water dimensions require {expectedCount}.");
            }
            if (source.Count == 0 &&
                pointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
                return null;

            var writer = new LinkTemplateWriter(
                checked(source.Count * sizeof(float)));
            foreach (float value in source)
                writer.WriteSingle(value);
            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                operations: null,
                fieldPath);
        }

        private LinkAliasCellSymbol FreezeLoadBits(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            IReadOnlyList<uint> source,
            string fieldPath)
        {
            var writer = new LinkTemplateWriter(2 * sizeof(uint));
            writer.WriteUInt32(source[0]);
            writer.WriteUInt32(source[1]);
            return _freeze.FreezeAliasCellStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.TEMP,
                alignment: 4,
                operations: null,
                fieldPath);
        }

        private static IEnumerable<LinkOperation> CreateTextureOperations(
            LinkStorageSymbol table,
            int addend,
            IReadOnlyList<FrozenTexture> textures)
        {
            for (int index = 0; index < textures.Count; index++)
            {
                FrozenTexture texture = textures[index];
                int pointerOffset = checked(
                    addend + index * MaterialTextureDef.SerializedSize + 0x08);
                if (texture.Water is not null)
                {
                    yield return DirectOperation(
                        table,
                        pointerOffset,
                        texture.Water.Value,
                        $"{texture.FieldPath}.Water");
                }
                else if (texture.Image is { } image)
                {
                    yield return ProviderOperation(table, pointerOffset, image);
                }
            }
        }

        private static IEnumerable<LinkOperation> CreateWaterOperations(
            LinkStorageSymbol root,
            int addend,
            LinkStorageTarget? h0x,
            LinkStorageTarget? h0y,
            LinkStorageTarget? wTerm,
            AssetDependency? image,
            string fieldPath)
        {
            if (h0x is not null)
                yield return DirectOperation(root, addend + 0x04, h0x.Value, $"{fieldPath}.H0X");
            if (h0y is not null)
                yield return DirectOperation(root, addend + 0x08, h0y.Value, $"{fieldPath}.H0Y");
            if (wTerm is not null)
                yield return DirectOperation(root, addend + 0x0c, wTerm.Value, $"{fieldPath}.WTerm");
            if (image is { } dependency)
                yield return ProviderOperation(root, addend + 0x44, dependency);
        }

        private static IEnumerable<LinkOperation> CreateStateBitsOperations(
            LinkStorageSymbol table,
            int addend,
            IReadOnlyList<LinkAliasCellSymbol?> aliases)
        {
            for (int index = 0; index < aliases.Count; index++)
            {
                LinkAliasCellSymbol? alias = aliases[index];
                if (alias is null)
                    continue;
                yield return new AliasCellStorageLinkOperation(
                    new LinkStorageCell(
                        table,
                        checked(addend + index * GfxStateBits.SerializedSize)),
                    alias,
                    $"Material.StateBits[{index}].LoadBits");
            }
        }

        private sealed record FrozenTexture(
            LinkStorageTarget? Water,
            AssetDependency? Image,
            string FieldPath);
    }

}
