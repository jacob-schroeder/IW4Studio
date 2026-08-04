using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.FastFiles.Emitters.Linking;

/// <summary>
/// Stable, source-independent identity for an XAsset.  A comma is wire
/// syntax for an external reference and is therefore deliberately forbidden
/// here.
/// </summary>
public readonly record struct ZoneAssetKey
{
    public ZoneAssetKey(XAssetType type, string logicalName)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        Type = type;
        LogicalName = NormalizeLogicalName(logicalName, nameof(logicalName));
    }

    public XAssetType Type { get; }
    public string LogicalName { get; }

    public override string ToString() => $"{Type}:{LogicalName}";

    /// <summary>
    /// IW4's DB lookup identity is case-insensitive. Canonical linker keys use
    /// lower-case invariant spelling and forward slashes so host filesystem
    /// case and separator conventions cannot affect graph identity. The native
    /// external-reference comma is removed only at this wire-import boundary.
    /// </summary>
    public static ZoneAssetKey FromWireName(XAssetType type, string wireName)
    {
        ArgumentNullException.ThrowIfNull(wireName);
        string logicalName = wireName.StartsWith(",", StringComparison.Ordinal)
            ? wireName[1..]
            : wireName;
        return new ZoneAssetKey(type, logicalName);
    }

    internal static string NormalizeLogicalName(string logicalName, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName, parameterName);
        if (!string.Equals(logicalName, logicalName.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Logical asset names cannot contain leading or trailing whitespace.", parameterName);
        if (logicalName[0] == ',')
            throw new ArgumentException("A comma prefix is serialized wire syntax, not logical asset identity.", parameterName);
        if (logicalName.IndexOf('\0') >= 0)
            throw new ArgumentException("Logical asset names cannot contain an embedded null character.", parameterName);

        string canonical = logicalName.Replace('\\', '/');
        if (canonical.Split('/').Any(segment => segment.Length == 0))
            throw new ArgumentException("Logical asset names cannot contain empty path segments.", parameterName);
        return canonical.ToLowerInvariant();
    }
}

/// <summary>The only top-level intents the canonical linker accepts.</summary>
public enum ZoneAssetReferenceIntent
{
    Owned,
    External,
    Null,
    Alias,
    OpaqueNativeNoOp
}

/// <summary>
/// Semantic dependency kind, independent of the field's eventual pointer
/// representation.
/// </summary>
public enum ZoneAssetDependencyKind
{
    /// <summary>The target must be another entry in the frozen graph.</summary>
    Required,
    /// <summary>The target orders the graph when present, but may be absent.</summary>
    Optional,
    /// <summary>
    /// A non-null, comma-prefixed nested XAsset reference. The field emitter
    /// materializes its external-reference shape, so a matching top-level row
    /// is not required. When one is present it still participates in
    /// dependency-first ordering.
    /// </summary>
    External
}

/// <summary>Describes a semantic relationship independently of a pointer cell.</summary>
public sealed record ZoneAssetDependency
{
    /// <summary>
    /// Compatibility constructor for callers that model graph-required versus
    /// optional links directly.
    /// </summary>
    public ZoneAssetDependency(
        ZoneAssetKey Target,
        bool IsRequired = true,
        string? OwnerPath = null)
        : this(
            Target,
            IsRequired ? ZoneAssetDependencyKind.Required : ZoneAssetDependencyKind.Optional,
            OwnerPath)
    {
    }

    public ZoneAssetDependency(
        ZoneAssetKey Target,
        ZoneAssetDependencyKind Kind,
        string? OwnerPath = null)
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
        this.Target = Target;
        this.Kind = Kind;
        this.OwnerPath = OwnerPath;
    }

    public ZoneAssetKey Target { get; }
    public ZoneAssetDependencyKind Kind { get; }
    public string? OwnerPath { get; }
    public bool IsRequired => Kind == ZoneAssetDependencyKind.Required;
    public bool IsExternal => Kind == ZoneAssetDependencyKind.External;
}

/// <summary>Policy chosen when removing an entry with inbound edges.</summary>
public enum ZoneRemoveDanglingPolicy
{
    Reject,
    ReplaceTargetWithExternal
}

