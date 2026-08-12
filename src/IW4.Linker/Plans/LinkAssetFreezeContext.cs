using System.Diagnostics.CodeAnalysis;
using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

internal sealed class LinkAssetFrozenIdentityCatalog
{
    private readonly Dictionary<ImportSymbolKey, LinkStorageSymbol> _storage;
    private readonly Dictionary<ImportSymbolKey, LinkStorageSymbol> _xstrings;
    private readonly Dictionary<ImportSymbolKey, LinkAliasCellSymbol> _aliasCells;
    private readonly Dictionary<LinkStorageSymbol, LinkStorageSymbol>
        _techniquePassTables;

    public LinkAssetFrozenIdentityCatalog()
    {
        _storage = new(ImportSymbolKeyComparer.Instance);
        _xstrings = new(ImportSymbolKeyComparer.Instance);
        _aliasCells = new(ImportSymbolKeyComparer.Instance);
        _techniquePassTables = new(ReferenceEqualityComparer.Instance);
    }

    private LinkAssetFrozenIdentityCatalog(LinkAssetFrozenIdentityCatalog source)
        : this()
    {
        foreach ((ImportSymbolKey key, LinkStorageSymbol value) in source._storage)
            _storage.Add(key, value);
        foreach ((ImportSymbolKey key, LinkStorageSymbol value) in source._xstrings)
            _xstrings.Add(key, value);
        foreach ((ImportSymbolKey key, LinkAliasCellSymbol value) in source._aliasCells)
            _aliasCells.Add(key, value);
        foreach ((LinkStorageSymbol key, LinkStorageSymbol value) in source._techniquePassTables)
            _techniquePassTables.Add(key, value);
    }

    public LinkAssetFrozenIdentityCatalog Clone() => new(this);

    public LinkAssetFrozenIdentityCatalog Merge(
        LinkAssetFrozenIdentityCatalog other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var merged = Clone();
        foreach ((ImportSymbolKey key, LinkStorageSymbol value) in other._storage)
            MergeIdentity(merged._storage, key, value, "direct storage");
        foreach ((ImportSymbolKey key, LinkStorageSymbol value) in other._xstrings)
            MergeIdentity(merged._xstrings, key, value, "XString storage");
        foreach ((ImportSymbolKey key, LinkAliasCellSymbol value) in other._aliasCells)
            MergeIdentity(merged._aliasCells, key, value, "alias-cell storage");
        foreach ((LinkStorageSymbol key, LinkStorageSymbol value) in other._techniquePassTables)
        {
            if (merged._techniquePassTables.TryGetValue(key, out LinkStorageSymbol? existing))
            {
                if (!ReferenceEquals(existing, value))
                {
                    throw new InvalidDataException(
                        "Composed pools froze one MaterialTechnique pass table into competing symbols.");
                }
            }
            else
            {
                merged._techniquePassTables.Add(key, value);
            }
        }
        return merged;
    }

    public bool TryGetStorage(
        ImportSymbolKey key,
        [NotNullWhen(true)] out LinkStorageSymbol? storage) =>
        _storage.TryGetValue(key, out storage);

    public bool TryGetXString(
        ImportSymbolKey key,
        [NotNullWhen(true)] out LinkStorageSymbol? storage) =>
        _xstrings.TryGetValue(key, out storage);

    public bool TryGetAliasCell(
        ImportSymbolKey key,
        [NotNullWhen(true)] out LinkAliasCellSymbol? alias) =>
        _aliasCells.TryGetValue(key, out alias);

    public void AddStorage(ImportSymbolKey key, LinkStorageSymbol storage) =>
        _storage.Add(key, storage);

    public void AddXString(ImportSymbolKey key, LinkStorageSymbol storage) =>
        _xstrings.Add(key, storage);

    public void AddAliasCell(ImportSymbolKey key, LinkAliasCellSymbol alias) =>
        _aliasCells.Add(key, alias);

    public bool TryGetTechniquePassTable(
        LinkStorageSymbol technique,
        [NotNullWhen(true)] out LinkStorageSymbol? passTable) =>
        _techniquePassTables.TryGetValue(technique, out passTable);

    public void AddTechniquePassTable(
        LinkStorageSymbol technique,
        LinkStorageSymbol passTable) =>
        _techniquePassTables.Add(technique, passTable);

    private static void MergeIdentity<T>(
        IDictionary<ImportSymbolKey, T> destination,
        ImportSymbolKey key,
        T value,
        string description)
        where T : class
    {
        if (destination.TryGetValue(key, out T? existing))
        {
            if (!ReferenceEquals(existing, value))
            {
                throw new InvalidDataException(
                    $"Composed pools froze one imported {description} identity into competing symbols.");
            }
            return;
        }

        destination.Add(key, value);
    }
}

/// <summary>
/// Single pool-wide owner of imported physical, XString, and non-XAsset alias
/// identity. Capture symbols are consumed only while plans are frozen.
/// </summary>
internal sealed class LinkAssetFreezeContext
{
    private readonly LinkAssetFrozenIdentityCatalog _catalog;
    private readonly Dictionary<ImportSymbolKey, ImportedStorage> _storage =
        new(ImportSymbolKeyComparer.Instance);
    private readonly Dictionary<ImportSymbolKey, LinkStorageSymbol> _xstrings =
        new(ImportSymbolKeyComparer.Instance);
    private readonly Dictionary<ImportSymbolKey, byte[]> _xstringTemplates =
        new(ImportSymbolKeyComparer.Instance);
    private readonly Dictionary<ImportSymbolKey, LinkAliasCellSymbol> _aliasCells =
        new(ImportSymbolKeyComparer.Instance);
    private readonly Dictionary<LinkStorageSymbol, LinkStorageSymbol>
        _techniquePassTables = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ImportSymbolKey, List<LinkStorageSymbol>> _authoredStorage =
        new(ImportSymbolKeyComparer.Instance);
    private readonly Dictionary<ImportSymbolKey, List<AuthoredXString>> _authoredXstrings =
        new(ImportSymbolKeyComparer.Instance);
    private readonly Dictionary<ImportSymbolKey, List<LinkAliasCellSymbol>> _authoredAliasCells =
        new(ImportSymbolKeyComparer.Instance);
    private readonly Dictionary<LinkStorageSymbol, List<LinkStorageSymbol>>
        _authoredTechniquePassTables = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LinkStorageSymbol, LinkStorageView>
        _authoredStorageBaselines = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ImportSymbolKey, LinkStorageSymbol>
        _capturedStorageIdentities = new(ImportSymbolKeyComparer.Instance);
    private readonly Dictionary<LinkStorageSymbol, AllocationSymbol>
        _capturedAllocations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LinkStorageSymbol, ImportedStorage>
        _importedStorageBySymbol = new(ReferenceEqualityComparer.Instance);
    private bool _completed;

    public LinkAssetFreezeContext(LinkAssetFrozenIdentityCatalog? catalog = null) =>
        _catalog = catalog ?? new LinkAssetFrozenIdentityCatalog();

    public LinkAssetFrozenIdentityCatalog Catalog => _catalog;

    public LinkAssetFreezeScope Bind(
        BaseAsset importedDefinition,
        ILinkAssetImportResolver? importResolver,
        LinkAssetProviderSourceDisposition disposition)
    {
        EnsureOpen();
        return new LinkAssetFreezeScope(
            this,
            importedDefinition,
            importResolver,
            disposition);
    }

    public void Complete()
    {
        EnsureOpen();
        _completed = true;
        foreach ((ImportSymbolKey key, ImportedStorage storage) in _storage)
        {
            storage.Complete();
            if (storage.IsNew)
                _catalog.AddStorage(key, storage.Symbol);
        }
        foreach ((ImportSymbolKey key, LinkStorageSymbol storage) in _xstrings)
            _catalog.AddXString(key, storage);
        foreach ((ImportSymbolKey key, LinkAliasCellSymbol alias) in _aliasCells)
            _catalog.AddAliasCell(key, alias);
        foreach ((LinkStorageSymbol technique, LinkStorageSymbol passTable) in
            _techniquePassTables)
        {
            _catalog.AddTechniquePassTable(technique, passTable);
        }
        _storage.Clear();
        _xstrings.Clear();
        _xstringTemplates.Clear();
        _aliasCells.Clear();
        _techniquePassTables.Clear();
        _authoredStorage.Clear();
        _authoredXstrings.Clear();
        _authoredAliasCells.Clear();
        _authoredTechniquePassTables.Clear();
        _authoredStorageBaselines.Clear();
        _capturedStorageIdentities.Clear();
        _capturedAllocations.Clear();
        _importedStorageBySymbol.Clear();
    }

