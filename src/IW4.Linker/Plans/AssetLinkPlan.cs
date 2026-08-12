using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen provider module. Its storage graph is the single authority for
/// native traversal, dependencies, script strings, layout, and relocations.
/// </summary>
internal abstract class AssetLinkPlan
{
    protected AssetLinkPlan(
        AssetKey key,
        string originalSerializedName,
        LinkStorageSymbol nameStorage,
        bool requireReferencePlaceholder = false)
    {
        if (originalSerializedName is null ||
            (originalSerializedName.Length == 0 &&
             !AssetKey.AllowsEmptyWireName(key.Family)))
            throw new InvalidDataException("Asset name cannot be null or empty.");
        if (originalSerializedName.Contains('\0'))
            throw new InvalidDataException("Asset name cannot contain NUL.");
        if (originalSerializedName.Any(character => character > byte.MaxValue))
            throw new InvalidDataException("Asset name must be representable as Latin-1.");

        AssetKey wireKey = AssetKey.FromWireName(
            key.Family,
            originalSerializedName);
        if (wireKey != key)
        {
            throw new InvalidDataException(
                $"Asset name '{originalSerializedName}' does not normalize to {key}.");
        }

        IsReferencePlaceholder = originalSerializedName.StartsWith(',');
        if (requireReferencePlaceholder && !IsReferencePlaceholder)
        {
            throw new InvalidDataException(
                $"{key.Family} providers are currently supported only as comma-prefixed references.");
        }

        OriginalSerializedName = originalSerializedName;
        NameStorage = nameStorage ?? throw new ArgumentNullException(nameof(nameStorage));
    }

    public string OriginalSerializedName { get; }
    public bool IsReferencePlaceholder { get; }

    internal abstract LinkStorageSymbol Root { get; }

    protected LinkStorageSymbol NameStorage { get; }

    public void Emit(LinkEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EmitProviderRoot(Root);
    }

    internal void VisitReferences(
        Action<AssetDependency> visitProviderDependency,
        Action<AssetDependency> visitDependencyOnly,
        Action<ScriptStringLinkOperation> visitScriptString,
        ISet<LinkStorageSymbol>? visitedStorage = null)
    {
        ArgumentNullException.ThrowIfNull(visitProviderDependency);
        ArgumentNullException.ThrowIfNull(visitDependencyOnly);
        ArgumentNullException.ThrowIfNull(visitScriptString);
        ISet<LinkStorageSymbol> visited = visitedStorage ??
            new HashSet<LinkStorageSymbol>(ReferenceEqualityComparer.Instance);
        VisitStorage(Root);

        void VisitStorage(LinkStorageSymbol storage)
        {
            if (!visited.Add(storage))
                return;

            foreach (LinkOperation operation in storage.Definition.Operations)
            {
                switch (operation)
                {
                    case ProviderLinkOperation provider:
                        visitProviderDependency(provider.Dependency);
                        break;
                    case DependencyOnlyLinkOperation dependencyOnly:
                        visitDependencyOnly(dependencyOnly.Dependency);
                        break;
                    case AliasCellStorageLinkOperation alias:
                        VisitStorage(alias.AliasCell.Target.Storage);
                        break;
                    case ScriptStringLinkOperation script:
                        visitScriptString(script);
                        break;
                    case DirectStorageLinkOperation direct:
                        VisitStorage(direct.Target.Storage);
                        break;
                    case PresenceStorageLinkOperation presence:
                        VisitStorage(presence.Target.Storage);
                        break;
                    case XStringLinkOperation text:
                        VisitStorage(text.Target.Storage);
                        break;
                    case MaterializeStorageLinkOperation materialize:
                        VisitStorage(materialize.Storage);
                        break;
                }
            }
        }
    }

    protected XStringLinkOperation NameOperation(
        LinkStorageSymbol owner,
        int pointerOffset) =>
        XStringOperation(owner, pointerOffset, NameStorage, "Asset.Name");

    protected static XStringLinkOperation XStringOperation(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageSymbol value,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            LinkStorageView.Whole(value),
            CanMaterializeRoot: true,
            fieldPath);

    protected static PresenceStorageLinkOperation PresenceOperation(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageSymbol value,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            LinkStorageView.Whole(value),
            fieldPath);

