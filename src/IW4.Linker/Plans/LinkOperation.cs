using IW4.FastFiles.Strings;

namespace IW4.Linker.Plans;

/// <summary>
/// One native-order operation performed after a frozen storage body has been
/// copied or reserved. Pointer cells name symbols; source addresses and raw
/// imported pointer words never participate in identity.
/// </summary>
internal abstract record LinkOperation;

internal sealed record DirectStorageLinkOperation(
    LinkStorageCell Cell,
    LinkStorageView Target,
    bool CanMaterializeRoot,
    string FieldPath) : LinkOperation;

internal sealed record PresenceStorageLinkOperation(
    LinkStorageCell Cell,
    LinkStorageView Target,
    string FieldPath) : LinkOperation;

internal sealed record XStringLinkOperation(
    LinkStorageCell Cell,
    LinkStorageView Target,
    bool CanMaterializeRoot,
    string FieldPath) : LinkOperation;

internal sealed record ProviderLinkOperation(
    LinkStorageCell Cell,
    AssetDependency Dependency) : LinkOperation;

/// <summary>
/// A logical dependency carried by a native name field. It participates in
/// provider closure without owning a relocation cell or materializing a body;
/// the paired XString operation remains the wire authority.
/// </summary>
internal sealed record DependencyOnlyLinkOperation(
    AssetDependency Dependency) : LinkOperation;

/// <summary>
/// A non-XAsset indirect publication cell. The alias symbol is request-local
/// identity; its target storage and bytes do not imply alias-cell sharing.
/// </summary>
internal sealed record AliasCellStorageLinkOperation(
    LinkStorageCell Cell,
    LinkAliasCellSymbol AliasCell,
    string FieldPath) : LinkOperation;

internal sealed record ScriptStringLinkOperation : LinkOperation
{
    public ScriptStringLinkOperation(
        LinkStorageCell cell,
        ScriptStringReference value,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        if (value.Text is null && value.RawLocalIndex != 0)
        {
            throw new NotSupportedException(
                $"{fieldPath} retains nonzero zone-local index " +
                $"{value.RawLocalIndex} without semantic text.");
        }

        Cell = cell;
        Text = value.Text;
        FieldPath = fieldPath;
    }

    public LinkStorageCell Cell { get; }
    public string? Text { get; }
    public string FieldPath { get; }
}

/// <summary>
/// An ordered allocation with no serialized owner pointer. This is used for
/// schema-proven source-free RUNTIME/VIRTUAL reserves and similar children.
/// </summary>
internal sealed record MaterializeStorageLinkOperation(
    LinkStorageSymbol Storage,
    string FieldPath) : LinkOperation;