    internal LinkStorageSymbol FreezeTechniquePassTable(
        LinkAssetFreezeScope scope,
        LinkStorageSymbol technique,
        LinkStorageSymbol candidate,
        string fieldPath)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(technique);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        LinkStorageSymbol identityTechnique =
            _authoredStorageBaselines.TryGetValue(
                technique,
                out LinkStorageView baselineTechnique) &&
            baselineTechnique.CompositeRange is null &&
            baselineTechnique.Addend == 0 &&
            baselineTechnique.Length ==
                baselineTechnique.Storage.Definition.ByteLength
                ? baselineTechnique.Storage
                : technique;
        if (_techniquePassTables.TryGetValue(
                identityTechnique,
                out LinkStorageSymbol? existing) ||
            _catalog.TryGetTechniquePassTable(identityTechnique, out existing))
        {
            if (EquivalentStorage(existing, candidate))
                return existing;
            if (!scope.IsAuthoredDetached)
                ThrowCompetingTechniquePassTable(fieldPath);
        }

        if (scope.IsAuthoredDetached)
        {
            if (!_authoredTechniquePassTables.TryGetValue(
                    identityTechnique,
                    out List<LinkStorageSymbol>? authored))
            {
                authored = [];
                _authoredTechniquePassTables.Add(identityTechnique, authored);
            }

            LinkStorageSymbol? equivalent = authored.FirstOrDefault(
                value => EquivalentStorage(value, candidate));
            if (equivalent is not null)
                return equivalent;

            authored.Add(candidate);
            return candidate;
        }