/// <summary>Controls deterministic graph and script-table ordering without
/// retaining an imported zone's source addresses.</summary>
public sealed record ZoneLinkOutputPolicy(
    bool PreferImportedOrder = false,
    bool PreserveImportedScriptStringOrder = false,
    bool RequireDeterministicPackageMetadata = true,
    bool DeduplicateScriptStrings = true,
    bool PreserveImportedAssetOrder = false)
{
    public static ZoneLinkOutputPolicy Canonical { get; } = new();
    /// <summary>Compatibility only. Imported asset-table and script-string
    /// positions remain stable while detached legacy build data uses the
    /// bridge. Dependencies are still validated, but they cannot rewrite the
    /// source asset-table order.</summary>
    public static ZoneLinkOutputPolicy LegacyImported { get; } = new(
        PreferImportedOrder: true,
        PreserveImportedScriptStringOrder: true,
        RequireDeterministicPackageMetadata: true,
        DeduplicateScriptStrings: false,
        PreserveImportedAssetOrder: true);
}

/// <summary>
/// Decoded-zone layout policy. Canonical requests derive all seven destination
/// block sizes from allocation high-water. Imported compatibility requests may
/// additionally preserve source block reservations as lower bounds; those
/// floors never move an allocation or introduce source bytes.
/// </summary>
public sealed class ZoneLinkLayoutPolicy
{
    private readonly IReadOnlyList<uint> _blockSizeFloors;

