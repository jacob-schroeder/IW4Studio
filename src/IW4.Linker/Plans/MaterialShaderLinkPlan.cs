using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen MaterialPixelShader/MaterialVertexShader provider. Bytecode uses the
/// native non-XAsset alias-cell publication path and remains distinct from the
/// logical shader provider identity.
/// </summary>
internal sealed class MaterialShaderLinkPlan : AssetLinkPlan
{
    private MaterialShaderLinkPlan(
        AssetKey key,
        string originalSerializedName,
        MaterialShaderKind kind,
        byte[] programBytes,
        byte[]? bytecode,
        LinkAliasCellSymbol? bytecodeAlias,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        int rootSize = GetRootSize(kind);
        var writer = new LinkTemplateWriter(rootSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteUInt32(checked((uint)(bytecode?.Length ?? 0)));
        writer.WriteBytes(programBytes);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => bytecodeAlias is null
                ? [NameOperation(root, 0)]
                :
                [
                    NameOperation(root, 0),
                    new AliasCellStorageLinkOperation(
                        new LinkStorageCell(root, sizeof(int)),
                        bytecodeAlias,
                        $"{GetDisplayName(kind)}.Bytecode")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        MaterialShaderAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        MaterialShaderKind kind = definition.Kind;
        _ = GetRootSize(kind);

        byte[] programBytes = definition.ProgramBytes?.ToArray()
            ?? throw new InvalidDataException(
                $"{GetDisplayName(kind)} program bytes cannot be null.");
        int expectedProgramByteCount = GetProgramByteCount(kind);
        if (programBytes.Length != expectedProgramByteCount)
        {
            throw new InvalidDataException(
                $"{GetDisplayName(kind)} requires exactly " +
                $"{expectedProgramByteCount} trailing program byte(s).");
        }

        byte[]? bytecode = definition.Data?.ToArray();
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.DataSize != 0 ||
                definition.DataPointer.Type !=
                    IW4.FastFiles.Pointers.PointerType.Null ||
                bytecode is not null ||
                programBytes.Any(value => value != 0))
            {
                throw new InvalidDataException(
                    $"A comma-prefixed {GetDisplayName(kind)} provider must have a zeroed reference body.");
            }

            return ExternalAssetLinkPlan.Create(
                key,
                GetAssetType(kind),
                originalSerializedName,
                freeze);
        }

        if (definition.DataSize != (uint)(bytecode?.Length ?? 0))
        {
            throw new InvalidDataException(
                $"{GetDisplayName(kind)} declares {definition.DataSize} bytecode byte(s), " +
                $"but retains {bytecode?.Length ?? 0}.");
        }
        if (bytecode is null && definition.DataPointer.Type !=
            IW4.FastFiles.Pointers.PointerType.Null)
        {
            throw new InvalidDataException(
                $"{GetDisplayName(kind)} retains a non-null bytecode pointer without semantic bytes.");
        }

        LinkAliasCellSymbol? bytecodeAlias = bytecode is null
            ? null
            : freeze.FreezeAliasCellStorage(
                definition.DataPointer.Untyped,
                bytecode,
                XFileBlockType.TEMP,
                alignment: 16,
                operations: null,
                $"{GetDisplayName(kind)}.Bytecode");

        return new MaterialShaderLinkPlan(
            key,
            originalSerializedName,
            kind,
            programBytes,
            bytecode,
            bytecodeAlias,
            freeze);
    }

    private static string GetDisplayName(MaterialShaderKind kind) => kind switch
    {
        MaterialShaderKind.Pixel => "MaterialPixelShader",
        MaterialShaderKind.Vertex => "MaterialVertexShader",
        _ => throw new InvalidDataException(
            $"Unsupported material shader kind {kind}.")
    };

    private static XAssetType GetAssetType(MaterialShaderKind kind)
    {
        try
        {
            return MaterialShaderAsset.GetAssetType(kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Unsupported material shader kind {kind}.");
        }
    }

    private static int GetRootSize(MaterialShaderKind kind)
    {
        try
        {
            return MaterialShaderAsset.GetSerializedSize(kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Unsupported material shader kind {kind}.");
        }
    }

    private static int GetProgramByteCount(MaterialShaderKind kind)
    {
        try
        {
            return MaterialShaderAsset.GetProgramByteCount(kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Unsupported material shader kind {kind}.");
        }
    }
}