        _techniquePassTables.Add(identityTechnique, candidate);
        return candidate;
    }

    private static bool EquivalentStorage(
        LinkStorageSymbol existing,
        LinkStorageSymbol candidate)
    {
        LinkStorageDefinition x = existing.Definition;
        LinkStorageDefinition y = candidate.Definition;
        if (x.Block != y.Block ||
            x.ByteLength != y.ByteLength ||
            x.Alignment != y.Alignment ||
            x.Kind != y.Kind ||
            !x.SourceTemplate.Span.SequenceEqual(y.SourceTemplate.Span) ||
            x.Operations.Count != y.Operations.Count)
        {
            return false;
        }

        for (int index = 0; index < x.Operations.Count; index++)
        {
            if (!ImportedStorage.Equivalent(
                    x.Operations[index],
                    y.Operations[index],
                    existing,
                    candidate))
                return false;
        }

        return true;
    }

    [DoesNotReturn]
    private static void ThrowCompetingTechniquePassTable(string fieldPath) =>
        throw new InvalidDataException(
            $"{fieldPath} assigns competing pass-table storage to one MaterialTechnique identity.");

    internal LinkStorageTarget FreezeStorage(
        LinkAssetFreezeScope scope,
        ILinkAssetImportResolver resolver,
        AllocationReference captured,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        bool allowStandaloneDetach,
        string fieldPath)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(scope);
        AllocationEvent allocation = captured.Symbol.Allocation;
        if (allocation.DestinationBlock != block)
        {
            throw new InvalidDataException(
                $"{fieldPath} captured {allocation.DestinationBlock} storage, not {block}.");
        }
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        // The requested alignment governs fresh materialization.  An imported
        // allocation keeps the oracle's physical alignment because one block
        // can have differently typed views.  Stock patch_mp, for example,
        // shares a 2-byte-aligned Weapon hideTags table with the 4-byte-wide
        // WorldGunModels view of the same 0x40 bytes.
        if (captured.Addend < 0 ||
            captured.Addend > allocation.Length - sourceTemplate.Length)
        {
            throw new InvalidDataException(
                $"{fieldPath} semantic bytes lie outside captured storage.");
        }

        ImportSymbolKey key = KeyFor(resolver, captured.Symbol.Occurrence);
        if (scope.IsAuthoredDetached)
        {
            if (_catalog.TryGetStorage(key, out LinkStorageSymbol? frozen))
            {
                RememberCapturedStorage(key, frozen, captured.Symbol, fieldPath);
                return FreezeAuthoredStorage(
                    key,
                    captured,
                    frozen,
                    sourceTemplate,
                    block,
                    alignment,
                    operations,
                    allowStandaloneDetach,
                    fieldPath);
            }

            return FreshStorage(
                sourceTemplate,
                block,
                alignment,
                operations,
                fieldPath);
        }

        if (!_storage.TryGetValue(key, out ImportedStorage? imported))
        {
            imported = _catalog.TryGetStorage(key, out LinkStorageSymbol? existing)
                ? new ImportedStorage(existing, fieldPath)
                : new ImportedStorage(captured.Symbol, fieldPath);
            _storage.Add(key, imported);
            _importedStorageBySymbol.TryAdd(imported.Symbol, imported);
        }
        RememberCapturedStorage(key, imported.Symbol, captured.Symbol, fieldPath);

        imported.Contribute(
            captured.Addend,
            sourceTemplate,
            operations,
            fieldPath);
        return new LinkStorageTarget(
            new LinkStorageView(
                imported.Symbol,
                captured.Addend,
                sourceTemplate.Length),
            CanMaterializeRoot:
                captured.Addend == 0 && sourceTemplate.Length == allocation.Length);
    }

    private void RememberCapturedStorage(
        ImportSymbolKey key,
        LinkStorageSymbol storage,
        AllocationSymbol allocation,
        string fieldPath)
    {
        if (_capturedStorageIdentities.TryGetValue(
                key,
                out LinkStorageSymbol? existingStorage))
        {
            if (!ReferenceEquals(existingStorage, storage))
            {
                throw new InvalidDataException(
                    $"{fieldPath} selected competing storage for one captured identity.");
            }
        }
        else
        {
            _capturedStorageIdentities.Add(key, storage);
        }

        if (_capturedAllocations.TryGetValue(
                storage,
                out AllocationSymbol? existingAllocation))
        {
            if (!ReferenceEquals(existingAllocation, allocation))
            {
                throw new InvalidDataException(
                    $"{fieldPath} selected one storage symbol for competing captured allocations.");
            }
        }
        else
        {
            _capturedAllocations.Add(storage, allocation);
        }
    }

    internal void ValidateReusedStorage(
        ILinkAssetImportResolver resolver,
        AllocationReference captured,
        LinkStorageSymbol reused,
        string fieldPath)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(reused);
        AllocationEvent allocation = captured.Symbol.Allocation;
        LinkStorageDefinition definition = reused.Definition;
        LinkStorageSymbol capturedIdentity = reused;
        if (_authoredStorageBaselines.TryGetValue(
                reused,
                out LinkStorageView baseline))
        {
            if (baseline.Addend != 0 ||
                baseline.Length != definition.ByteLength)
            {
                throw new InvalidDataException(
                    $"{fieldPath} reused an authored storage view with a different captured shape.");
            }
            capturedIdentity = baseline.Storage;
        }

        if (allocation.DestinationBlock != definition.Block ||
            captured.Addend != 0 ||
            allocation.Length != definition.ByteLength)
        {
            throw new InvalidDataException(
                $"{fieldPath} reused one semantic node through a different captured storage allocation.");
        }

        RememberCapturedStorage(
            KeyFor(resolver, captured.Symbol.Occurrence),
            capturedIdentity,
            captured.Symbol,
            fieldPath);
    }

    private LinkStorageTarget FreezeAuthoredStorage(
        ImportSymbolKey importKey,
        AllocationReference captured,
        LinkStorageSymbol frozen,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        bool allowStandaloneDetach,
        string fieldPath)
    {
        var importedView = new LinkStorageView(
            frozen,
            captured.Addend,
            sourceTemplate.Length);
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? candidateOperations =
            BindAuthoredOperations(operations, importedView);
        LinkStorageTarget candidate = FreshStorage(
            sourceTemplate,
            block,
            alignment,
            candidateOperations,
            fieldPath);
        _authoredStorageBaselines.TryAdd(candidate.View.Storage, importedView);
        if (StorageRangeMatches(importedView, candidate.View.Storage))
        {
            return StorageTarget(frozen, captured, sourceTemplate.Length);
        }

        AllocationEvent allocation = captured.Symbol.Allocation;
        if ((captured.Addend != 0 || sourceTemplate.Length != allocation.Length) &&
            !allowStandaloneDetach)
        {
            throw new NotSupportedException(
                $"{fieldPath} changes an interior imported storage view. " +
                "This plan has not established that the view can become a standalone allocation.");
        }

        return ReuseAuthoredStorage(importKey, candidate);
    }

    private Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>?
        BindAuthoredOperations(
            Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
            LinkStorageView baseline)
    {
        return operations is null
            ? null
            : (owner, addend) =>
            {
                _authoredStorageBaselines.Add(owner, baseline);
                return operations(owner, addend);
            };
    }

    private LinkStorageTarget ReuseAuthoredStorage(
        ImportSymbolKey importKey,
        LinkStorageTarget candidate)
    {
        if (!_authoredStorage.TryGetValue(
                importKey,
                out List<LinkStorageSymbol>? authored))
        {
            authored = [];
            _authoredStorage.Add(importKey, authored);
        }

        LinkStorageSymbol? equivalent = authored.FirstOrDefault(
            value => EquivalentStorage(value, candidate.View.Storage));
        if (equivalent is not null)
        {
            return new LinkStorageTarget(
                LinkStorageView.Whole(equivalent),
                CanMaterializeRoot: true);
        }

        authored.Add(candidate.View.Storage);
        return candidate;
    }

    private static LinkStorageTarget StorageTarget(
        LinkStorageSymbol storage,
        AllocationReference captured,
        int semanticLength)
    {
        AllocationEvent allocation = captured.Symbol.Allocation;
        return new LinkStorageTarget(
            new LinkStorageView(storage, captured.Addend, semanticLength),
            CanMaterializeRoot:
                captured.Addend == 0 && semanticLength == allocation.Length);
    }

    internal LinkStorageTarget ResolveStorageRange(
        ILinkAssetImportResolver resolver,
        IReadOnlyList<AllocationReference> capturedRange,
        int requiredLength,
        XFileBlockType block,
        string fieldPath)
    {
        EnsureOpen();
        if (requiredLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredLength));
        if (capturedRange.Count == 0)
            throw new InvalidDataException($"{fieldPath} has no captured direct-storage range.");

        int covered = 0;
        var segments = new List<LinkStorageView>();
        foreach ((AllocationReference captured, int index) in
            capturedRange.Select((value, index) => (value, index)))
        {
            AllocationEvent allocation = captured.Symbol.Allocation;
            if (allocation.DestinationBlock != block)
            {
                throw new InvalidDataException(
                    $"{fieldPath} captured {allocation.DestinationBlock} storage, not {block}.");
            }
            if (captured.Addend < 0 || captured.Addend >= allocation.Length)
                throw new InvalidDataException($"{fieldPath} captured an invalid storage addend.");

            ImportSymbolKey key = KeyFor(resolver, captured.Symbol.Occurrence);
            if (!_storage.TryGetValue(key, out ImportedStorage? imported))
            {
                imported = _catalog.TryGetStorage(key, out LinkStorageSymbol? existing)
                    ? new ImportedStorage(existing, fieldPath)
                    : new ImportedStorage(captured.Symbol, fieldPath);
                _storage.Add(key, imported);
                _importedStorageBySymbol.TryAdd(imported.Symbol, imported);
            }
            RememberCapturedStorage(key, imported.Symbol, captured.Symbol, fieldPath);

            int available = allocation.Length - captured.Addend;
            int used = Math.Min(requiredLength - covered, available);
            segments.Add(new LinkStorageView(
                imported.Symbol,
                captured.Addend,
                used));
            covered = checked(covered + used);
            if (covered == requiredLength)
                break;
        }
        if (covered != requiredLength || segments.Count == 0)
        {
            throw new InvalidDataException(
                $"{fieldPath} captured storage covers 0x{covered:X} of 0x{requiredLength:X} bytes.");
        }

        LinkStorageView view = segments.Count == 1
            ? segments[0]
            : LinkStorageView.Composite(segments, requiredLength);
        return new LinkStorageTarget(view, CanMaterializeRoot: false);
    }

    internal LinkStorageTarget FreezeStorageRange(
        LinkAssetFreezeScope scope,
        ILinkAssetImportResolver resolver,
        IReadOnlyList<AllocationReference> capturedRange,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (sourceTemplate.Length == 0)
            throw new ArgumentException("Semantic storage cannot be empty.", nameof(sourceTemplate));

        if (scope.IsAuthoredDetached && capturedRange.Any(captured =>
                !_catalog.TryGetStorage(
                    KeyFor(resolver, captured.Symbol.Occurrence),
                    out _)))
        {
            return FreshStorage(
                sourceTemplate,
                block,
                alignment,
                operations,
                fieldPath);
        }

        LinkStorageTarget imported = ResolveStorageRange(
            resolver,
            capturedRange,
            sourceTemplate.Length,
            block,
            fieldPath);
        if (!scope.IsAuthoredDetached)
            return imported;

        LinkStorageTarget candidate = FreshStorage(
            sourceTemplate,
            block,
            alignment,
            BindAuthoredOperations(operations, imported.View),
            fieldPath);
        _authoredStorageBaselines.TryAdd(candidate.View.Storage, imported.View);
        if (StorageRangeMatches(imported.View, candidate.View.Storage))
            return imported;

        ImportSymbolKey key = KeyFor(
            resolver,
            capturedRange[0].Symbol.Occurrence);
        return ReuseAuthoredStorage(key, candidate);
    }

    internal LinkStorageTarget FreezeContainedStorageView(
        LinkAssetFreezeScope scope,
        ILinkAssetImportResolver? resolver,
        SymbolReference? capturedTarget,
        LinkStorageTarget canonical,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        bool allowCapturedEndBoundary,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (canonical.View.Length != sourceTemplate.Length)
        {
            throw new InvalidDataException(
                $"{fieldPath} semantic bytes do not cover its canonical contained view.");
        }
        if (canonical.View.Storage.Definition.Block != block)
        {
            throw new InvalidDataException(
                $"{fieldPath} canonical contained view is in " +
                $"{canonical.View.Storage.Definition.Block}, not {block}.");
        }

        LinkStorageTarget candidate = FreshStorage(
            sourceTemplate,
            block,
            alignment,
            operations,
            fieldPath);
        if (!CanonicalStorageRangeMatches(
                canonical.View,
                candidate.View.Storage))
        {
            throw new InvalidDataException(
                $"{fieldPath} semantic bytes or relocations disagree with its canonical root slice.");
        }

        if (capturedTarget is null)
            return canonical;

        LinkStorageView baseline = BaselineView(canonical.View, fieldPath);
        switch (capturedTarget)
        {
            case AllocationReference captured:
            {
                if (resolver is null)
                {
                    throw new InvalidDataException(
                        $"{fieldPath} captured contained storage has no import resolver.");
                }
                AllocationEvent allocation = captured.Symbol.Allocation;
                if (allocation.DestinationBlock != block ||
                    captured.Addend < 0 ||
                    captured.Addend > allocation.Length - sourceTemplate.Length ||
                    captured.Addend == allocation.Length &&
                    sourceTemplate.Length == 0 &&
                    !captured.AllowsEndAddress)
                {
                    throw new InvalidDataException(
                        $"{fieldPath} captured pointer is outside its contained storage.");
                }

                ImportSymbolKey key = KeyFor(resolver, captured.Symbol.Occurrence);
                if (!_capturedStorageIdentities.TryGetValue(
                        key,
                        out LinkStorageSymbol? capturedStorage))
                {
                    if (!_catalog.TryGetStorage(key, out capturedStorage))
                    {
                        throw new InvalidDataException(
                            $"{fieldPath} captured contained storage has no frozen root identity.");
                    }
                    RememberCapturedStorage(
                        key,
                        capturedStorage,
                        captured.Symbol,
                        fieldPath);
                }

                var capturedView = new LinkStorageView(
                    capturedStorage,
                    captured.Addend,
                    sourceTemplate.Length);
                if (capturedView != baseline)
                {
                    throw new InvalidDataException(
                        $"{fieldPath} captured pointer does not select its canonical root slice.");
                }
                break;
            }
            case BoundaryReference boundary when
                allowCapturedEndBoundary && sourceTemplate.Length == 0:
            {
                if (baseline.CompositeRange is not null ||
                    !_capturedAllocations.TryGetValue(
                        baseline.Storage,
                        out AllocationSymbol? allocationSymbol))
                {
                    throw new InvalidDataException(
                        $"{fieldPath} zero-length boundary has no captured canonical root allocation.");
                }

                AllocationEvent allocation = allocationSymbol.Allocation;
                BoundaryEvent captured = boundary.Symbol.Boundary;
                if (captured.DestinationBlock != allocation.DestinationBlock ||
                    captured.TempEpoch != allocation.TempEpoch ||
                    captured.DestinationOffset != checked(
                        allocation.DestinationOffset + baseline.Addend))
                {
                    throw new InvalidDataException(
                        $"{fieldPath} boundary does not select its canonical zero-length root slice.");
                }
                break;
            }
            default:
                throw new InvalidDataException(
                    $"{fieldPath} captured pointer does not target contained direct storage.");
        }

        return canonical;
    }

    private bool CanonicalStorageRangeMatches(
        LinkStorageView canonical,
        LinkStorageSymbol candidate)
    {
        if (canonical.CompositeRange is null &&
            _importedStorageBySymbol.TryGetValue(
                canonical.Storage,
                out ImportedStorage? imported) &&
            imported.IsNew)
        {
            return imported.ViewMatches(
                canonical.Addend,
                candidate);
        }

        return StorageRangeMatches(canonical, candidate);
    }

    private LinkStorageView BaselineView(
        LinkStorageView canonical,
        string fieldPath)
    {
        if (!_authoredStorageBaselines.TryGetValue(
                canonical.Storage,
                out LinkStorageView baselineOwner))
        {
            return canonical;
        }
        if (canonical.CompositeRange is not null ||
            baselineOwner.CompositeRange is not null)
        {
            throw new NotSupportedException(
                $"{fieldPath} cannot derive a contained slice across a composite detached root.");
        }

        return new LinkStorageView(
            baselineOwner.Storage,
            checked(baselineOwner.Addend + canonical.Addend),
            canonical.Length);
    }

    private static bool StorageRangeMatches(
        LinkStorageView imported,
        LinkStorageSymbol candidate)
    {
        IReadOnlyList<LinkStorageView> segments =
            imported.CompositeRange?.Segments ?? [imported];
        LinkStorageDefinition candidateDefinition = candidate.Definition;
        if (candidateDefinition.Kind != LinkMaterializationKind.SourceBytes ||
            candidateDefinition.ByteLength != segments.Sum(segment => segment.Length))
        {
            return false;
        }

        int logicalOffset = 0;
        foreach (LinkStorageView segment in segments)
        {
            LinkStorageDefinition definition = segment.Storage.Definition;
            if (definition.Kind != LinkMaterializationKind.SourceBytes ||
                !definition.SourceTemplate.Span
                    .Slice(segment.Addend, segment.Length)
                    .SequenceEqual(candidateDefinition.SourceTemplate.Span
                        .Slice(logicalOffset, segment.Length)))
            {
                return false;
            }
            logicalOffset = checked(logicalOffset + segment.Length);
        }

        var importedOperations = new List<(LinkOperation Operation, int? CellOffset)>();
        logicalOffset = 0;
        foreach (LinkStorageView segment in segments)
        {
            LinkStorageDefinition definition = segment.Storage.Definition;
            bool whole = segment.Addend == 0 &&
                segment.Length == definition.ByteLength;
            int segmentEnd = checked(segment.Addend + segment.Length);
            foreach (LinkOperation operation in definition.Operations)
            {
                if (!TryGetOperationCell(operation, out LinkStorageCell cell))
                {
                    if (!whole)
                        return false;
                    importedOperations.Add((operation, null));
                    continue;
                }
                if (!ReferenceEquals(cell.Owner, segment.Storage) ||
                    !TryGetOperationCellWidth(operation, out int cellWidth))
                {
                    return false;
                }

                int cellEnd = checked(cell.Offset + cellWidth);
                bool overlaps = cell.Offset < segmentEnd && cellEnd > segment.Addend;
                if (!overlaps)
                    continue;
                if (cell.Offset < segment.Addend || cellEnd > segmentEnd)
                    return false;
                importedOperations.Add((
                    operation,
                    checked(logicalOffset + cell.Offset - segment.Addend)));
            }
            logicalOffset = checked(logicalOffset + segment.Length);
        }

        IReadOnlyList<LinkOperation> candidateOperations = candidateDefinition.Operations;
        if (importedOperations.Count != candidateOperations.Count)
            return false;
        for (int index = 0; index < importedOperations.Count; index++)
        {
            (LinkOperation operation, int? cellOffset) = importedOperations[index];
            LinkOperation authored = candidateOperations[index];
            if (cellOffset is { } offset)
            {
                if (!TryGetOperationCell(authored, out LinkStorageCell authoredCell) ||
                    !ReferenceEquals(authoredCell.Owner, candidate) ||
                    authoredCell.Offset != offset ||
                    !EquivalentOperationPayload(operation, authored))
                {
                    return false;
                }
            }
            else if (!ImportedStorage.Equivalent(operation, authored))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EquivalentOperationPayload(
        LinkOperation left,
        LinkOperation right) =>
        (left, right) switch
        {
            (DirectStorageLinkOperation x, DirectStorageLinkOperation y) =>
                EquivalentView(x.Target, y.Target) &&
                x.CanMaterializeRoot == y.CanMaterializeRoot,
            (PresenceStorageLinkOperation x, PresenceStorageLinkOperation y) =>
                EquivalentView(x.Target, y.Target),
            (XStringLinkOperation x, XStringLinkOperation y) =>
                EquivalentView(x.Target, y.Target) &&
                x.CanMaterializeRoot == y.CanMaterializeRoot,
            (ProviderLinkOperation x, ProviderLinkOperation y) =>
                x.Dependency.Key == y.Dependency.Key &&
                x.Dependency.SerializedType == y.Dependency.SerializedType,
            (AliasCellStorageLinkOperation x, AliasCellStorageLinkOperation y) =>
                ReferenceEquals(x.AliasCell, y.AliasCell),
            (ScriptStringLinkOperation x, ScriptStringLinkOperation y) =>
                x.Text == y.Text,
            _ => false
        };

    private static bool EquivalentView(
        LinkStorageView left,
        LinkStorageView right)
    {
        if (!ReferenceEquals(left.Storage, right.Storage) ||
            left.Addend != right.Addend ||
            left.Length != right.Length)
        {
            return false;
        }

        IReadOnlyList<LinkStorageView>? x = left.CompositeRange?.Segments;
        IReadOnlyList<LinkStorageView>? y = right.CompositeRange?.Segments;
        if (x is null || y is null)
            return x is null && y is null;
        return x.SequenceEqual(y);
    }

    private static bool TryGetOperationCell(
        LinkOperation operation,
        out LinkStorageCell cell)
    {
        switch (operation)
        {
            case DirectStorageLinkOperation value:
                cell = value.Cell;
                return true;
            case PresenceStorageLinkOperation value:
                cell = value.Cell;
                return true;
            case XStringLinkOperation value:
                cell = value.Cell;
                return true;
            case ProviderLinkOperation value:
                cell = value.Cell;
                return true;
            case AliasCellStorageLinkOperation value:
                cell = value.Cell;
                return true;
            case ScriptStringLinkOperation value:
                cell = value.Cell;
                return true;
            default:
                cell = default;
                return false;
        }
    }

    private static bool TryGetOperationCellWidth(
        LinkOperation operation,
        out int width)
    {
        if (operation is ScriptStringLinkOperation)
        {
            width = sizeof(ushort);
            return true;
        }
        if (TryGetOperationCell(operation, out _))
        {
            width = sizeof(int);
            return true;
        }

        width = 0;
        return false;
    }

    internal LinkStorageSymbol FreezeXString(
        LinkAssetFreezeScope scope,
        ILinkAssetImportResolver resolver,
        XStringReference captured,
        ReadOnlySpan<byte> sourceTemplate,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var allocation = new AllocationReference(
            captured.Symbol.Allocation,
            captured.Addend);
        ImportSymbolKey key = KeyFor(resolver, captured.Symbol.Occurrence);
        byte[] bytes = sourceTemplate.ToArray();
        if (scope.IsAuthoredDetached)
        {
            return FreezeAuthoredXString(
                key,
                bytes,
                fieldPath);
        }

        if (_xstringTemplates.TryGetValue(key, out byte[]? activeBytes))
        {
            if (!activeBytes.AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException(
                    $"{fieldPath} assigns competing text to one captured XString identity.");
            }
        }
        else if (_catalog.TryGetXString(
                     key,
                     out LinkStorageSymbol? importedStorage))
        {
            if (!importedStorage.Definition.SourceTemplate.Span.SequenceEqual(bytes))
            {
                throw new InvalidDataException(
                    $"{fieldPath} assigns competing text to one captured XString identity.");
            }
        }
        else
        {
            _xstringTemplates.Add(key, bytes);
        }

        AllocationEvent capturedAllocation = allocation.Symbol.Allocation;
        if (allocation.Addend != 0)
        {
            throw new InvalidDataException(
                $"{fieldPath} captured XString begins at a nonzero allocation addend.");
        }
        if (capturedAllocation.Length != bytes.Length)
        {
            throw new InvalidDataException(
                $"{fieldPath} semantic XString occupies 0x{bytes.Length:X} bytes, " +
                $"but its captured allocation occupies 0x{capturedAllocation.Length:X} bytes.");
        }

        LinkStorageTarget storage = FreezeStorage(
            scope,
            resolver,
            allocation,
            bytes,
            XFileBlockType.LARGE,
            alignment: 1,
            operations: null,
            allowStandaloneDetach: false,
            fieldPath);
        if (storage.View.Addend != 0 ||
            storage.View.Length != storage.View.Storage.Definition.ByteLength)
        {
            throw new InvalidDataException(
                $"{fieldPath} captured XString is not a complete physical allocation.");
        }

        if (_xstrings.TryGetValue(key, out LinkStorageSymbol? existing) ||
            _catalog.TryGetXString(key, out existing))
        {
            if (!ReferenceEquals(existing, storage.View.Storage))
                throw new InvalidDataException($"{fieldPath} selected competing XString storage.");
            return existing;
        }

        _xstrings.Add(key, storage.View.Storage);
        return storage.View.Storage;
    }

    private LinkStorageSymbol FreezeAuthoredXString(
        ImportSymbolKey key,
        byte[] bytes,
        string fieldPath)
    {
        if (_catalog.TryGetXString(key, out LinkStorageSymbol? imported) &&
            imported.Definition.SourceTemplate.Span.SequenceEqual(bytes))
        {
            return imported;
        }

        if (!_authoredXstrings.TryGetValue(
                key,
                out List<AuthoredXString>? authored))
        {
            authored = [];
            _authoredXstrings.Add(key, authored);
        }
        AuthoredXString? equivalent = authored.FirstOrDefault(
            value => value.SourceTemplate.AsSpan().SequenceEqual(bytes));
        if (equivalent is not null)
            return equivalent.Storage;

        LinkStorageSymbol replacement = LinkStorageSymbol.CString(bytes);
        authored.Add(new AuthoredXString(bytes, replacement));
        return replacement;
    }

    internal LinkAliasCellSymbol FreezeAliasCell(
        LinkAssetFreezeScope scope,
        ILinkAssetImportResolver resolver,
        AliasCellSymbol capturedAlias,
        AllocationReference capturedStorage,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(scope);
        LinkStorageTarget storage = FreezeStorage(
            scope,
            resolver,
            capturedStorage,
            sourceTemplate,
            block,
            alignment,
            operations,
            allowStandaloneDetach: false,
            fieldPath);
        ImportSymbolKey key = KeyFor(resolver, capturedAlias.Occurrence);
        if (scope.IsAuthoredDetached)
        {
            if (_catalog.TryGetAliasCell(key, out LinkAliasCellSymbol? imported) &&
                imported.Target == storage.View)
                return imported;

            if (!_authoredAliasCells.TryGetValue(
                    key,
                    out List<LinkAliasCellSymbol>? authored))
            {
                authored = [];
                _authoredAliasCells.Add(key, authored);
            }
            LinkAliasCellSymbol? equivalent = authored.FirstOrDefault(
                value => value.Target == storage.View);
            if (equivalent is not null)
                return equivalent;

            var detached = new LinkAliasCellSymbol(storage.View);
            authored.Add(detached);
            return detached;
        }

        if (_aliasCells.TryGetValue(key, out LinkAliasCellSymbol? existing) ||
            _catalog.TryGetAliasCell(key, out existing))
        {
            if (existing.Target != storage.View)
                throw new InvalidDataException($"{fieldPath} selected competing alias-cell targets.");
            return existing;
        }

        var alias = new LinkAliasCellSymbol(storage.View);
        _aliasCells.Add(key, alias);
        return alias;
    }

    private static ImportSymbolKey KeyFor(
        ILinkAssetImportResolver resolver,
        CaptureOccurrence occurrence) =>
        new(
            resolver.IdentityScope ?? throw new InvalidDataException(
                "An import resolver returned no data-free identity scope."),
            occurrence);

    private static LinkStorageTarget FreshStorage(
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        LinkStorageSymbol storage = LinkStorageSymbol.SourceBytes(
            block,
            sourceTemplate,
            alignment,
            symbol => operations?.Invoke(symbol, 0) ?? []);
        return new LinkStorageTarget(
            LinkStorageView.Whole(storage),
            CanMaterializeRoot: true);
    }

    private void EnsureOpen()
    {
        if (_completed)
            throw new InvalidOperationException("The asset pool freeze context is already complete.");
    }

    private sealed record AuthoredXString(
        byte[] SourceTemplate,
        LinkStorageSymbol Storage);

    private sealed class ImportedStorage
    {
        private readonly byte[] _bytes;
        private readonly List<WrittenRange> _writtenRanges = [];
        private readonly List<LinkOperation> _operations = [];
        private readonly Dictionary<int, LinkOperation> _operationsByCell = [];
        private readonly bool _isFrozen;

        public ImportedStorage(AllocationSymbol captured, string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(captured);
            AllocationEvent allocation = captured.Allocation;
            if (allocation.Kind is not (
                MaterializationKind.StreamCopy or
                MaterializationKind.CString))
            {
                throw new NotSupportedException(
                    $"{fieldPath} captured {allocation.Kind} storage cannot provide source bytes.");
            }

            _bytes = new byte[allocation.Length];
            Symbol = LinkStorageSymbol.CreatePendingSourceBytes(
                allocation.DestinationBlock,
                allocation.Length,
                allocation.Alignment);
        }

        public ImportedStorage(LinkStorageSymbol frozen, string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(frozen);
            ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
            if (frozen.Definition.Kind != LinkMaterializationKind.SourceBytes)
            {
                throw new InvalidDataException(
                    $"{fieldPath} selected a source-free symbol for imported source storage.");
            }

            Symbol = frozen;
            _bytes = frozen.Definition.SourceTemplate.ToArray();
            _isFrozen = true;
        }

        public LinkStorageSymbol Symbol { get; }
        public bool IsNew => !_isFrozen;

        public bool ViewMatches(
            int addend,
            LinkStorageSymbol candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (_isFrozen)
            {
                return StorageRangeMatches(
                    new LinkStorageView(
                        Symbol,
                        addend,
                        candidate.Definition.ByteLength),
                    candidate);
            }

            LinkStorageDefinition definition = candidate.Definition;
            int length = definition.ByteLength;
            if (definition.Kind != LinkMaterializationKind.SourceBytes ||
                addend < 0 ||
                addend > _bytes.Length - length)
            {
                return false;
            }
            if (!IsRangeWritten(addend, length) ||
                !_bytes.AsSpan(addend, length)
                    .SequenceEqual(definition.SourceTemplate.Span))
                return false;

            int end = checked(addend + length);
            bool whole = addend == 0 && length == _bytes.Length;
            var operations = new List<(LinkOperation Operation, int? CellOffset)>();
            foreach (LinkOperation operation in _operations)
            {
                if (!TryGetCell(operation, out LinkStorageCell cell))
                {
                    if (!whole)
                        return false;
                    operations.Add((operation, null));
                    continue;
                }
                if (!ReferenceEquals(cell.Owner, Symbol) ||
                    !TryGetCellWidth(operation, out int cellWidth))
                {
                    return false;
                }

                int cellEnd = checked(cell.Offset + cellWidth);
                bool overlaps = cell.Offset < end && cellEnd > addend;
                if (!overlaps)
                    continue;
                if (cell.Offset < addend || cellEnd > end)
                    return false;
                operations.Add((operation, cell.Offset - addend));
            }

            if (operations.Count != definition.Operations.Count)
                return false;
            for (int index = 0; index < operations.Count; index++)
            {
                (LinkOperation operation, int? cellOffset) = operations[index];
                LinkOperation authored = definition.Operations[index];
                if (cellOffset is { } offset)
                {
                    if (!TryGetOperationCell(
                            authored,
                            out LinkStorageCell authoredCell) ||
                        !ReferenceEquals(authoredCell.Owner, candidate) ||
                        authoredCell.Offset != offset ||
                        !EquivalentOperationPayload(operation, authored))
                    {
                        return false;
                    }
                }
                else if (!Equivalent(
                        operation,
                        authored,
                        Symbol,
                        candidate))
                {
                    return false;
                }
            }

            return true;
        }

        public bool ContributionMatches(
            int addend,
            ReadOnlySpan<byte> sourceTemplate,
            Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
            string fieldPath)
        {
            if (!_isFrozen)
            {
                throw new InvalidOperationException(
                    "Only frozen imported storage can be compared for authored copy-on-write.");
            }
            if (addend < 0 || addend > _bytes.Length - sourceTemplate.Length ||
                !_bytes.AsSpan(addend, sourceTemplate.Length).SequenceEqual(sourceTemplate))
            {
                return false;
            }

            LinkOperation[] candidate = (operations?.Invoke(Symbol, addend) ?? [])
                .Select(operation => operation ?? throw new InvalidDataException(
                    $"{fieldPath} produced a null link operation."))
                .ToArray();
            IReadOnlyList<LinkOperation> existing = Symbol.Definition.Operations;
            if (addend == 0 && sourceTemplate.Length == _bytes.Length)
            {
                if (existing.Count != candidate.Length)
                    return false;
                for (int index = 0; index < existing.Count; index++)
                {
                    if (!Equivalent(existing[index], candidate[index]))
                        return false;
                }
                return true;
            }

            int end = checked(addend + sourceTemplate.Length);
            var inRange = new List<LinkOperation>();
            foreach (LinkOperation operation in existing)
            {
                if (!TryGetCell(operation, out LinkStorageCell cell) ||
                    !TryGetCellWidth(operation, out int cellWidth))
                {
                    return false;
                }

                int cellEnd = checked(cell.Offset + cellWidth);
                bool overlaps = cell.Offset < end && cellEnd > addend;
                if (!overlaps)
                    continue;
                if (cell.Offset < addend || cellEnd > end)
                    return false;
                inRange.Add(operation);
            }

            if (inRange.Count != candidate.Length)
                return false;
            for (int index = 0; index < inRange.Count; index++)
            {
                if (!Equivalent(inRange[index], candidate[index]))
                    return false;
            }
            return true;
        }

        public void Contribute(
            int addend,
            ReadOnlySpan<byte> sourceTemplate,
            Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
            string fieldPath)
        {
            if (_isFrozen)
            {
                if (!ContributionMatches(
                        addend,
                        sourceTemplate,
                        operations,
                        fieldPath))
                {
                    throw new InvalidDataException(
                        $"{fieldPath} provides competing semantic bytes or relocations " +
                        "for one frozen imported storage identity.");
                }
                return;
            }

            ContributeBytes(addend, sourceTemplate, fieldPath);

            if (operations is not null)
            {
                foreach (LinkOperation operation in operations(Symbol, addend))
                {
                    if (operation is null)
                        throw new InvalidDataException($"{fieldPath} produced a null link operation.");
                    MergeOperation(operation, fieldPath);
                }
            }
        }

        private void MergeOperation(LinkOperation operation, string fieldPath)
        {
            if (TryGetCell(operation, out LinkStorageCell cell))
            {
                if (!ReferenceEquals(cell.Owner, Symbol))
                    throw new InvalidDataException($"{fieldPath} operation belongs to different storage.");
                if (_operationsByCell.TryGetValue(cell.Offset, out LinkOperation? existing))
                {
                    if (!Equivalent(existing, operation))
                    {
                        throw new InvalidDataException(
                            $"{fieldPath} assigns competing relocations to physical cell +0x{cell.Offset:X}.");
                    }
                    return;
                }

                _operationsByCell.Add(cell.Offset, operation);
                _operations.Add(operation);
                return;
            }

            if (_operations.Any(existing => Equivalent(existing, operation)))
                return;
            _operations.Add(operation);
        }

        private void ContributeBytes(
            int addend,
            ReadOnlySpan<byte> sourceTemplate,
            string fieldPath)
        {
            if (addend < 0 || addend > _bytes.Length - sourceTemplate.Length)
                throw new InvalidDataException($"{fieldPath} lies outside captured storage.");
            if (sourceTemplate.Length == 0)
                return;

            int end = checked(addend + sourceTemplate.Length);
            foreach (WrittenRange range in _writtenRanges)
            {
                if (range.End <= addend)
                    continue;
                if (range.Start >= end)
                    break;

                int overlapStart = Math.Max(addend, range.Start);
                int overlapEnd = Math.Min(end, range.End);
                int overlapLength = overlapEnd - overlapStart;
                if (!_bytes.AsSpan(overlapStart, overlapLength).SequenceEqual(
                        sourceTemplate.Slice(overlapStart - addend, overlapLength)))
                {
                    throw new InvalidDataException(
                        $"{fieldPath} provides conflicting semantic bytes for one captured storage identity.");
                }
            }

            sourceTemplate.CopyTo(_bytes.AsSpan(addend, sourceTemplate.Length));

            int insertionIndex = 0;
            while (insertionIndex < _writtenRanges.Count &&
                   _writtenRanges[insertionIndex].End < addend)
            {
                insertionIndex++;
            }

            int mergedStart = addend;
            int mergedEnd = end;
            while (insertionIndex < _writtenRanges.Count &&
                   _writtenRanges[insertionIndex].Start <= mergedEnd)
            {
                WrittenRange range = _writtenRanges[insertionIndex];
                mergedStart = Math.Min(mergedStart, range.Start);
                mergedEnd = Math.Max(mergedEnd, range.End);
                _writtenRanges.RemoveAt(insertionIndex);
            }
            _writtenRanges.Insert(insertionIndex, new WrittenRange(mergedStart, mergedEnd));
        }

        private bool IsRangeWritten(int addend, int length)
        {
            int end = checked(addend + length);
            return length == 0 || _writtenRanges.Any(range =>
                range.Start <= addend && range.End >= end);
        }

        private static bool TryGetCell(
            LinkOperation operation,
            out LinkStorageCell cell)
        {
            switch (operation)
            {
                case DirectStorageLinkOperation value:
                    cell = value.Cell;
                    return true;
                case PresenceStorageLinkOperation value:
                    cell = value.Cell;
                    return true;
                case XStringLinkOperation value:
                    cell = value.Cell;
                    return true;
                case ProviderLinkOperation value:
                    cell = value.Cell;
                    return true;
                case AliasCellStorageLinkOperation value:
                    cell = value.Cell;
                    return true;
                case ScriptStringLinkOperation value:
                    cell = value.Cell;
                    return true;
                default:
                    cell = default;
                    return false;
            }
        }

        private static bool TryGetCellWidth(
            LinkOperation operation,
            out int width)
        {
            if (operation is ScriptStringLinkOperation)
            {
                width = sizeof(ushort);
                return true;
            }
            if (TryGetCell(operation, out _))
            {
                width = sizeof(int);
                return true;
            }

            width = 0;
            return false;
        }

        internal static bool Equivalent(
            LinkOperation left,
            LinkOperation right,
            LinkStorageSymbol? leftRoot = null,
            LinkStorageSymbol? rightRoot = null) =>
            (left, right) switch
            {
                (DirectStorageLinkOperation x, DirectStorageLinkOperation y) =>
                    Equivalent(x.Cell, y.Cell, leftRoot, rightRoot) &&
                    Equivalent(x.Target, y.Target, leftRoot, rightRoot) &&
                    x.CanMaterializeRoot == y.CanMaterializeRoot,
                (PresenceStorageLinkOperation x, PresenceStorageLinkOperation y) =>
                    Equivalent(x.Cell, y.Cell, leftRoot, rightRoot) &&
                    Equivalent(x.Target, y.Target, leftRoot, rightRoot),
                (XStringLinkOperation x, XStringLinkOperation y) =>
                    Equivalent(x.Cell, y.Cell, leftRoot, rightRoot) &&
                    Equivalent(x.Target, y.Target, leftRoot, rightRoot) &&
                    x.CanMaterializeRoot == y.CanMaterializeRoot,
                (ProviderLinkOperation x, ProviderLinkOperation y) =>
                    Equivalent(x.Cell, y.Cell, leftRoot, rightRoot) &&
                    x.Dependency.Key == y.Dependency.Key &&
                    x.Dependency.SerializedType == y.Dependency.SerializedType,
                (AliasCellStorageLinkOperation x, AliasCellStorageLinkOperation y) =>
                    Equivalent(x.Cell, y.Cell, leftRoot, rightRoot) &&
                    ReferenceEquals(x.AliasCell, y.AliasCell),
                (ScriptStringLinkOperation x, ScriptStringLinkOperation y) =>
                    Equivalent(x.Cell, y.Cell, leftRoot, rightRoot) &&
                    x.Text == y.Text,
                (MaterializeStorageLinkOperation x, MaterializeStorageLinkOperation y) =>
                    ReferenceEquals(x.Storage, y.Storage) ||
                    ReferenceEquals(x.Storage, leftRoot) &&
                    ReferenceEquals(y.Storage, rightRoot),
                (DependencyOnlyLinkOperation x, DependencyOnlyLinkOperation y) =>
                    x.Dependency.Key == y.Dependency.Key &&
                    x.Dependency.SerializedType == y.Dependency.SerializedType,
                _ => false
            };

        private static bool Equivalent(
            LinkStorageCell left,
            LinkStorageCell right,
            LinkStorageSymbol? leftRoot,
            LinkStorageSymbol? rightRoot) =>
            left.Offset == right.Offset &&
            (ReferenceEquals(left.Owner, right.Owner) ||
             ReferenceEquals(left.Owner, leftRoot) &&
             ReferenceEquals(right.Owner, rightRoot));

        private static bool Equivalent(
            LinkStorageView left,
            LinkStorageView right,
            LinkStorageSymbol? leftRoot,
            LinkStorageSymbol? rightRoot)
        {
            bool sameStorage = ReferenceEquals(left.Storage, right.Storage) ||
                ReferenceEquals(left.Storage, leftRoot) &&
                ReferenceEquals(right.Storage, rightRoot);
            if (!sameStorage ||
                left.Addend != right.Addend ||
                left.Length != right.Length)
            {
                return false;
            }

            IReadOnlyList<LinkStorageView>? leftSegments =
                left.CompositeRange?.Segments;
            IReadOnlyList<LinkStorageView>? rightSegments =
                right.CompositeRange?.Segments;
            if (leftSegments is null || rightSegments is null)
                return leftSegments is null && rightSegments is null;
            if (leftSegments.Count != rightSegments.Count)
                return false;

            for (int index = 0; index < leftSegments.Count; index++)
            {
                LinkStorageView x = leftSegments[index];
                LinkStorageView y = rightSegments[index];
                if (!ReferenceEquals(x.Storage, y.Storage) ||
                    x.Addend != y.Addend ||
                    x.Length != y.Length)
                {
                    return false;
                }
            }

            return true;
        }

        public void Complete()
        {
            if (_isFrozen)
                return;

            int missing = FirstMissingByte();
            if (missing >= 0)
            {
                throw new NotSupportedException(
                    $"Captured storage is missing semantic byte coverage at +0x{missing:X}.");
            }

            Symbol.FreezeSourceBytes(_bytes, _operations);
        }

        private int FirstMissingByte()
        {
            int covered = 0;
            foreach (WrittenRange range in _writtenRanges)
            {
                if (range.Start > covered)
                    return covered;
                covered = Math.Max(covered, range.End);
            }
            return covered == _bytes.Length ? -1 : covered;
        }

        private readonly record struct WrittenRange(int Start, int End);
    }

}

internal readonly record struct ImportSymbolKey(
    LinkAssetImportIdentityScope Scope,
    CaptureOccurrence Occurrence);

internal sealed class ImportSymbolKeyComparer : IEqualityComparer<ImportSymbolKey>
{
    public static ImportSymbolKeyComparer Instance { get; } = new();

    public bool Equals(ImportSymbolKey x, ImportSymbolKey y) =>
        ReferenceEquals(x.Scope, y.Scope) && x.Occurrence == y.Occurrence;

    public int GetHashCode(ImportSymbolKey obj) =>
        HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Scope),
            obj.Occurrence);
}

