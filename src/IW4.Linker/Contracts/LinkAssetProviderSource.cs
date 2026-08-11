using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.Linker.Model;

namespace IW4.Linker.Contracts;

/// <summary>
/// Controls whether one transient provider source preserves captured import
/// identities or treats conflicting semantic values as authored copy-on-write
/// replacements.
/// </summary>
public enum LinkAssetProviderSourceDisposition
{
    PreserveImportedIdentity,
    AuthoredDetached
}

/// <summary>
/// Data-free namespace for one immutable loader capture. Frozen pools may
/// retain this token with symbolic capture occurrences; the token carries no
/// source addresses, runtime ids, provider objects, or loader authority.
/// </summary>
public sealed class LinkAssetImportIdentityScope
{
}

/// <summary>
/// Loader-owned, provider-scoped access to one captured zone object. Raw
/// provider ids and block coordinates are resolved inside the Loader and do
/// not become identities in the frozen link request.
/// </summary>
public interface ILinkAssetImportResolver
{
    LinkAssetImportIdentityScope IdentityScope { get; }

    PointerRelocation ResolvePointer(
        BaseAsset provider,
        XPointerReference pointer,
        string fieldPath);

    PointerRelocation ResolveProviderRootPointer(
        BaseAsset provider,
        int rootRelativeOffset,
        string fieldPath);

    IReadOnlyList<AllocationReference> ResolveDirectStorageRange(
        PointerRelocation pointer,
        int byteLength,
        string fieldPath);

    AliasCellSymbol ResolveAliasCell(
        PointerRelocation pointer,
        string fieldPath);

    SymbolReference? ResolveAliasCellValue(
        AliasCellSymbol aliasCell,
        string fieldPath);
}

/// <summary>
/// One transient provider input to a pool freeze. The source may retain a
/// mutable semantic asset and Loader capture only until <see cref="LinkAssetPool"/>
/// finishes construction; neither survives in the immutable pool. When
/// <see cref="Definition"/> is an edited clone, <see cref="ImportedDefinition"/>
/// must be the original provider registered with <see cref="ImportResolver"/>.
/// </summary>
public sealed class LinkAssetProviderSource
{
    public LinkAssetProviderSource(
        BaseAsset definition,
        ILinkAssetImportResolver? importResolver = null,
        IEnumerable<ImageFileStreamLanguageReferences>? imageStreamReferences = null,
        LinkAssetProviderSourceDisposition disposition =
            LinkAssetProviderSourceDisposition.PreserveImportedIdentity,
        BaseAsset? importedDefinition = null)
    {
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));

        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ImportResolver = importResolver;
        ImportedDefinition = importedDefinition ?? definition;
        ImageStreamReferences = imageStreamReferences is null
            ? Array.Empty<ImageFileStreamLanguageReferences>()
            : Array.AsReadOnly(imageStreamReferences
                .Select(references => references ?? throw new ArgumentException(
                    "Imagefile language reference contributions cannot contain null.",
                    nameof(imageStreamReferences)))
                .ToArray());
        Disposition = disposition;
    }

    public BaseAsset Definition { get; }
    public ILinkAssetImportResolver? ImportResolver { get; }
    /// <summary>
    /// Transient original provider used only to resolve imported pointer
    /// occurrences. Recipes always serialize <see cref="Definition"/>.
    /// </summary>
    public BaseAsset ImportedDefinition { get; }
    public IReadOnlyList<ImageFileStreamLanguageReferences> ImageStreamReferences { get; }
    public LinkAssetProviderSourceDisposition Disposition { get; }

    /// <summary>
    /// Marks this semantic definition as an authored replacement. Its resolver
    /// remains transiently available to reuse unchanged imported identities;
    /// conflicting semantic storage and XStrings receive fresh symbols.
    /// </summary>
    public LinkAssetProviderSource AsAuthoredDetached() =>
        Disposition == LinkAssetProviderSourceDisposition.AuthoredDetached
            ? this
            : new LinkAssetProviderSource(
                Definition,
                ImportResolver,
                ImageStreamReferences,
                LinkAssetProviderSourceDisposition.AuthoredDetached,
                ImportedDefinition);
}
