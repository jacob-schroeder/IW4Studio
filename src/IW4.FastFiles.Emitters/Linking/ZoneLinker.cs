using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Linking;

/// <summary>PS3 external-root shape. The comma-prefixed name is
/// emitted by the linker; it is never part of <see cref="ZoneAssetKey"/>.</summary>
public sealed record AssetReferenceShape(
    XAssetType AssetType,
    int RootSize,
    int RootAlignment,
    XFileBlockType RootBlock,
    int NamePointerOffset,
    XPointerResolutionMode NameResolutionMode,
    XFileBlockType NameBlock,
    TopLevelHeaderForms LegalTopLevelHeaders);

[Flags]
public enum TopLevelHeaderForms
{
    None = 0,
    Null = 1 << 0,
    Inline = 1 << 1,
    Insert = 1 << 2,
    PackedAlias = 1 << 3
}

/// <summary>Registry for externally emitted root stubs.</summary>
public sealed class AssetReferenceShapeRegistry
{
    private readonly Dictionary<XAssetType, AssetReferenceShape> _shapes = [];

    public static AssetReferenceShapeRegistry CreateDefault()
    {
        var result = new AssetReferenceShapeRegistry();
        const TopLevelHeaderForms wrapperForms =
            TopLevelHeaderForms.Null |
            TopLevelHeaderForms.Inline |
            TopLevelHeaderForms.Insert |
            TopLevelHeaderForms.PackedAlias;
        result.Register(new(
            XAssetType.RawFile,
            0x10,
            4,
            XFileBlockType.TEMP,
            0x00,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        result.Register(new(
            XAssetType.Localize,
            0x08,
            4,
            XFileBlockType.TEMP,
            0x04,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        result.Register(new(
            XAssetType.StringTable,
            0x10,
            4,
            XFileBlockType.TEMP,
            0x00,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        result.Register(new(
            XAssetType.StructuredDataDef,
            0x0c,
            4,
            XFileBlockType.TEMP,
            0x00,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        result.Register(new(
            XAssetType.Techset,
            0x9c,
            4,
            XFileBlockType.TEMP,
            0x00,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        result.Register(new(
            XAssetType.Material,
            0xa8,
            4,
            XFileBlockType.TEMP,
            0x00,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        result.Register(new(
            XAssetType.Image,
            0x50,
            4,
            XFileBlockType.TEMP,
            0x4c,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        result.Register(new(
            XAssetType.MenuFile,
            MenuFileAsset.SerializedSize,
            4,
            XFileBlockType.TEMP,
            0x00,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        result.Register(new(
            XAssetType.Menu,
            MenuDefAsset.SerializedSize,
            4,
            XFileBlockType.TEMP,
            0x00,
            XPointerResolutionMode.Direct,
            XFileBlockType.LARGE,
            wrapperForms));
        return result;
    }

    public void Register(AssetReferenceShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.RootSize < sizeof(int) || shape.NamePointerOffset < 0 || shape.NamePointerOffset > shape.RootSize - sizeof(int))
            throw new ArgumentOutOfRangeException(nameof(shape));
        if (shape.RootAlignment <= 0 || (shape.RootAlignment & (shape.RootAlignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(shape));
        if (shape.NameResolutionMode != XPointerResolutionMode.Direct)
            throw new InvalidDataException("PS3 external-stub names require a direct XString pointer.");
        if ((shape.LegalTopLevelHeaders & TopLevelHeaderForms.Inline) == 0)
            throw new InvalidDataException("A PS3 external-stub shape must permit an inline top-level header.");
        if (!_shapes.TryAdd(shape.AssetType, shape))
            throw new InvalidDataException($"A PS3 external-reference shape is already registered for '{shape.AssetType}'.");
    }

    public AssetReferenceShape Require(XAssetType type) =>
        _shapes.TryGetValue(type, out AssetReferenceShape? shape)
            ? shape
            : throw new InvalidDataException($"No PS3 external-reference shape is registered for '{type}'.");

    public bool TryGet(XAssetType type, out AssetReferenceShape? shape) =>
        _shapes.TryGetValue(type, out shape);
}

/// <summary>Failure-atomic link result. No decoded bytes are made
/// available when validation, an emitter, an alias or a fixup cannot be
/// resolved.</summary>
public sealed class ZoneLinkResult
{
    private ZoneLinkResult(
        byte[]? decodedBytes,
        XFile? xfile,
        IEnumerable<string> errors,
        IEnumerable<StreamedGfxImageEmissionContribution>?
            streamedGfxImageContributions = null)
    {
        _decodedBytes = decodedBytes;
        XFile = xfile;
        Errors = Array.AsReadOnly(errors.ToArray());
        StreamedGfxImageEmissionContribution[] contributions =
            streamedGfxImageContributions?.ToArray() ?? [];
        StreamedGfxImageContributions =
            Array.AsReadOnly(contributions);
        DbHeaderImageStreamEntry[] streamEntries = contributions
            .SelectMany(value =>
                value.SelectedLanguageStreamEntries)
            .ToArray();
        if (streamEntries.Length % GfxImageStreamData.EntryCount != 0)
        {
            throw new InvalidDataException(
                "Selected-language image-stream entry count must be a " +
                $"multiple of {GfxImageStreamData.EntryCount}.");
        }
        SelectedLanguageImageStreamEntries =
            Array.AsReadOnly(streamEntries);
    }

    private readonly byte[]? _decodedBytes;
    public bool Succeeded => _decodedBytes is not null;
    public ReadOnlyMemory<byte>? DecodedBytes => _decodedBytes is null
        ? (ReadOnlyMemory<byte>?)null
        : new ReadOnlyMemory<byte>(_decodedBytes);
    public XFile? XFile { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<DbHeaderImageStreamEntry>
        SelectedLanguageImageStreamEntries { get; }
    public IReadOnlyList<StreamedGfxImageEmissionContribution>
        StreamedGfxImageContributions { get; }
    public int StreamedGfxImageCount =>
        StreamedGfxImageContributions.Count;

    internal static ZoneLinkResult Success(
        byte[] decodedBytes,
        XFile xfile,
        IEnumerable<StreamedGfxImageEmissionContribution>?
            streamedGfxImageContributions = null) =>
        new(
            decodedBytes,
            xfile,
            [],
            streamedGfxImageContributions);

    internal static ZoneLinkResult Failure(string error) =>
        new(null, null, [error]);
}

/// <summary>
/// Source-independent PS3 decoded-zone linker.  It owns graph order, the
/// source tape, seven destination cursors and top-level symbol resolution;
/// packaging remains a deliberately separate outer stage.
/// </summary>
public sealed class ZoneLinker
{
    private readonly AssetBodyEmitterRegistry _emitters;
    private readonly AssetReferenceShapeRegistry _referenceShapes;
    private readonly ZoneScriptStringCollectorRegistry _scriptStringCollectors;
    private readonly TempAllocationMode _tempAllocationMode;

    public ZoneLinker(
        AssetBodyEmitterRegistry? emitters = null,
        AssetReferenceShapeRegistry? referenceShapes = null,
        TempAllocationMode tempAllocationMode = TempAllocationMode.NativeScoped,
        ZoneScriptStringCollectorRegistry? scriptStringCollectors = null)
    {
        _emitters = emitters ?? AssetBodyEmitterRegistry.CreateDefault();
        _referenceShapes = referenceShapes ?? AssetReferenceShapeRegistry.CreateDefault();
        _tempAllocationMode = tempAllocationMode;
        _scriptStringCollectors =
            scriptStringCollectors ?? ZoneScriptStringCollectorRegistry.CreateDefault();
    }

    public ZoneLinkResult Link(ZoneLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return LinkCore(request);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or OverflowException or ArgumentException or KeyNotFoundException)
        {
            return ZoneLinkResult.Failure($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private ZoneLinkResult LinkCore(ZoneLinkRequest request)
    {
        IReadOnlyList<ZoneAssetEntry> entries = request.GetDeterministicLinkOrder();
        IReadOnlyList<CollectedScriptStringUse> collectedScriptStrings =
            CollectScriptStringUses(entries);
        // Imported compatibility requests retain the captured table exactly.
        // Canonical requests construct their table from detached values plus
        // the caller's explicit values, so a greenfield weapon can introduce
        // a script string without inheriting any source-local slot. Opaque
        // local indices are validated but never guessed into semantic values.
        IEnumerable<string?> scriptStringInput = request.OutputPolicy.PreserveImportedScriptStringOrder
            ? request.ScriptStrings
            : request.ScriptStrings.Concat(collectedScriptStrings
                .Where(value =>
                    value.Use.Representation ==
                        ZoneScriptStringRepresentation.SemanticReference &&
                    value.Use.SemanticValue is not null)
                .Select(value => value.Use.SemanticValue));
        ScriptTable scripts = CreateScriptTable(scriptStringInput, request.OutputPolicy);
        var scriptStringResolver = new ScriptStringIndexResolver(
            scripts.Indices,
            scripts.Values,
            allowImportedRawIndex: request.OutputPolicy.PreserveImportedScriptStringOrder);
        ValidateScriptStringUses(collectedScriptStrings, scriptStringResolver);
        var plan = new EmissionPlan(
            _tempAllocationMode,
            request.OutputPolicy.PreserveImportedAssetOrder);
        var scriptSegments = new List<EmissionBlockSegment>();
        EmissionAddress? scriptTable;
        EmissionAddress? assetTable;

        plan.Push(XFileBlockType.LARGE, "XAssetList tables");
        try
        {
            scriptTable = scripts.Values.Count == 0 ? null : plan.Allocate(checked(scripts.Values.Count * sizeof(int)), 4, "script string pointer table");
            foreach (string? value in scripts.Values)
            {
                if (value is null)
                    continue;
                EmissionAddress address = plan.Allocate(checked(value.Length + 1), 1, "script string value");
                var writer = new XSourceWriter();
                writer.WriteLatin1CString(value);
                scriptSegments.Add(new EmissionBlockSegment(address, writer.ToArray()));
            }
            assetTable = entries.Count == 0 ? null : plan.Allocate(checked(entries.Count * 0x08), 4, "XAsset table");
        }
        finally
        {
            plan.Pop(XFileBlockType.LARGE);
        }

        var emittedByEntryId = new Dictionary<string, AssetBodyEmission>(
            StringComparer.Ordinal);
        var firstEmittedByKey = new Dictionary<ZoneAssetKey, AssetBodyEmission>();
        for (int index = 0; index < entries.Count; index++)
        {
            ZoneAssetEntry entry = entries[index];
            if (assetTable is { } persistentAssetTable &&
                entry.Intent is not (
                    ZoneAssetReferenceIntent.Null or
                    ZoneAssetReferenceIntent.OpaqueNativeNoOp))
            {
                // The native loader patches the current inline header cell
                // before traversing that row's children. Prior cells are also
                // safe alias targets; future -1 header cells are deliberately
                // not exposed yet.
                EmissionAddress headerCell = new(
                    persistentAssetTable.Block,
                    checked(
                        persistentAssetTable.Offset +
                        index * 0x08 +
                        sizeof(int)));
                string aliasKey = AssetBodyEmitterHelpers.XAssetAliasKey(
                    entry.Key.Type,
                    entry.Key.LogicalName);
                plan.PersistentXAssetAliasCells.TryAdd(aliasKey, headerCell);
            }

            switch (entry.Intent)
            {
                case ZoneAssetReferenceIntent.Owned:
                {
                    AssetBodyEmission body = PlanOwned(
                        entry,
                        plan,
                        scriptStringResolver);
                    emittedByEntryId.Add(entry.EntryId, body);
                    firstEmittedByKey.TryAdd(entry.Key, body);
                    break;
                }
                case ZoneAssetReferenceIntent.External:
                {
                    AssetBodyEmission body = PlanExternal(entry.Key, plan);
                    emittedByEntryId.Add(entry.EntryId, body);
                    firstEmittedByKey.TryAdd(entry.Key, body);
                    break;
                }
                case ZoneAssetReferenceIntent.Null:
                case ZoneAssetReferenceIntent.OpaqueNativeNoOp:
                    break;
                case ZoneAssetReferenceIntent.Alias:
                    if (entry.AliasTarget is not { } target ||
                        !firstEmittedByKey.TryGetValue(
                            target,
                            out AssetBodyEmission? targetBody))
                        throw new InvalidDataException($"Alias '{entry.Key}' does not follow an emitted target '{entry.AliasTarget}'.");
                    firstEmittedByKey.TryAdd(entry.Key, targetBody);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported link intent '{entry.Intent}'.");
            }
        }
        plan.EnsureBalanced();
        var stream = new ZoneLinkStream(plan);
        SourceTapeOffset headerOffset = stream.Reserve(XFile.SerializedSize);

        stream.WriteInt32(scripts.Values.Count);
        stream.WriteInt32(scriptTable is null ? 0 : -1);
        stream.WriteInt32(entries.Count);
        stream.WriteInt32(assetTable is null ? 0 : -1);

        if (scriptTable is not null)
        {
            var pointerTableWriter = new XSourceWriter();
            foreach (string? value in scripts.Values)
                pointerTableWriter.WriteInt32(value is null ? 0 : -1);
            stream.Append(pointerTableWriter.ToArray());
            foreach (EmissionBlockSegment segment in scriptSegments)
                stream.Append(segment.Bytes.Span);
        }

        int[] rowHeaders = entries
            .Select(entry => HeaderFor(entry, firstEmittedByKey))
            .ToArray();
        if (assetTable is not null)
        {
            var assetTableWriter = new XSourceWriter();
            for (int index = 0; index < entries.Count; index++)
            {
                assetTableWriter.WriteInt32((int)entries[index].Key.Type);
                assetTableWriter.WriteInt32(rowHeaders[index]);
            }
            stream.Append(assetTableWriter.ToArray());
        }

        stream.AppendLegacyBodies(entries
            .Where(entry => emittedByEntryId.ContainsKey(entry.EntryId))
            .Select(entry => new KeyValuePair<ZoneAssetKey, AssetBodyEmission>(
                entry.Key,
                emittedByEntryId[entry.EntryId])));

        int meaningfulLength = stream.SourcePosition.Value;
        int decodedLength = AlignUp(meaningfulLength, request.LayoutPolicy.DecodedAlignment);
        if (decodedLength != meaningfulLength)
        {
            int paddingLength = decodedLength - meaningfulLength;
            stream.Reserve(paddingLength);
        }
        byte[] decoded = stream.Complete();
        int[] highWater = stream.HighWater.ToArray();
        int[] blockSizes = highWater
            .Select((value, index) => Math.Max(
                value,
                checked((int)request.LayoutPolicy.BlockSizeFloors[index])))
            .ToArray();
        uint xfileSize = checked((uint)(meaningfulLength - XFile.SerializedSize));
        var header = new XSourceWriter();
        header.WriteUInt32(xfileSize);
        header.WriteUInt32(request.LayoutPolicy.ExternalSize);
        foreach (int value in blockSizes)
            header.WriteUInt32(checked((uint)value));
        if (header.Position != XFile.SerializedSize || headerOffset.Value != 0)
            throw new InvalidDataException("PS3 decoded XFile header is not 0x24 bytes.");
        Buffer.BlockCopy(header.ToArray(), 0, decoded, 0, XFile.SerializedSize);

        var xfile = new XFile(
            xfileSize,
            request.LayoutPolicy.ExternalSize,
            blockSizes.Select(value => (uint)value));
        (ZoneAssetKey Key, EmissionAddress? RootAddress)[] symbols = entries
            .Select(entry => (
                entry.Key,
                emittedByEntryId.TryGetValue(
                    entry.EntryId,
                    out AssetBodyEmission? body)
                    ? (EmissionAddress?)body.RootAddress
                    : entry.Intent == ZoneAssetReferenceIntent.Alias &&
                      entry.AliasTarget is { } alias
                        ? firstEmittedByKey[alias].RootAddress
                        : null))
            .ToArray();
        ValidateSymbolAddresses(symbols, highWater);
        IReadOnlyList<StreamedGfxImageEmissionContribution>
            imageStreamContributions =
                plan.StreamedGfxImageContributions;
        IReadOnlyList<DbHeaderImageStreamEntry> imageStreamEntries =
            imageStreamContributions
                .SelectMany(value =>
                    value.SelectedLanguageStreamEntries)
                .ToArray();
        if (imageStreamEntries.Count %
                GfxImageStreamData.EntryCount != 0)
        {
            throw new InvalidDataException(
                "Planned selected-language image-stream entry count must be " +
                $"a multiple of {GfxImageStreamData.EntryCount}.");
        }
        return ZoneLinkResult.Success(
            decoded,
            xfile,
            imageStreamContributions);
    }

    private static int AlignUp(int value, int alignment) =>
        checked((value + alignment - 1) & ~(alignment - 1));

    private AssetBodyEmission PlanOwned(
        ZoneAssetEntry entry,
        EmissionPlan plan,
        ScriptStringIndexResolver scriptStringResolver)
    {
        if (entry.BuildData is null)
            throw new InvalidDataException($"Owned entry '{entry.Key}' has no build data.");
        IXAssetBodyEmitter emitter = _emitters.Require(entry.Key.Type);
        using IDisposable scope = ScriptStringEmissionScope.Push(scriptStringResolver);
        return emitter.Plan(entry.BuildData, plan);
    }

    private AssetBodyEmission PlanExternal(ZoneAssetKey key, EmissionPlan plan)
    {
        AssetReferenceShape shape = _referenceShapes.Require(key.Type);
        string wireName = $",{key.LogicalName}";
        if (wireName.Any(character => character > byte.MaxValue))
            throw new InvalidDataException($"External reference '{key}' cannot be encoded as Latin-1.");

        plan.Push(shape.RootBlock, $"external root {key}");
        EmissionAddress root;
        try { root = plan.Allocate(shape.RootSize, shape.RootAlignment, $"external root {key}"); }
        finally { plan.Pop(shape.RootBlock); }
        var segments = new List<EmissionBlockSegment>();
        plan.Push(shape.NameBlock, $"external name {key}");
        PlannedString? name;
        try
        {
            name = AssetBodyEmitterHelpers.PlanString(
                wireName,
                plan,
                segments,
                plan.StringAliases);
        }
        finally { plan.Pop(shape.NameBlock); }

        var rootWriter = new XSourceWriter();
        rootWriter.Reserve(shape.RootSize);
        rootWriter.PatchInt32(
            shape.NamePointerOffset,
            AssetBodyEmitterHelpers.SourcePointer(name));
        var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray());
        return new AssetBodyEmission(
            key.Type,
            root,
            [rootSegment, .. segments],
            [rootSegment, .. segments]);
    }

    private static int HeaderFor(
        ZoneAssetEntry entry,
        IReadOnlyDictionary<ZoneAssetKey, AssetBodyEmission> firstEmittedByKey) =>
        entry.Intent switch
    {
        ZoneAssetReferenceIntent.Owned or ZoneAssetReferenceIntent.External => -1,
        ZoneAssetReferenceIntent.Null => 0,
        ZoneAssetReferenceIntent.OpaqueNativeNoOp => entry.OpaqueHeader,
        ZoneAssetReferenceIntent.Alias when
            entry.AliasTarget is { } target &&
            firstEmittedByKey.TryGetValue(target, out AssetBodyEmission? body) =>
            body.RootAddress.ToPackedPointer(),
        ZoneAssetReferenceIntent.Alias => throw new InvalidDataException($"Alias '{entry.Key}' has no emitted target."),
        _ => throw new InvalidDataException($"Unsupported header intent '{entry.Intent}'.")
    };

    private static void ValidateSymbolAddresses(
        IEnumerable<(ZoneAssetKey Key, EmissionAddress? RootAddress)> symbols,
        IReadOnlyList<int> blockHighWater)
    {
        foreach ((ZoneAssetKey key, EmissionAddress? rootAddress) in symbols)
        {
            if (rootAddress is not { } address)
                continue;
            int highWater = blockHighWater[(int)address.Block];
            if (address.Offset < 0 || address.Offset >= highWater)
            {
                throw new InvalidDataException(
                    $"Symbol '{key}' targets {address}, outside final " +
                    $"{address.Block} high-water 0x{highWater:X}.");
            }
        }
    }

    private IReadOnlyList<CollectedScriptStringUse> CollectScriptStringUses(
        IEnumerable<ZoneAssetEntry> entries)
    {
        var result = new List<CollectedScriptStringUse>();
        foreach (ZoneAssetEntry entry in entries)
        {
            if (entry.Intent != ZoneAssetReferenceIntent.Owned)
                continue;
            if (entry.BuildData is null)
                throw new InvalidDataException($"Owned entry '{entry.Key}' has no build data.");
            result.AddRange(_scriptStringCollectors
                .Collect(entry.BuildData)
                .Select(use => new CollectedScriptStringUse(entry.Key, use)));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static void ValidateScriptStringUses(
        IEnumerable<CollectedScriptStringUse> uses,
        ScriptStringIndexResolver resolver)
    {
        foreach (CollectedScriptStringUse collected in uses)
        {
            ZoneScriptStringUse use = collected.Use;
            string fieldPath = $"{collected.Owner}.{use.FieldPath}";
            switch (use.Representation)
            {
                case ZoneScriptStringRepresentation.SemanticReference:
                    resolver.Resolve(
                        use.RawLocalIndex,
                        use.SemanticValue,
                        fieldPath);
                    break;
                case ZoneScriptStringRepresentation.OpaqueImportedIndex:
                    resolver.ResolveOpaqueImportedRaw(
                        use.RawLocalIndex,
                        fieldPath);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Script-string field '{fieldPath}' has an unknown representation.");
            }
        }
    }

    private static ScriptTable CreateScriptTable(IEnumerable<string?> input, ZoneLinkOutputPolicy policy)
    {
        string?[] sourceValues = input.ToArray();
        var values = new List<string?>();
        var indices = new Dictionary<string, ushort>(StringComparer.Ordinal);

        if (policy.PreserveImportedScriptStringOrder)
        {
            foreach (string? value in sourceValues)
            {
                if (values.Count > ushort.MaxValue)
                    throw new InvalidDataException("The PS3 script-string table exceeds the UInt16 local-index range.");
                // Imported tables are serialized data, not a handle allocator.
                // Slot zero may contain an unreachable string because
                // scr_string_t zero still resolves as null. Preserve its bytes
                // and position, and let a later duplicate become reachable.
                if (values.Count != 0 &&
                    value is not null &&
                    !indices.ContainsKey(value))
                {
                    indices.Add(value, checked((ushort)values.Count));
                }
                values.Add(value);
            }
            return new ScriptTable(Array.AsReadOnly(values.ToArray()), indices);
        }

        string[] canonicalValues = sourceValues
            .Where(value => value is not null)
            .Cast<string>()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (canonicalValues.Length == 0)
            return new ScriptTable(Array.AsReadOnly(Array.Empty<string?>()), indices);

        // Serialized scr_string_t value zero is the null handle, so non-empty
        // tables reserve local index zero as a null entry. Non-null values
        // occupy 1..UInt16.MaxValue.
        values.Add(null);
        foreach (string value in canonicalValues)
        {
            if (policy.DeduplicateScriptStrings && indices.ContainsKey(value))
                continue;
            if (values.Count > ushort.MaxValue)
                throw new InvalidDataException("The PS3 script-string table exceeds the UInt16 reference range.");
            if (!indices.ContainsKey(value))
                indices.Add(value, checked((ushort)values.Count));
            values.Add(value);
        }
        return new ScriptTable(Array.AsReadOnly(values.ToArray()), indices);
    }

    private sealed record ScriptTable(IReadOnlyList<string?> Values, IReadOnlyDictionary<string, ushort> Indices);
    private sealed record CollectedScriptStringUse(
        ZoneAssetKey Owner,
        ZoneScriptStringUse Use);
}