    public ZoneLinkLayoutPolicy(
        uint externalSize = 0,
        int decodedAlignment = 0x10000,
        IEnumerable<uint>? blockSizeFloors = null)
    {
        if (decodedAlignment <= 0 || (decodedAlignment & (decodedAlignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(decodedAlignment));

        uint[] copiedFloors = (blockSizeFloors ??
            Enumerable.Repeat(0u, (int)XFileBlockType.COUNT)).ToArray();
        if (copiedFloors.Length != (int)XFileBlockType.COUNT)
        {
            throw new ArgumentException(
                $"A zone layout requires exactly {(int)XFileBlockType.COUNT} block-size floors.",
                nameof(blockSizeFloors));
        }

        ExternalSize = externalSize;
        DecodedAlignment = decodedAlignment;
        _blockSizeFloors = Array.AsReadOnly(copiedFloors);
    }

    public static ZoneLinkLayoutPolicy Canonical { get; } = new();
    public uint ExternalSize { get; }
    public int DecodedAlignment { get; }
    public IReadOnlyList<uint> BlockSizeFloors => _blockSizeFloors;
}

/// <summary>One immutable logical entry.  It never holds a runtime asset,
/// pool address, source bytes, document ID, or loader context.</summary>
public sealed class ZoneAssetEntry
{
    private readonly IReadOnlyList<ZoneAssetDependency> _declaredDependencies;
    private readonly IReadOnlyList<ZoneAssetDependency> _dependencies;

    public ZoneAssetEntry(
        string entryId,
        ZoneAssetKey key,
        ZoneAssetReferenceIntent intent,
        IXAssetBuildData? buildData = null,
        ZoneAssetKey? aliasTarget = null,
        int? importedOrder = null,
        IEnumerable<ZoneAssetDependency>? dependencies = null,
        int opaqueHeader = 0,
        string? originalSpelling = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (importedOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(importedOrder));
        EnsureValidKey(key, nameof(key));
        if (aliasTarget is { } target)
            EnsureValidKey(target, nameof(aliasTarget));

        EntryId = entryId;
        Key = key;
        Intent = intent;
        BuildData = buildData;
        AliasTarget = aliasTarget;
        ImportedOrder = importedOrder;
        OpaqueHeader = opaqueHeader;
        OriginalSpelling = ValidateOriginalSpelling(key, originalSpelling);
        ZoneAssetDependency[] declaredDependencies = (dependencies ?? [])
            .Select(value => value ?? throw new ArgumentException("Dependencies cannot contain null.", nameof(dependencies)))
            .Select(value =>
            {
                EnsureValidKey(value.Target, nameof(dependencies));
                return value;
            })
            .Distinct()
            .OrderBy(value => value.Target.Type)
            .ThenBy(value => value.Target.LogicalName, StringComparer.Ordinal)
            .ThenBy(value => value.Kind)
            .ThenBy(value => value.OwnerPath, StringComparer.Ordinal)
            .ToArray();
        _declaredDependencies = Array.AsReadOnly(declaredDependencies);

        IReadOnlyList<ZoneAssetDependency> discoveredDependencies =
            Intent == ZoneAssetReferenceIntent.Owned && BuildData is not null
                ? ZoneAssetDependencyCollectorRegistry.Default.CollectKnown(BuildData)
                : [];
        _dependencies = Array.AsReadOnly(MergeDependencies(
            declaredDependencies,
            discoveredDependencies));

        ValidateShape();
    }

    public string EntryId { get; }
    public ZoneAssetKey Key { get; }
    public ZoneAssetReferenceIntent Intent { get; }
    public IXAssetBuildData? BuildData { get; }
    public ZoneAssetKey? AliasTarget { get; }
    public int? ImportedOrder { get; }
    public int OpaqueHeader { get; }
    /// <summary>
    /// Optional non-semantic imported/display spelling. Graph identity always
    /// uses <see cref="ZoneAssetKey"/>; this value may retain case, slash
    /// direction, and one native external-reference comma.
    /// </summary>
    public string? OriginalSpelling { get; }
    public IReadOnlyList<ZoneAssetDependency> Dependencies => _dependencies;
    internal IReadOnlyList<ZoneAssetDependency> DeclaredDependencies => _declaredDependencies;

    internal ZoneAssetEntry With(
        ZoneAssetReferenceIntent? intent = null,
        IXAssetBuildData? buildData = null,
        bool replaceBuildData = false,
        ZoneAssetKey? aliasTarget = null,
        bool replaceAliasTarget = false) =>
        new(
            EntryId,
            Key,
            intent ?? Intent,
            replaceBuildData ? buildData : BuildData,
            replaceAliasTarget ? aliasTarget : AliasTarget,
            ImportedOrder,
            DeclaredDependencies,
            OpaqueHeader,
            OriginalSpelling);

    private static ZoneAssetDependency[] MergeDependencies(
        IEnumerable<ZoneAssetDependency> declared,
        IEnumerable<ZoneAssetDependency> discovered)
    {
        ZoneAssetDependency[] merged = declared
            .Concat(discovered)
            .Distinct()
            .OrderBy(value => value.Target.Type)
            .ThenBy(value => value.Target.LogicalName, StringComparer.Ordinal)
            .ThenBy(value => value.Kind)
            .ThenBy(value => value.OwnerPath, StringComparer.Ordinal)
            .ToArray();

        string[] contradictions = merged
            .Where(value => !string.IsNullOrWhiteSpace(value.OwnerPath))
            .GroupBy(value => value.OwnerPath!, StringComparer.Ordinal)
            .Where(group => group
                .Select(value => (value.Target, value.Kind))
                .Distinct()
                .Skip(1)
                .Any())
            .Select(group =>
                $"{group.Key}: {string.Join(", ", group.Select(value => $"{value.Kind} {value.Target}").OrderBy(value => value, StringComparer.Ordinal))}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (contradictions.Length != 0)
        {
            throw new InvalidDataException(
                "Zone asset dependency declarations contradict discovered semantic fields:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, contradictions.Select(value => $" - {value}")));
        }

        return merged;
    }

    private void ValidateShape()
    {
        switch (Intent)
        {
            case ZoneAssetReferenceIntent.Owned when BuildData is null:
                throw new ArgumentException($"Owned entry '{Key}' requires build data.", nameof(BuildData));
            case ZoneAssetReferenceIntent.Owned when BuildData.AssetType != Key.Type:
                throw new ArgumentException($"Owned entry '{Key}' has build data for '{BuildData.AssetType}'.", nameof(BuildData));
            case ZoneAssetReferenceIntent.Owned when AliasTarget is not null:
                throw new ArgumentException("Owned entries cannot carry an alias target.", nameof(AliasTarget));
            case ZoneAssetReferenceIntent.External or ZoneAssetReferenceIntent.Null or ZoneAssetReferenceIntent.OpaqueNativeNoOp
                when BuildData is not null || AliasTarget is not null:
                throw new ArgumentException($"{Intent} entry '{Key}' cannot carry build data or an alias target.");
            case ZoneAssetReferenceIntent.Alias when AliasTarget is null:
                throw new ArgumentException($"Alias entry '{Key}' requires an alias target.", nameof(AliasTarget));
            case ZoneAssetReferenceIntent.Alias when BuildData is not null:
                throw new ArgumentException("Alias entries cannot carry owned build data.", nameof(BuildData));
            case ZoneAssetReferenceIntent.Alias when AliasTarget?.Type != Key.Type:
                throw new ArgumentException(
                    $"Alias entry '{Key}' cannot target a different asset type '{AliasTarget}'.",
                    nameof(AliasTarget));
        }
    }

    private static string? ValidateOriginalSpelling(ZoneAssetKey key, string? originalSpelling)
    {
        if (originalSpelling is null)
            return null;
        ZoneAssetKey imported = ZoneAssetKey.FromWireName(key.Type, originalSpelling);
        if (imported != key)
        {
            throw new ArgumentException(
                $"Original spelling '{originalSpelling}' does not normalize to logical key '{key}'.",
                nameof(originalSpelling));
        }
        return originalSpelling;
    }

    private static void EnsureValidKey(ZoneAssetKey key, string parameterName)
    {
        if (!Enum.IsDefined(key.Type) || string.IsNullOrEmpty(key.LogicalName))
            throw new ArgumentException("Zone asset keys must be constructed logical identities.", parameterName);
        string normalized = ZoneAssetKey.NormalizeLogicalName(key.LogicalName, parameterName);
        if (!string.Equals(normalized, key.LogicalName, StringComparison.Ordinal))
            throw new ArgumentException("Zone asset keys must use canonical logical spelling.", parameterName);
    }
}

/// <summary>
/// Immutable linker input.  A request is intentionally smaller than a Studio
/// snapshot: it carries only logical entries, relationships, script-string
/// values and output policy.
/// </summary>
public sealed class ZoneLinkRequest
{
    private readonly IReadOnlyList<ZoneAssetEntry> _entries;
    private readonly IReadOnlyList<string?> _scriptStrings;
    private readonly IReadOnlyList<ZoneAssetEntry> _deterministicLinkOrder;

    public ZoneLinkRequest(
        IEnumerable<ZoneAssetEntry> entries,
        IEnumerable<string?>? scriptStrings = null,
        ZoneLinkOutputPolicy? outputPolicy = null,
        ZoneLinkLayoutPolicy? layoutPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ZoneAssetEntry[] copied = entries.ToArray();
        if (copied.Select(entry => entry.EntryId).Distinct(StringComparer.Ordinal).Count() != copied.Length)
            throw new InvalidDataException("Zone link entries require unique entry IDs.");

        _entries = Array.AsReadOnly(copied);
        _scriptStrings = Array.AsReadOnly((scriptStrings ?? []).ToArray());
        OutputPolicy = outputPolicy ?? ZoneLinkOutputPolicy.Canonical;
        LayoutPolicy = layoutPolicy ?? ZoneLinkLayoutPolicy.Canonical;
        ValidateScriptStrings();
        _deterministicLinkOrder = BuildDeterministicLinkOrder();
    }

    public IReadOnlyList<ZoneAssetEntry> Entries => _entries;
    public IReadOnlyList<string?> ScriptStrings => _scriptStrings;
    public ZoneLinkOutputPolicy OutputPolicy { get; }
    public ZoneLinkLayoutPolicy LayoutPolicy { get; }

    /// <summary>Validates dependency targets/cycles and returns the stable
    /// top-level order used by the linker. Canonical requests are
    /// dependency-first; imported compatibility requests preserve the source
    /// asset table after validation.</summary>
    public IReadOnlyList<ZoneAssetEntry> GetDeterministicLinkOrder() =>
        _deterministicLinkOrder;

    private IReadOnlyList<ZoneAssetEntry> BuildDeterministicLinkOrder()
    {
        var stableComparer = Comparer<ZoneAssetEntry>.Create(StableOrder);
        Dictionary<ZoneAssetKey, ZoneAssetEntry> byKey = _entries
            .GroupBy(entry => entry.Key)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(entry => MaterializedProviderPreference(entry.Intent))
                    .ThenBy(entry => entry, stableComparer)
                    .First());
        ValidateAllRequiredTargets(_entries, byKey);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var active = new List<ZoneAssetEntry>();
        var result = new List<ZoneAssetEntry>(_entries.Count);

        foreach (ZoneAssetEntry entry in _entries.OrderBy(
            entry => entry,
            stableComparer))
            Visit(entry);
        if (!OutputPolicy.PreserveImportedAssetOrder)
            return Array.AsReadOnly(result.ToArray());

        ZoneAssetEntry[] importedOrder = _entries
            .OrderBy(entry => entry, Comparer<ZoneAssetEntry>.Create(ImportedCompatibilityOrder))
            .ToArray();
        var precedingProviders = new HashSet<ZoneAssetKey>();
        foreach (ZoneAssetEntry entry in importedOrder)
        {
            if (entry.Intent == ZoneAssetReferenceIntent.Alias &&
                entry.AliasTarget is { } aliasTarget &&
                !precedingProviders.Contains(aliasTarget))
            {
                throw new InvalidDataException(
                    $"Imported alias '{entry.Key}' does not follow a materialized target '{aliasTarget}'.");
            }
            if (entry.Intent is
                    ZoneAssetReferenceIntent.Owned or
                    ZoneAssetReferenceIntent.External or
                    ZoneAssetReferenceIntent.Alias)
            {
                precedingProviders.Add(entry.Key);
            }
        }
        return Array.AsReadOnly(importedOrder);

        void Visit(ZoneAssetEntry entry)
        {
            if (completed.Contains(entry.EntryId))
                return;
            int cycleStart = active.FindIndex(candidate =>
                string.Equals(
                    candidate.EntryId,
                    entry.EntryId,
                    StringComparison.Ordinal));
            if (cycleStart >= 0)
            {
                string cycle = string.Join(
                    " -> ",
                    active
                        .Skip(cycleStart)
                        .Append(entry)
                        .Select(candidate =>
                            $"{candidate.Key} [{candidate.EntryId}]"));
                throw new InvalidDataException($"Zone link dependency cycle: {cycle}.");
            }

            active.Add(entry);
            foreach (ZoneAssetDependency dependency in entry.Dependencies
                         .OrderBy(value => value.Target.Type)
                         .ThenBy(value => value.Target.LogicalName, StringComparer.Ordinal)
                         .ThenBy(value => value.Kind)
                         .ThenBy(value => value.OwnerPath, StringComparer.Ordinal))
            {
                if (!byKey.TryGetValue(dependency.Target, out ZoneAssetEntry? target))
                {
                    continue;
                }
                Visit(target);
            }
            if (entry.Intent == ZoneAssetReferenceIntent.Alias && entry.AliasTarget is { } aliasTarget)
            {
                if (!byKey.TryGetValue(aliasTarget, out ZoneAssetEntry? target))
                    throw new InvalidDataException($"Alias '{entry.Key}' targets missing entry '{aliasTarget}'.");
                Visit(target);
            }
            active.RemoveAt(active.Count - 1);
            completed.Add(entry.EntryId);
            result.Add(entry);
        }
    }

    private static int MaterializedProviderPreference(
        ZoneAssetReferenceIntent intent) =>
        intent switch
        {
            ZoneAssetReferenceIntent.Owned or
            ZoneAssetReferenceIntent.External => 0,
            ZoneAssetReferenceIntent.Alias => 1,
            _ => 2
        };

    /// <summary>
    /// Compares frozen linker inputs as graph values without depending on
    /// dictionary insertion order. Build payloads use their own equality
    /// contracts; the linker cannot safely guess semantic equality for an
    /// arbitrary <see cref="IXAssetBuildData"/> implementation.
    /// </summary>
    public bool ValueEquals(ZoneLinkRequest? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null ||
            OutputPolicy != other.OutputPolicy ||
            LayoutPolicy.ExternalSize != other.LayoutPolicy.ExternalSize ||
            LayoutPolicy.DecodedAlignment != other.LayoutPolicy.DecodedAlignment ||
            !LayoutPolicy.BlockSizeFloors.SequenceEqual(other.LayoutPolicy.BlockSizeFloors) ||
            !_scriptStrings.SequenceEqual(other._scriptStrings, StringComparer.Ordinal))
        {
            return false;
        }

        ZoneAssetEntry[] left = CanonicalEntries(_entries);
        ZoneAssetEntry[] right = CanonicalEntries(other._entries);
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (!EntryValueEquals(left[index], right[index]))
                return false;
        }
        return true;
    }

    private void ValidateScriptStrings()
    {
        foreach (string? value in _scriptStrings)
        {
            if (value is not null && (value.IndexOf('\0') >= 0 || value.Any(character => character > byte.MaxValue)))
                throw new InvalidDataException("Script strings must be null or Latin-1 C-string values.");
        }
    }

    private int StableOrder(ZoneAssetEntry left, ZoneAssetEntry right)
    {
        if (OutputPolicy.PreferImportedOrder && left.ImportedOrder is { } leftOrder && right.ImportedOrder is { } rightOrder)
        {
            int order = leftOrder.CompareTo(rightOrder);
            if (order != 0) return order;
        }
        if (OutputPolicy.PreferImportedOrder && left.ImportedOrder is not null && right.ImportedOrder is null) return -1;
        if (OutputPolicy.PreferImportedOrder && left.ImportedOrder is null && right.ImportedOrder is not null) return 1;
        int type = left.Key.Type.CompareTo(right.Key.Type);
        if (type != 0) return type;
        int name = StringComparer.Ordinal.Compare(left.Key.LogicalName, right.Key.LogicalName);
        return name != 0 ? name : StringComparer.Ordinal.Compare(left.EntryId, right.EntryId);
    }

    private static int ImportedCompatibilityOrder(
        ZoneAssetEntry left,
        ZoneAssetEntry right)
    {
        if (left.ImportedOrder is { } leftOrder &&
            right.ImportedOrder is { } rightOrder)
        {
            int order = leftOrder.CompareTo(rightOrder);
            if (order != 0)
                return order;
        }
        if (left.ImportedOrder is not null && right.ImportedOrder is null)
            return -1;
        if (left.ImportedOrder is null && right.ImportedOrder is not null)
            return 1;
        int type = left.Key.Type.CompareTo(right.Key.Type);
        if (type != 0)
            return type;
        int name = StringComparer.Ordinal.Compare(
            left.Key.LogicalName,
            right.Key.LogicalName);
        return name != 0
            ? name
            : StringComparer.Ordinal.Compare(left.EntryId, right.EntryId);
    }

    private static string PathSuffix(ZoneAssetDependency dependency) =>
        string.IsNullOrWhiteSpace(dependency.OwnerPath) ? string.Empty : $" at {dependency.OwnerPath}";

    private static void ValidateAllRequiredTargets(
        IReadOnlyList<ZoneAssetEntry> entries,
        IReadOnlyDictionary<ZoneAssetKey, ZoneAssetEntry> byKey)
    {
        string[] missing = entries
            .SelectMany(entry =>
                entry.Dependencies
                    .Where(dependency => dependency.IsRequired && !byKey.ContainsKey(dependency.Target))
                    .Select(dependency =>
                        $"{entry.Key}{PathSuffix(dependency)} -> {dependency.Target}")
                    .Concat(
                        entry.Intent == ZoneAssetReferenceIntent.Alias &&
                        entry.AliasTarget is { } aliasTarget &&
                        !byKey.ContainsKey(aliasTarget)
                            ? [$"{entry.Key} at <aliasTarget> -> {aliasTarget}"]
                            : []))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length == 0)
        {
            ValidateResolvedTargetIntents(entries, byKey);
            return;
        }

        throw new InvalidDataException(
            "Zone link graph has missing required dependencies:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(value => $" - {value}")));
    }

    private static void ValidateResolvedTargetIntents(
        IReadOnlyList<ZoneAssetEntry> entries,
        IReadOnlyDictionary<ZoneAssetKey, ZoneAssetEntry> byKey)
    {
        string[] contradictions = entries
            .SelectMany(owner => owner.Dependencies
                .Where(dependency =>
                    dependency.Kind is ZoneAssetDependencyKind.Required or ZoneAssetDependencyKind.External &&
                    byKey.TryGetValue(dependency.Target, out ZoneAssetEntry? target) &&
                    target.Intent is ZoneAssetReferenceIntent.Null or ZoneAssetReferenceIntent.OpaqueNativeNoOp)
                .Select(dependency =>
                    $"{owner.Key}{PathSuffix(dependency)} -> {dependency.Target} " +
                    $"({byKey[dependency.Target].Intent})"))
            .Concat(entries
                .Where(entry =>
                    entry.Intent == ZoneAssetReferenceIntent.Alias &&
                    entry.AliasTarget is { } aliasTarget &&
                    byKey.TryGetValue(aliasTarget, out ZoneAssetEntry? target) &&
                    target.Intent is
                        ZoneAssetReferenceIntent.Null or
                        ZoneAssetReferenceIntent.OpaqueNativeNoOp)
                .Select(entry =>
                    $"{entry.Key} at <aliasTarget> -> {entry.AliasTarget} " +
                    $"({byKey[entry.AliasTarget!.Value].Intent})"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (contradictions.Length == 0)
            return;

        throw new InvalidDataException(
            "Zone link graph has dependency targets with contradictory null/no-op intent:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, contradictions.Select(value => $" - {value}")));
    }

    private static ZoneAssetEntry[] CanonicalEntries(IEnumerable<ZoneAssetEntry> entries) =>
        entries
            .OrderBy(entry => entry.Key.Type)
            .ThenBy(entry => entry.Key.LogicalName, StringComparer.Ordinal)
            .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
            .ToArray();

    private static bool EntryValueEquals(ZoneAssetEntry left, ZoneAssetEntry right) =>
        string.Equals(left.EntryId, right.EntryId, StringComparison.Ordinal) &&
        left.Key == right.Key &&
        left.Intent == right.Intent &&
        Equals(left.BuildData, right.BuildData) &&
        left.AliasTarget == right.AliasTarget &&
        left.ImportedOrder == right.ImportedOrder &&
        left.OpaqueHeader == right.OpaqueHeader &&
        string.Equals(left.OriginalSpelling, right.OriginalSpelling, StringComparison.Ordinal) &&
        left.Dependencies.SequenceEqual(right.Dependencies);
}

/// <summary>Mutable construction helper.  It is the only graph-mutation API;
/// callers freeze it into <see cref="ZoneLinkRequest"/> before linking.</summary>
public sealed class ZoneBuildGraphBuilder
{
    private readonly Dictionary<string, ZoneAssetEntry> _entries =
        new(StringComparer.Ordinal);

    public ZoneBuildGraphBuilder AddOwned(
        ZoneAssetKey key,
        IXAssetBuildData buildData,
        IEnumerable<ZoneAssetDependency>? dependencies = null,
        int? importedOrder = null,
        string? entryId = null,
        string? originalSpelling = null)
    {
        Add(new ZoneAssetEntry(
            entryId ?? key.ToString(),
            key,
            ZoneAssetReferenceIntent.Owned,
            buildData,
            importedOrder: importedOrder,
            dependencies: dependencies,
            originalSpelling: originalSpelling));
        return this;
    }

    public ZoneBuildGraphBuilder AddExternal(
        ZoneAssetKey key,
        int? importedOrder = null,
        string? entryId = null,
        string? originalSpelling = null)
    {
        Add(new ZoneAssetEntry(
            entryId ?? key.ToString(),
            key,
            ZoneAssetReferenceIntent.External,
            importedOrder: importedOrder,
            originalSpelling: originalSpelling));
        return this;
    }

    public ZoneBuildGraphBuilder AddNull(
        ZoneAssetKey key,
        int? importedOrder = null,
        string? entryId = null,
        string? originalSpelling = null)
    {
        Add(new ZoneAssetEntry(
            entryId ?? key.ToString(),
            key,
            ZoneAssetReferenceIntent.Null,
            importedOrder: importedOrder,
            originalSpelling: originalSpelling));
        return this;
    }

    public ZoneBuildGraphBuilder AddAlias(
        ZoneAssetKey key,
        ZoneAssetKey target,
        int? importedOrder = null,
        string? entryId = null,
        string? originalSpelling = null)
    {
        Add(new ZoneAssetEntry(
            entryId ?? key.ToString(),
            key,
            ZoneAssetReferenceIntent.Alias,
            aliasTarget: target,
            importedOrder: importedOrder,
            originalSpelling: originalSpelling));
        return this;
    }

    public ZoneBuildGraphBuilder AddOpaqueNativeNoOp(
        ZoneAssetKey key,
        int opaqueHeader,
        int? importedOrder = null,
        string? entryId = null,
        string? originalSpelling = null)
    {
        Add(new ZoneAssetEntry(
            entryId ?? key.ToString(),
            key,
            ZoneAssetReferenceIntent.OpaqueNativeNoOp,
            importedOrder: importedOrder,
            opaqueHeader: opaqueHeader,
            originalSpelling: originalSpelling));
        return this;
    }

    public ZoneBuildGraphBuilder ReplaceOwned(
        ZoneAssetKey key,
        IXAssetBuildData buildData,
        IEnumerable<ZoneAssetDependency>? dependencies = null)
    {
        ZoneAssetEntry existing = RequireSingle(key, "replace");
        _entries[existing.EntryId] = new ZoneAssetEntry(existing.EntryId, key, ZoneAssetReferenceIntent.Owned, buildData,
            importedOrder: existing.ImportedOrder,
            dependencies: dependencies ?? existing.DeclaredDependencies,
            originalSpelling: existing.OriginalSpelling);
        return this;
    }

    public ZoneBuildGraphBuilder Remove(ZoneAssetKey key, ZoneRemoveDanglingPolicy policy = ZoneRemoveDanglingPolicy.Reject)
    {
        ZoneAssetEntry existing = RequireSingle(key, "remove");

        string[] dependents = _entries.Values
            .SelectMany(entry =>
                entry.Dependencies
                    .Where(dependency => dependency.IsRequired && dependency.Target == key)
                    .Select(dependency =>
                        $"{entry.Key}{(string.IsNullOrWhiteSpace(dependency.OwnerPath) ? " at <unspecified>" : $" at {dependency.OwnerPath}")}")
                    .Concat(entry.AliasTarget == key ? [$"{entry.Key} at <aliasTarget>"] : []))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (dependents.Length != 0 && policy == ZoneRemoveDanglingPolicy.Reject)
        {
            throw new InvalidDataException(
                $"Cannot remove '{key}'; required by:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, dependents.Select(value => $" - {value}")));
        }

        if (dependents.Length != 0)
        {
            _entries[existing.EntryId] = new ZoneAssetEntry(existing.EntryId, key, ZoneAssetReferenceIntent.External,
                importedOrder: existing.ImportedOrder,
                originalSpelling: existing.OriginalSpelling);
        }
        else
        {
            _entries.Remove(existing.EntryId);
        }
        return this;
    }

    public ZoneLinkRequest Freeze(
        IEnumerable<string?>? scriptStrings = null,
        ZoneLinkOutputPolicy? outputPolicy = null,
        ZoneLinkLayoutPolicy? layoutPolicy = null) =>
        new(_entries.Values, scriptStrings, outputPolicy, layoutPolicy);

    private void Add(ZoneAssetEntry entry)
    {
        if (!_entries.TryAdd(entry.EntryId, entry))
        {
            throw new InvalidDataException(
                $"A zone entry with ID '{entry.EntryId}' already exists.");
        }
    }

    private ZoneAssetEntry RequireSingle(ZoneAssetKey key, string operation)
    {
        ZoneAssetEntry[] matches = _entries.Values
            .Where(entry => entry.Key == key)
            .ToArray();
        return matches.Length switch
        {
            0 => throw new KeyNotFoundException(
                $"Cannot {operation} missing zone entry '{key}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Cannot {operation} logical key '{key}' because it has " +
                $"{matches.Length:N0} row occurrences; key-based mutation is ambiguous.")
        };
    }
}
