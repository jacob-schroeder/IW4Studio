using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Linker.Plans;

internal sealed class LinkStorageEmissionState
{
    private readonly Dictionary<LinkStorageSymbol, LinkStoragePublication>
        _publications = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LinkAliasCellSymbol, XBlockAddress>
        _aliasCellPublications = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<List<LinkStorageSymbol>> _tempScopes = [];
    private readonly IReadOnlyDictionary<string, ushort> _scriptStringIndices;

    public LinkStorageEmissionState(
        ZoneEmissionWriter output,
        IReadOnlyDictionary<string, ushort> scriptStringIndices)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        _scriptStringIndices = scriptStringIndices ??
            throw new ArgumentNullException(nameof(scriptStringIndices));
    }

    public ZoneEmissionWriter Output { get; }

    public void EmitDetached(LinkStorageSymbol storage, string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        if (_publications.ContainsKey(storage))
            throw new InvalidDataException($"{fieldPath} was materialized more than once.");
        Materialize(storage);
    }

    public void PushTempScope()
    {
        Output.PushTempScope();
        _tempScopes.Push([]);
    }

    public void PopTempScope()
    {
        if (_tempScopes.Count == 0)
            throw new InvalidOperationException("Link storage TEMP scope stack is empty.");

        List<LinkStorageSymbol> scoped = _tempScopes.Pop();
        foreach (LinkStorageSymbol storage in scoped)
            _publications.Remove(storage);
        Output.PopTempScope();
    }

    public void EmitProviderRoot(
        LinkStorageSymbol root,
        Action<AssetDependency, XBlockAddress, int> emitDependency)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(emitDependency);
        if (root.Definition.Block != XFileBlockType.TEMP)
        {
            throw new InvalidDataException(
                "Provider plan roots must materialize in the TEMP block.");
        }

        PushTempScope();
        try
        {
            if (_publications.ContainsKey(root))
                throw new InvalidDataException("A provider body was materialized more than once.");
            Materialize(root, emitDependency);
        }
        finally
        {
            PopTempScope();
        }
    }

    private LinkStoragePublication Materialize(
        LinkStorageSymbol storage,
        Action<AssetDependency, XBlockAddress, int>? emitDependency = null)
    {
        LinkStorageDefinition definition = storage.Definition;
        XBlockAddress address = Output.Allocate(
            definition.Block,
            definition.ByteLength,
            definition.Alignment);
        int? sourceOffset = null;
        switch (definition.Kind)
        {
            case LinkMaterializationKind.SourceBytes:
                sourceOffset = Output.SourceLength;
                Output.WriteBytes(definition.SourceTemplate.Span);
                break;
            case LinkMaterializationKind.RuntimeZeroFill:
            case LinkMaterializationKind.VirtualReservation:
            case LinkMaterializationKind.VertexReservation:
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported storage materialization kind {definition.Kind}.");
        }

        var publication = new LinkStoragePublication(address, sourceOffset);
        if (!_publications.TryAdd(storage, publication))
            throw new InvalidDataException("One storage identity selected competing materializations.");
        if (definition.Block == XFileBlockType.TEMP)
        {
            if (_tempScopes.Count == 0)
                throw new InvalidOperationException("TEMP storage has no active lifetime scope.");
            _tempScopes.Peek().Add(storage);
        }

        foreach (LinkOperation operation in definition.Operations)
            Execute(operation, emitDependency);
        return publication;
    }

    private void Execute(
        LinkOperation operation,
        Action<AssetDependency, XBlockAddress, int>? emitDependency)
    {
        switch (operation)
        {
            case DirectStorageLinkOperation direct:
                EmitDirect(direct.Cell, direct.Target, direct.CanMaterializeRoot, direct.FieldPath, emitDependency);
                break;
            case PresenceStorageLinkOperation presence:
                EmitPresence(presence, emitDependency);
                break;
            case XStringLinkOperation text:
                EmitDirect(text.Cell, text.Target, text.CanMaterializeRoot, text.FieldPath, emitDependency);
                break;
            case ProviderLinkOperation provider:
            {
                if (emitDependency is null)
                    throw new InvalidDataException($"{provider.Dependency.FieldPath} has no provider resolver.");
                ResolvedCell cell = ResolveCell(provider.Cell, sizeof(int));
                emitDependency(provider.Dependency, cell.Address, cell.SourceOffset);
                break;
            }
            case DependencyOnlyLinkOperation:
                break;
            case AliasCellStorageLinkOperation alias:
                EmitAliasCell(alias, emitDependency);
                break;
            case ScriptStringLinkOperation script:
            {
                ResolvedCell cell = ResolveCell(script.Cell, sizeof(ushort));
                ushort index = script.Text is null
                    ? (ushort)0
                    : _scriptStringIndices.TryGetValue(script.Text, out ushort resolved)
                        ? resolved
                        : throw new InvalidDataException(
                            $"{script.FieldPath} value '{script.Text}' is absent from the rebuilt script-string table.");
                Output.PatchUInt16(cell.SourceOffset, index);
                break;
            }
            case MaterializeStorageLinkOperation materialize:
                if (_publications.ContainsKey(materialize.Storage))
                {
                    throw new InvalidDataException(
                        $"{materialize.FieldPath} selected a second physical materialization.");
                }
                Materialize(materialize.Storage, emitDependency);
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported link operation {operation.GetType().Name}.");
        }
    }

    private void EmitDirect(
        LinkStorageCell source,
        LinkStorageView target,
        bool canMaterializeRoot,
        string fieldPath,
        Action<AssetDependency, XBlockAddress, int>? emitDependency)
    {
        ResolvedCell cell = ResolveCell(source, sizeof(int));
        if (_publications.TryGetValue(target.Storage, out LinkStoragePublication publication))
        {
            ValidateCompositeRange(target, fieldPath);
            Output.PatchInt32(
                cell.SourceOffset,
                XPointerCodec.Encode(Add(publication.Address, target.Addend)));
            return;
        }

        RequireFullRootMaterializer(target, canMaterializeRoot, fieldPath);
        Output.PatchInt32(cell.SourceOffset, -1);
        Materialize(target.Storage, emitDependency);
    }

    private void ValidateCompositeRange(
        LinkStorageView target,
        string fieldPath)
    {
        if (target.CompositeRange is not { } range)
            return;

        XBlockAddress? expected = null;
        int covered = 0;
        foreach ((LinkStorageView segment, int index) in
            range.Segments.Select((value, index) => (value, index)))
        {
            if (!_publications.TryGetValue(
                    segment.Storage,
                    out LinkStoragePublication publication))
            {
                throw new InvalidDataException(
                    $"{fieldPath} composite direct range segment {index} has not been materialized.");
            }

            XBlockAddress actual = Add(publication.Address, segment.Addend);
            if (expected is { } expectedAddress && actual != expectedAddress)
            {
                throw new InvalidDataException(
                    $"{fieldPath} composite direct range segment {index} is not adjacent to its predecessor.");
            }

            expected = Add(actual, segment.Length);
            covered = checked(covered + segment.Length);
        }
        if (covered != range.ByteLength)
            throw new InvalidDataException($"{fieldPath} composite direct range has inconsistent coverage.");
    }

    private void EmitPresence(
        PresenceStorageLinkOperation presence,
        Action<AssetDependency, XBlockAddress, int>? emitDependency)
    {
        ResolvedCell cell = ResolveCell(presence.Cell, sizeof(int));
        if (_publications.ContainsKey(presence.Target.Storage))
        {
            throw new InvalidDataException(
                $"{presence.FieldPath} is presence/unique storage and cannot reuse an earlier body.");
        }
        RequireFullRootMaterializer(
            presence.Target,
            canMaterializeRoot: true,
            presence.FieldPath);
        Output.PatchInt32(cell.SourceOffset, -1);
        Materialize(presence.Target.Storage, emitDependency);
    }

    private void EmitAliasCell(
        AliasCellStorageLinkOperation alias,
        Action<AssetDependency, XBlockAddress, int>? emitDependency)
    {
        ResolvedCell cell = ResolveCell(alias.Cell, sizeof(int));
        if (_aliasCellPublications.TryGetValue(
                alias.AliasCell,
                out XBlockAddress publication))
        {
            Output.PatchInt32(cell.SourceOffset, XPointerCodec.Encode(publication));
            return;
        }

        LinkStorageView target = alias.AliasCell.Target;
        RequireFullRootMaterializer(
            target,
            canMaterializeRoot: true,
            alias.FieldPath);
        if (_publications.ContainsKey(target.Storage))
        {
            throw new InvalidDataException(
                $"{alias.FieldPath} cannot publish an already-materialized direct body as a new alias cell.");
        }

        int marker;
        if (cell.Address.BlockType == XFileBlockType.TEMP)
        {
            publication = Output.Allocate(
                XFileBlockType.LARGE,
                sizeof(int),
                alignment: 4);
            marker = -2;
        }
        else
        {
            publication = cell.Address;
            marker = -1;
        }

        _aliasCellPublications.Add(alias.AliasCell, publication);
        Output.PatchInt32(cell.SourceOffset, marker);
        Materialize(target.Storage, emitDependency);
        if (alias.FirstPublicationMaterialization is { } materialization)
            Materialize(materialization, emitDependency);
    }

    private ResolvedCell ResolveCell(LinkStorageCell cell, int width)
    {
        if (!_publications.TryGetValue(cell.Owner, out LinkStoragePublication owner))
            throw new InvalidDataException("A relocation source storage has not been materialized.");
        if (owner.SourceOffset is not { } sourceOffset)
            throw new InvalidDataException("A source-free allocation cannot own a serialized relocation cell.");
        if (cell.Offset < 0 || cell.Offset > cell.Owner.Definition.ByteLength - width)
            throw new InvalidDataException("A relocation cell lies outside its source storage.");

        return new ResolvedCell(
            Add(owner.Address, cell.Offset),
            checked(sourceOffset + cell.Offset));
    }

    private static void RequireFullRootMaterializer(
        LinkStorageView target,
        bool canMaterializeRoot,
        string fieldPath)
    {
        if (!canMaterializeRoot ||
            target.Addend != 0 ||
            target.Length != target.Storage.Definition.ByteLength)
        {
            throw new InvalidDataException(
                $"{fieldPath} reaches unpublished interior storage and has no loader-safe full-root materializer.");
        }
    }

    private static XBlockAddress Add(XBlockAddress address, int addend) =>
        new(address.BlockType, checked(address.Offset + addend));

    private readonly record struct LinkStoragePublication(
        XBlockAddress Address,
        int? SourceOffset);

    private readonly record struct ResolvedCell(
        XBlockAddress Address,
        int SourceOffset);
}

internal sealed class LinkEmissionContext
{
    private readonly LinkStorageEmissionState _state;
    private readonly Action<AssetDependency, XBlockAddress, int> _emitDependency;

    public LinkEmissionContext(
        LinkStorageEmissionState state,
        Action<AssetDependency, XBlockAddress, int> emitDependency)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _emitDependency = emitDependency ?? throw new ArgumentNullException(nameof(emitDependency));
    }

    public void EmitProviderRoot(LinkStorageSymbol root) =>
        _state.EmitProviderRoot(root, _emitDependency);
}