    protected static DirectStorageLinkOperation DirectOperation(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            target.View,
            target.CanMaterializeRoot,
            fieldPath);

    protected static DirectStorageLinkOperation DirectOperation(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageSymbol target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            LinkStorageView.Whole(target),
            CanMaterializeRoot: true,
            fieldPath);

    protected static ProviderLinkOperation ProviderOperation(
        LinkStorageSymbol owner,
        int pointerOffset,
        AssetDependency dependency) =>
        new(new LinkStorageCell(owner, pointerOffset), dependency);

    /// <summary>
    /// Freezes one provider AliasCell from semantic identity. A null retained
    /// pointer remains valid for authored semantic providers; a retained
    /// non-null cell cannot be discarded when no semantic identity exists.
    /// </summary>
    protected static AssetDependency? FreezeProviderDependency(
        XPointerReference retainedPointer,
        BaseAsset? definition,
        XAssetType expectedType,
        string fieldPath,
        string? symbolicName = null,
        bool allowExternalReference = false)
    {
        if (retainedPointer.Type != PointerType.Null &&
            retainedPointer.ResolutionMode != XPointerResolutionMode.AliasCell)
        {
            throw new InvalidDataException(
                $"{fieldPath} retains a {retainedPointer.ResolutionMode} pointer; " +
                "provider cells require AliasCell resolution.");
        }

        AssetKey? definitionKey = null;
        if (definition is not null)
        {
            XAssetType actualType;
            try
            {
                actualType = definition.SerializedAssetType;
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException(
                    $"{fieldPath} has an invalid provider type.",
                    exception);
            }
            if (actualType != expectedType)
            {
                throw new InvalidDataException(
                    $"{fieldPath} resolves {actualType}, expected {expectedType}.");
            }

            try
            {
                definitionKey = AssetKey.FromDefinition(definition);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"{fieldPath} has an invalid provider identity.",
                    exception);
            }
        }

        AssetKey? symbolicKey = null;
        if (symbolicName is not null)
        {
            try
            {
                symbolicKey = AssetKey.FromWireName(
                    CanonicalAssetFamily.FromSerializedType(expectedType),
                    symbolicName);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"{fieldPath} has invalid symbolic provider name '{symbolicName}'.",
                    exception);
            }
        }

        if (definitionKey is { } resolved &&
            symbolicKey is { } symbolic &&
            resolved != symbolic)
        {
            throw new InvalidDataException(
                $"{fieldPath} resolved and symbolic provider identities disagree.");
        }

        AssetKey? key = definitionKey ?? symbolicKey;
        if (key is null)
        {
            if (retainedPointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains a provider pointer without a semantic provider.");
            }
            return null;
        }

        string? externalSerializedName = null;
        if (allowExternalReference)
        {
            string? sourceName = symbolicName ?? definition?.SerializedAssetName;
            if (sourceName is null)
            {
                throw new InvalidDataException(
                    $"{fieldPath} permits an external provider but has no serialized name.");
            }

            externalSerializedName = sourceName.StartsWith(',')
                ? sourceName
                : $",{sourceName}";
        }

        return new AssetDependency(
            key.Value,
            expectedType,
            fieldPath,
            externalSerializedName);
    }

    protected static IEnumerable<LinkOperation> IndirectXStringOperations(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageSymbol? textStorage,
        string? assetName,
        XAssetType serializedType,
        string fieldPath)
    {
        if (textStorage is not null)
        {
            yield return XStringOperation(
                owner,
                pointerOffset,
                textStorage,
                fieldPath);
        }

        if (string.IsNullOrEmpty(assetName))
            yield break;

        AssetKey key;
        try
        {
            var family = CanonicalAssetFamily.FromSerializedType(serializedType);
            key = AssetKey.FromWireName(family, assetName);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"{fieldPath} has invalid indirect asset name '{assetName}'.",
                exception);
        }

        yield return new DependencyOnlyLinkOperation(new AssetDependency(
            key,
            serializedType,
            fieldPath));
    }

}

/// <summary>
/// One schema-declared provider AliasCell occurrence. Direct structural and
/// payload pointers use separate storage semantics and must not become assets.
/// </summary>
internal readonly record struct AssetDependency(
    AssetKey Key,
    XAssetType SerializedType,
    string FieldPath,
    string? ExternalSerializedName = null);
