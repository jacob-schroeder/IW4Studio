using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen MaterialTechniqueSet graph. Direct technique, declaration, argument,
/// and literal storage remains separate from shader provider references.
/// </summary>
internal sealed class MaterialTechniqueSetLinkRecipe : AssetLinkRecipe
{
    private MaterialTechniqueSetLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        MaterialTechniqueSetAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        if (!Enum.IsDefined(definition.WorldVertexFormat))
        {
            throw new InvalidDataException(
                $"Unsupported MaterialTechniqueSet world vertex format {definition.WorldVertexFormat}.");
        }

        MaterialTechniqueSlot[] techniques = FreezeSlots(
            definition.TechniqueSlots);
        var freezer = new StorageFreezer(freeze);
        var writer = new LinkTemplateWriter(
            MaterialTechniqueSetAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteByte((byte)definition.WorldVertexFormat);
        writer.Skip(3);
        writer.Skip(MaterialTechniqueSetAsset.SerializedSize - 0x08);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => freezer.CreateRootOperations(root, this, techniques));
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        MaterialTechniqueSetAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.Techset,
                originalSerializedName,
                freeze);
        }

        return new MaterialTechniqueSetLinkRecipe(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private static MaterialTechniqueSlot[] FreezeSlots(
        IReadOnlyList<MaterialTechniqueSlot> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var techniques = new MaterialTechniqueSlot[
            MaterialAssetTechniqueSlotCount];
        if (source.Count == 0)
        {
            for (int index = 0; index < techniques.Length; index++)
            {
                techniques[index] = new MaterialTechniqueSlot(
                    index,
                    default,
                    Technique: null);
            }
            return techniques;
        }
        if (source.Count != MaterialAssetTechniqueSlotCount)
        {
            throw new InvalidDataException(
                $"MaterialTechniqueSet requires exactly {MaterialAssetTechniqueSlotCount} technique slots.");
        }

        for (int index = 0; index < techniques.Length; index++)
        {
            MaterialTechniqueSlot slot = source[index] ??
                throw new InvalidDataException(
                    $"MaterialTechniqueSet.TechniqueSlots[{index}] cannot be null.");
            if (slot.Index != index)
            {
                throw new InvalidDataException(
                    $"MaterialTechniqueSet.TechniqueSlots[{index}] declares slot {slot.Index}.");
            }
            techniques[index] = slot;
        }

        return techniques;
    }

    private static void ValidateReferenceShape(
        MaterialTechniqueSetAsset definition)
    {
        if ((byte)definition.WorldVertexFormat != 0)
        {
            throw new InvalidDataException(
                "A comma-prefixed MaterialTechniqueSet provider must have a zeroed reference body.");
        }

        MaterialTechniqueSlot[] techniques = FreezeSlots(
            definition.TechniqueSlots);
        if (techniques.Any(slot => slot.Technique is not null ||
                slot.Pointer.Type != IW4.FastFiles.Pointers.PointerType.Null))
        {
            throw new InvalidDataException(
                "A comma-prefixed MaterialTechniqueSet provider cannot contain techniques.");
        }
    }

    private const int MaterialAssetTechniqueSlotCount = 37;

    private sealed class StorageFreezer
    {
        private readonly LinkAssetFreezeScope _freeze;

        public StorageFreezer(LinkAssetFreezeScope freeze) =>
            _freeze = freeze ?? throw new ArgumentNullException(nameof(freeze));

        public IEnumerable<LinkOperation> CreateRootOperations(
            LinkStorageSymbol root,
            MaterialTechniqueSetLinkRecipe recipe,
            IReadOnlyList<MaterialTechniqueSlot> techniques)
        {
            yield return recipe.NameOperation(root, 0);
            for (int index = 0; index < techniques.Count; index++)
            {
                MaterialTechniqueSlot slot = techniques[index];
                MaterialTechniqueAsset? technique = slot.Technique;
                if (technique is null)
                {
                    if (slot.Pointer.Type != IW4.FastFiles.Pointers.PointerType.Null)
                    {
                        throw new InvalidDataException(
                            $"MaterialTechniqueSet.TechniqueSlots[{index}] has no semantic technique body.");
                    }
                    continue;
                }

                LinkStorageTarget storage = FreezeTechnique(
                    slot.Pointer.Untyped,
                    technique,
                    $"MaterialTechniqueSet.TechniqueSlots[{index}]");
                yield return Direct(
                    root,
                    checked(0x08 + index * sizeof(int)),
                    storage,
                    $"MaterialTechniqueSet.TechniqueSlots[{index}]");
            }
        }

        private LinkStorageTarget FreezeTechnique(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            MaterialTechniqueAsset definition,
            string fieldPath)
        {
            IReadOnlyList<MaterialPassAsset> sourcePasses = definition.Passes ??
                throw new InvalidDataException($"{fieldPath}.Passes cannot be null.");
            MaterialPassAsset[] passes = sourcePasses
                .Select((pass, index) => pass ?? throw new InvalidDataException(
                    $"{fieldPath}.Passes[{index}] cannot be null."))
                .ToArray();
            if (passes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"{fieldPath} contains too many passes for its UInt16 count.");
            }
            if (definition.PassCount != passes.Length)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.PassCount declares {definition.PassCount}, but retains {passes.Length} pass(es).");
            }

            LinkStorageSymbol? name = _freeze.FreezeOptionalXString(
                definition.Name,
                definition.NamePointer.Untyped,
                $"{fieldPath}.Name");
            var writer = new LinkTemplateWriter(
                MaterialTechniqueAsset.SerializedSize);
            writer.Skip(sizeof(int));
            writer.WriteUInt16(definition.Flags);
            writer.WriteUInt16(checked((ushort)passes.Length));
            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                (root, addend) =>
                {
                    LinkStorageSymbol? passTable = passes.Length == 0
                        ? null
                        : FreezePassTableOnce(
                            root,
                            passes,
                            fieldPath);
                    return CreateTechniqueOperations(
                        root,
                        addend,
                        passTable,
                        name,
                        fieldPath);
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezePassTableOnce(
            LinkStorageSymbol techniqueStorage,
            IReadOnlyList<MaterialPassAsset> passes,
            string fieldPath)
        {
            return _freeze.FreezeTechniquePassTable(
                techniqueStorage,
                FreezePassTable(passes, fieldPath),
                $"{fieldPath}.Passes");
        }

        private static IEnumerable<LinkOperation> CreateTechniqueOperations(
            LinkStorageSymbol root,
            int addend,
            LinkStorageSymbol? passTable,
            LinkStorageSymbol? name,
            string fieldPath)
        {
            if (passTable is not null)
            {
                yield return new MaterializeStorageLinkOperation(
                    passTable,
                    $"{fieldPath}.Passes");
            }
            if (name is not null)
            {
                yield return XStringOperation(
                    root,
                    addend,
                    name,
                    $"{fieldPath}.Name");
            }
        }

        private LinkStorageSymbol FreezePassTable(
            IReadOnlyList<MaterialPassAsset> passes,
            string fieldPath)
        {
            FrozenPass[] frozen = passes
                .Select((pass, index) => FreezePass(
                    pass,
                    $"{fieldPath}.Passes[{index}]"))
                .ToArray();
            var writer = new LinkTemplateWriter(
                checked(frozen.Length * MaterialPassAsset.SerializedSize));
            foreach (FrozenPass pass in frozen)
            {
                writer.Skip(3 * sizeof(int));
                writer.WriteByte(pass.PerPrimArgCount);
                writer.WriteByte(pass.PerObjArgCount);
                writer.WriteByte(pass.StableArgCount);
                writer.WriteByte(pass.CustomSamplerFlags);
                writer.WriteByte(pass.PrecompiledIndex);
                writer.Skip(3);
                writer.Skip(sizeof(int));
            }

            return LinkStorageSymbol.SourceBytes(
                XFileBlockType.LARGE,
                writer.Complete(),
                alignment: 4,
                table => CreatePassOperations(table, frozen));
        }

        private FrozenPass FreezePass(
            MaterialPassAsset definition,
            string fieldPath)
        {
            IReadOnlyList<MaterialShaderArgumentAsset> arguments =
                definition.Args ?? throw new InvalidDataException(
                    $"{fieldPath}.Args cannot be null.");
            int declaredCount = checked(
                definition.PerPrimArgCount +
                definition.PerObjArgCount +
                definition.StableArgCount);
            if (arguments.Count != declaredCount)
            {
                throw new InvalidDataException(
                    $"{fieldPath} declares {declaredCount} shader argument(s), " +
                    $"but retains {arguments.Count}.");
            }

            LinkStorageTarget? declaration = definition.VertexDeclaration is null
                ? null
                : FreezeDeclaration(
                    definition.VertexDeclPointer.Untyped,
                    definition.VertexDeclaration,
                    $"{fieldPath}.VertexDeclaration");
            AssetDependency? vertexShader = FreezeProviderDependency(
                definition.VertexShaderPointer.Untyped,
                definition.VertexShader,
                XAssetType.VertexShader,
                $"{fieldPath}.VertexShader");
            AssetDependency? pixelShader = FreezeProviderDependency(
                definition.PixelShaderPointer.Untyped,
                definition.PixelShader,
                XAssetType.PixelShader,
                $"{fieldPath}.PixelShader");
            LinkStorageTarget? argumentTable = arguments.Count == 0
                ? null
                : FreezeArgumentTable(
                    definition.ArgsPointer.Untyped,
                    arguments,
                    fieldPath);

            return new FrozenPass(
                declaration,
                vertexShader,
                pixelShader,
                argumentTable,
                definition.PerPrimArgCount,
                definition.PerObjArgCount,
                definition.StableArgCount,
                definition.CustomSamplerFlags,
                definition.PrecompiledIndex,
                fieldPath);
        }

        private static IEnumerable<LinkOperation> CreatePassOperations(
            LinkStorageSymbol table,
            IReadOnlyList<FrozenPass> passes)
        {
            for (int index = 0; index < passes.Count; index++)
            {
                FrozenPass pass = passes[index];
                int offset = checked(index * MaterialPassAsset.SerializedSize);
                if (pass.VertexDeclaration is not null)
                {
                    yield return Direct(
                        table,
                        offset,
                        pass.VertexDeclaration.Value,
                        $"{pass.FieldPath}.VertexDeclaration");
                }
                if (pass.VertexShader is { } vertexShader)
                {
                    yield return ProviderOperation(
                        table,
                        checked(offset + 0x04),
                        vertexShader);
                }
                if (pass.PixelShader is { } pixelShader)
                {
                    yield return ProviderOperation(
                        table,
                        checked(offset + 0x08),
                        pixelShader);
                }
                if (pass.ArgumentTable is not null)
                {
                    yield return Direct(
                        table,
                        checked(offset + 0x14),
                        pass.ArgumentTable.Value,
                        $"{pass.FieldPath}.Args");
                }
            }
        }

        private LinkStorageTarget FreezeDeclaration(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            MaterialVertexDeclarationAsset definition,
            string fieldPath)
        {
            IReadOnlyList<MaterialVertexStreamRouting> routing =
                definition.Routing ?? throw new InvalidDataException(
                    $"{fieldPath}.Routing cannot be null.");
            if (routing.Count != MaterialVertexDeclarationAsset.RoutingCount)
            {
                throw new InvalidDataException(
                    $"{fieldPath} requires exactly " +
                    $"{MaterialVertexDeclarationAsset.RoutingCount} routing pairs.");
            }

            var writer = new LinkTemplateWriter(
                MaterialVertexDeclarationAsset.SerializedSize);
            writer.WriteByte(definition.StreamCount);
            writer.WriteByte(definition.HasOptionalSource);
            foreach (MaterialVertexStreamRouting route in routing)
            {
                writer.WriteByte(route.Source);
                writer.WriteByte(route.Dest);
            }
            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                operations: null,
                fieldPath);
        }

        private LinkStorageTarget FreezeArgumentTable(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            IReadOnlyList<MaterialShaderArgumentAsset> arguments,
            string passPath)
        {
            MaterialShaderArgumentAsset[] copied = arguments
                .Select((argument, index) => argument ?? throw new InvalidDataException(
                    $"{passPath}.Args[{index}] cannot be null."))
                .ToArray();
            var writer = new LinkTemplateWriter(
                checked(copied.Length * sizeof(ulong)));
            var literals = new LinkStorageTarget?[copied.Length];
            for (int index = 0; index < copied.Length; index++)
            {
                MaterialShaderArgumentAsset argument = copied[index];
                bool isLiteral = IsLiteral(argument.Type);
                if (isLiteral != argument.LiteralConstant.HasValue)
                {
                    throw new InvalidDataException(
                        $"{passPath}.Args[{index}] must retain a Float4 exactly when its type is literal.");
                }

                writer.WriteUInt16((ushort)argument.Type);
                writer.WriteUInt16(argument.Dest);
                if (isLiteral)
                {
                    writer.Skip(sizeof(int));
                    literals[index] = FreezeLiteral(
                        argument.ArgumentPointer,
                        argument,
                        $"{passPath}.Args[{index}].LiteralConstant");
                }
                else
                {
                    writer.WriteInt32(argument.ArgumentRaw);
                }
            }

            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                (table, addend) => CreateLiteralOperations(
                    table,
                    addend,
                    literals,
                    passPath),
                $"{passPath}.Args");
        }

        private LinkStorageTarget FreezeLiteral(
            IW4.FastFiles.Pointers.XPointerReference pointer,
            MaterialShaderArgumentAsset argument,
            string fieldPath)
        {
            MaterialShaderLiteralConstant literal = argument.LiteralConstant!.Value;
            var writer = new LinkTemplateWriter(sizeof(float) * 4);
            writer.WriteInt32(BitConverter.SingleToInt32Bits(literal.X));
            writer.WriteInt32(BitConverter.SingleToInt32Bits(literal.Y));
            writer.WriteInt32(BitConverter.SingleToInt32Bits(literal.Z));
            writer.WriteInt32(BitConverter.SingleToInt32Bits(literal.W));
            return _freeze.FreezeStorage(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 16,
                operations: null,
                fieldPath);
        }

        private static IEnumerable<LinkOperation> CreateLiteralOperations(
            LinkStorageSymbol table,
            int addend,
            IReadOnlyList<LinkStorageTarget?> literals,
            string passPath)
        {
            for (int index = 0; index < literals.Count; index++)
            {
                LinkStorageTarget? literal = literals[index];
                if (literal is null)
                    continue;
                yield return Direct(
                    table,
                    checked(addend + index * sizeof(ulong) + sizeof(int)),
                    literal.Value,
                    $"{passPath}.Args[{index}].LiteralConstant");
            }
        }

        private static bool IsLiteral(MaterialShaderArgumentType type) =>
            type is
                MaterialShaderArgumentType.LiteralVertexConst or
                MaterialShaderArgumentType.LiteralPixelConst;

        private static DirectStorageLinkOperation Direct(
            LinkStorageSymbol owner,
            int pointerOffset,
            LinkStorageTarget target,
            string fieldPath) =>
            new(
                new LinkStorageCell(owner, pointerOffset),
                target.View,
                target.CanMaterializeRoot,
                fieldPath);

        private static ProviderLinkOperation ProviderOperation(
            LinkStorageSymbol owner,
            int pointerOffset,
            AssetDependency dependency) =>
            new(new LinkStorageCell(owner, pointerOffset), dependency);

        private sealed record FrozenPass(
            LinkStorageTarget? VertexDeclaration,
            AssetDependency? VertexShader,
            AssetDependency? PixelShader,
            LinkStorageTarget? ArgumentTable,
            byte PerPrimArgCount,
            byte PerObjArgCount,
            byte StableArgCount,
            byte CustomSamplerFlags,
            byte PrecompiledIndex,
            string FieldPath);
    }
}