internal sealed class LinkAssetFreezeScope
{
    private readonly LinkAssetFreezeContext _context;
    private readonly BaseAsset _importedDefinition;
    private readonly ILinkAssetImportResolver? _resolver;
    private readonly LinkAssetProviderSourceDisposition _disposition;

    internal LinkAssetFreezeScope(
        LinkAssetFreezeContext context,
        BaseAsset importedDefinition,
        ILinkAssetImportResolver? resolver,
        LinkAssetProviderSourceDisposition disposition)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _importedDefinition = importedDefinition ?? throw new ArgumentNullException(
            nameof(importedDefinition));
        _resolver = resolver;
        _disposition = disposition;
    }

    internal bool IsAuthoredDetached =>
        _disposition == LinkAssetProviderSourceDisposition.AuthoredDetached;

    public LinkStorageSymbol FreezeProviderName(
        string value,
        int providerRootOffset,
        string fieldPath) =>
        FreezeXString(
            value,
            pointer: null,
            providerRootOffset,
            fieldPath);

    public LinkStorageSymbol FreezeTechniquePassTable(
        LinkStorageSymbol technique,
        LinkStorageSymbol candidate,
        string fieldPath) =>
        _context.FreezeTechniquePassTable(
            this,
            technique,
            candidate,
            fieldPath);

    public LinkStorageSymbol? FreezeOptionalXString(
        string? value,
        XPointerReference pointer,
        string fieldPath) =>
        value is null
            ? null
            : FreezeXString(
                value,
                pointer,
                providerRootOffset: null,
                fieldPath);

    public LinkStorageSymbol FreezeRequiredXString(
        string value,
        XPointerReference pointer,
        string fieldPath) =>
        FreezeXString(
            value,
            pointer,
            providerRootOffset: null,
            fieldPath);

    public LinkStorageTarget FreezeStorage(
        XPointerReference pointer,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath)
        => FreezeStorageCore(
            pointer,
            sourceTemplate,
            block,
            alignment,
            operations,
            requireCompleteAllocation: true,
            allowStandaloneDetach: false,
            fieldPath);

    public void ValidateReusedStorage(
        XPointerReference pointer,
        LinkStorageSymbol reused,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(reused);
        if (_resolver is null)
        {
            if (!IsAuthoredDetached && pointer.CellAddress is not null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains an imported pointer cell but has no capture resolver.");
            }
            return;
        }
        if (pointer.CellAddress is null)
            return;

        PointerRelocation relocation = _resolver.ResolvePointer(
            _importedDefinition,
            pointer,
            fieldPath);
        if (relocation.Target is not AllocationReference captured)
        {
            throw new InvalidDataException(
                $"{fieldPath} reused semantic storage through a non-storage captured pointer.");
        }

        _context.ValidateReusedStorage(
            _resolver,
            captured,
            reused,
            fieldPath);
    }

    public LinkStorageTarget FreezeStorageView(
        XPointerReference pointer,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath,
        bool allowStandaloneDetach = false)
        => FreezeStorageCore(
            pointer,
            sourceTemplate,
            block,
            alignment,
            operations,
            requireCompleteAllocation: false,
            allowStandaloneDetach,
            fieldPath);

    public LinkStorageTarget FreezeContainedStorageView(
        XPointerReference pointer,
        LinkStorageTarget canonical,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath,
        bool allowCapturedEndBoundary = false)
    {
        SymbolReference? capturedTarget = null;
        if (_resolver is null)
        {
            if (!IsAuthoredDetached && pointer.CellAddress is not null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains an imported pointer cell but has no capture resolver.");
            }
        }
        else if (pointer.CellAddress is not null)
        {
            PointerRelocation relocation = _resolver.ResolvePointer(
                _importedDefinition,
                pointer,
                fieldPath);
            if (relocation.Target is null)
            {
                throw new InvalidDataException(
                    $"{fieldPath} imported contained storage resolves to a null captured pointer.");
            }
            capturedTarget = relocation.Target;
        }

        return _context.FreezeContainedStorageView(
            this,
            _resolver,
            capturedTarget,
            canonical,
            sourceTemplate,
            block,
            alignment,
            operations,
            allowCapturedEndBoundary,
            fieldPath);
    }

    private LinkStorageTarget FreezeStorageCore(
        XPointerReference pointer,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        bool requireCompleteAllocation,
        bool allowStandaloneDetach,
        string fieldPath)
    {
        if (_resolver is null)
        {
            if (!IsAuthoredDetached && pointer.CellAddress is not null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains an imported pointer cell but has no capture resolver.");
            }

            return FreshStorage(
                sourceTemplate,
                block,
                alignment,
                operations,
                fieldPath);
        }
        if (pointer.CellAddress is null)
        {
            return FreshStorage(
                sourceTemplate,
                block,
                alignment,
                operations,
                fieldPath);
        }

        PointerRelocation relocation = _resolver.ResolvePointer(
            _importedDefinition,
            pointer,
            fieldPath);
        if (relocation.Target is not AllocationReference captured)
        {
            if (relocation.Target is null)
            {
                throw new InvalidDataException(
                    $"{fieldPath} imported non-null semantic storage resolves to a null captured pointer.");
            }

            throw new InvalidDataException(
                $"{fieldPath} captured {relocation.Target.GetType().Name}, not direct storage.");
        }

        if (requireCompleteAllocation &&
            (captured.Addend != 0 ||
             captured.Symbol.Allocation.Length != sourceTemplate.Length))
        {
            return _context.FreezeStorage(
                this,
                _resolver,
                captured,
                sourceTemplate,
                block,
                alignment,
                operations,
                allowStandaloneDetach: IsAuthoredDetached,
                fieldPath);
        }

        return _context.FreezeStorage(
            this,
            _resolver,
            captured,
            sourceTemplate,
            block,
            alignment,
            operations,
            allowStandaloneDetach,
            fieldPath);
    }

    public LinkStorageTarget ResolveStorage(
        XPointerReference pointer,
        int requiredLength,
        XFileBlockType block,
        string fieldPath)
    {
        if (requiredLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredLength));
        if (IsAuthoredDetached)
        {
            throw new NotSupportedException(
                $"{fieldPath} retains offset-only imported storage without a semantic " +
                "copy-on-write body. Authored providers cannot silently reuse that data.");
        }
        if (_resolver is null)
        {
            throw new NotSupportedException(
                $"{fieldPath} has no imported direct-storage identity to resolve.");
        }

        PointerRelocation relocation = _resolver.ResolvePointer(
            _importedDefinition,
            pointer,
            fieldPath);
        IReadOnlyList<AllocationReference> range =
            _resolver.ResolveDirectStorageRange(
                relocation,
                requiredLength,
                fieldPath);
        return _context.ResolveStorageRange(
            _resolver,
            range,
            requiredLength,
            block,
            fieldPath);
    }

    public LinkStorageTarget FreezeStorageRange(
        XPointerReference pointer,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath)
    {
        if (sourceTemplate.Length == 0)
            throw new ArgumentException("Semantic storage cannot be empty.", nameof(sourceTemplate));
        if (_resolver is null)
        {
            if (!IsAuthoredDetached && pointer.CellAddress is not null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains an imported pointer cell but has no capture resolver.");
            }
            return FreshStorage(
                sourceTemplate,
                block,
                alignment,
                operations,
                fieldPath);
        }
        if (pointer.CellAddress is null)
        {
            return FreshStorage(
                sourceTemplate,
                block,
                alignment,
                operations,
                fieldPath);
        }

        PointerRelocation relocation = _resolver.ResolvePointer(
            _importedDefinition,
            pointer,
            fieldPath);
        IReadOnlyList<AllocationReference> range =
            _resolver.ResolveDirectStorageRange(
                relocation,
                sourceTemplate.Length,
                fieldPath);
        return _context.FreezeStorageRange(
            this,
            _resolver,
            range,
            sourceTemplate,
            block,
            alignment,
            operations,
            fieldPath);
    }

    public LinkAliasCellSymbol FreezeAliasCellStorage(
        XPointerReference pointer,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath)
        => FreezeAliasCellStorageCore(
            pointer,
            sourceTemplate,
            block,
            alignment,
            operations,
            fieldPath);

    private LinkAliasCellSymbol FreezeAliasCellStorageCore(
        XPointerReference pointer,
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath)
    {
        if (_resolver is null)
        {
            if (!IsAuthoredDetached && pointer.CellAddress is not null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains an imported pointer cell but has no capture resolver.");
            }

            LinkStorageTarget fresh = FreshStorage(
                sourceTemplate,
                block,
                alignment,
                operations,
                fieldPath);
            return new LinkAliasCellSymbol(fresh.View);
        }
        if (pointer.CellAddress is null)
        {
            LinkStorageTarget fresh = FreshStorage(
                sourceTemplate,
                block,
                alignment,
                operations,
                fieldPath);
            return new LinkAliasCellSymbol(fresh.View);
        }

        PointerRelocation relocation = _resolver.ResolvePointer(
            _importedDefinition,
            pointer,
            fieldPath);
        AliasCellSymbol alias = _resolver.ResolveAliasCell(relocation, fieldPath);
        if (_resolver.ResolveAliasCellValue(alias, fieldPath) is not
            AllocationReference capturedStorage)
        {
            throw new InvalidDataException(
                $"{fieldPath} captured alias cell does not publish direct storage.");
        }

        return _context.FreezeAliasCell(
            this,
            _resolver,
            alias,
            capturedStorage,
            sourceTemplate,
            block,
            alignment,
            operations,
            fieldPath);
    }

    private LinkStorageSymbol FreezeXString(
        string value,
        XPointerReference? pointer,
        int? providerRootOffset,
        string fieldPath)
    {
        byte[] bytes = LinkStorageSymbol.EncodeCString(value, fieldPath);
        if (_resolver is null)
        {
            bool retainsImportedCell = pointer is { CellAddress: not null };
            bool retainsImportedRoot = providerRootOffset is not null &&
                _importedDefinition.StagingAddress is not null;
            if (!IsAuthoredDetached &&
                (retainsImportedCell || retainsImportedRoot))
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains imported pointer provenance but has no capture resolver.");
            }

            return LinkStorageSymbol.CString(bytes);
        }
        if (pointer is { CellAddress: null } && providerRootOffset is null)
            return LinkStorageSymbol.CString(bytes);

        PointerRelocation relocation = providerRootOffset is { } rootOffset
            ? _resolver.ResolveProviderRootPointer(
                _importedDefinition,
                rootOffset,
                fieldPath)
            : _resolver.ResolvePointer(
                _importedDefinition,
                pointer!.Value,
                fieldPath);
        if (relocation.Target is not XStringReference captured)
        {
            if (relocation.Target is null)
            {
                throw new InvalidDataException(
                    $"{fieldPath} imported non-null text resolves to a null captured pointer.");
            }
            throw new InvalidDataException(
                $"{fieldPath} captured {relocation.Target.GetType().Name}, not XString storage.");
        }

        return _context.FreezeXString(
            this,
            _resolver,
            captured,
            bytes,
            fieldPath);
    }

    private static LinkStorageTarget FreshStorage(
        ReadOnlySpan<byte> sourceTemplate,
        XFileBlockType block,
        int alignment,
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        LinkStorageSymbol storage = LinkStorageSymbol.SourceBytes(
            block,
            sourceTemplate,
            alignment,
            symbol => operations?.Invoke(symbol, 0) ?? []);
        return new LinkStorageTarget(
            LinkStorageView.Whole(storage),
            CanMaterializeRoot: true);
    }
}

internal readonly record struct LinkStorageTarget(
    LinkStorageView View,
    bool CanMaterializeRoot);
