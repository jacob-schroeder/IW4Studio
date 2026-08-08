using IW4.Assets.Assets.Image;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Emitters.Emission;

/// <summary>Immutable destination address in the projected seven-block zone.</summary>
public readonly record struct EmissionAddress(XFileBlockType Block, int Offset)
{
    public int ToPackedPointer() => XPointerCodec.Encode(new XBlockAddress(Block, Offset));
}

/// <summary>One logical destination allocation consumed by the link stream.</summary>
public sealed record EmissionAllocation(
    XFileBlockType Block,
    int Offset,
    int Size,
    string? Owner);

/// <summary>
/// Legacy body emitters were authored around a monotonic TEMP cursor.  New
/// canonical linking must select <see cref="NativeScoped"/>; the legacy mode
/// exists only for the SourceSegments compatibility bridge.
/// </summary>
public enum TempAllocationMode
{
    LegacyMonotonic,
    NativeScoped
}

/// <summary>
/// One streamed GfxImage definition and the exact four selected-language
/// DB-header records that must occupy its stream-image index. Serialized
/// source offsets are normalized because they are container locations, not
/// wire semantics.
/// </summary>
public sealed class StreamedGfxImageEmissionContribution :
    IEquatable<StreamedGfxImageEmissionContribution>
{
    private readonly IReadOnlyList<DbHeaderImageStreamEntry> _entries;

    internal StreamedGfxImageEmissionContribution(
        ZoneAssetKey imageKey,
        IEnumerable<DbHeaderImageStreamEntry> entries)
    {
        if (imageKey.Type != XAssetType.Image)
        {
            throw new ArgumentException(
                "A streamed-image contribution requires an Image key.",
                nameof(imageKey));
        }
        ArgumentNullException.ThrowIfNull(entries);
        DbHeaderImageStreamEntry[] copied = entries
            .Select(entry =>
                new DbHeaderImageStreamEntry(
                    entry.FileIndex,
                    entry.SourceStart,
                    entry.SourceEnd,
                    entry.BlockOffset,
                    entry.StreamOffset,
                    SerializedOffset: -1))
            .ToArray();
        if (copied.Length != GfxImageStreamData.EntryCount)
        {
            throw new InvalidDataException(
                "A streamed GfxImage contribution must contain exactly " +
                $"{GfxImageStreamData.EntryCount} " +
                $"selected-language entries; found {copied.Length}.");
        }

        ImageKey = imageKey;
        _entries = Array.AsReadOnly(copied);
    }

    public ZoneAssetKey ImageKey { get; }

    public IReadOnlyList<DbHeaderImageStreamEntry>
        SelectedLanguageStreamEntries => _entries;

    public bool Equals(StreamedGfxImageEmissionContribution? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ImageKey == other.ImageKey &&
        _entries.SequenceEqual(other._entries);

    public override bool Equals(object? obj) =>
        obj is StreamedGfxImageEmissionContribution other &&
        Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ImageKey);
        foreach (DbHeaderImageStreamEntry entry in _entries)
            hash.Add(entry);
        return hash.ToHashCode();
    }

    public static bool operator ==(
        StreamedGfxImageEmissionContribution? left,
        StreamedGfxImageEmissionContribution? right) =>
        EqualityComparer<StreamedGfxImageEmissionContribution>.Default
            .Equals(left, right);

    public static bool operator !=(
        StreamedGfxImageEmissionContribution? left,
        StreamedGfxImageEmissionContribution? right) =>
        !(left == right);
}

/// <summary>Pass-one allocation state; it never owns final output bytes.</summary>
public sealed class EmissionPlan
{
    private sealed record BlockFrame(
        XFileBlockType PreviousBlock,
        XFileBlockType PushedBlock,
        int PushedBlockEntryCursor,
        string? Owner);

    private readonly int[] _cursors = new int[(int)XFileBlockType.COUNT];
    private readonly int[] _highWater = new int[(int)XFileBlockType.COUNT];
    private readonly Stack<BlockFrame> _stack = new();
    private readonly List<EmissionAllocation> _allocations = [];
    private readonly Dictionary<string, EmissionAddress> _stringAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EmissionAddress> _persistentXAssetAliasCells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EmissionAddress> _persistentSoundAliasCells = new(StringComparer.Ordinal);
    private readonly Dictionary<int, EmissionAddress> _materialTechniqueOwnersByImportedRaw = [];
    private readonly Dictionary<int, EmissionAddress> _materialVertexDeclarationOwnersByImportedRaw = [];
    private readonly Dictionary<int, EmissionAddress> _materialLiteralOwnersByImportedRaw = [];
    private readonly Dictionary<int, EmissionAddress> _materialWaterOwnersByImportedRaw = [];
    private readonly Dictionary<int, EmissionAddress> _materialShaderBytecodeAliasCellsByImportedRaw = [];
    private readonly Dictionary<int, EmissionAddress> _materialLoadBitsAliasCells = [];
    private readonly Dictionary<object, EmissionAddress> _persistentObjectAliases =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Dictionary<string, EmissionAddress>> _persistentObjectViewAliases =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, ReusableStorageMaterialization> _reusableStorage = [];
    private readonly HashSet<int> _invalidReusableStorage = [];
    private readonly List<StreamedGfxImageEmissionContribution>
        _streamedGfxImageContributions = [];
    private readonly TempAllocationMode _tempAllocationMode;
    private readonly bool _preserveImportedXAssetPointerValues;
    private XFileBlockType _current = XFileBlockType.TEMP;

    public EmissionPlan(
        TempAllocationMode tempAllocationMode = TempAllocationMode.LegacyMonotonic,
        bool preserveImportedXAssetPointerValues = false)
    {
        _tempAllocationMode = tempAllocationMode;
        _preserveImportedXAssetPointerValues =
            preserveImportedXAssetPointerValues;
    }

    public TempAllocationMode TempAllocationMode => _tempAllocationMode;

    /// <summary>
    /// Imported compatibility may retain a packed alias into a dependency
    /// zone when no current-zone owner cell exists for that identity.
    /// Canonical/greenfield linking never consumes imported raw addresses.
    /// </summary>
    public bool PreserveImportedXAssetPointerValues =>
        _preserveImportedXAssetPointerValues;

    public IReadOnlyList<int> HighWater => Array.AsReadOnly(_highWater.ToArray());

    /// <summary>Destination allocation order consumed by the source writer.</summary>
    public IReadOnlyList<EmissionAllocation> Allocations =>
        Array.AsReadOnly(_allocations.ToArray());

    /// <summary>
    /// Selected-language DB-header records contributed by streamed GfxImage
    /// definitions, flattened in the order those definitions were planned.
    /// Each contribution is an indivisible group of four records.
    /// </summary>
    public IReadOnlyList<DbHeaderImageStreamEntry>
        SelectedLanguageImageStreamEntries =>
        Array.AsReadOnly(_streamedGfxImageContributions
            .SelectMany(contribution =>
                contribution.SelectedLanguageStreamEntries)
            .ToArray());

    public IReadOnlyList<StreamedGfxImageEmissionContribution>
        StreamedGfxImageContributions =>
        Array.AsReadOnly(_streamedGfxImageContributions.ToArray());

    /// <summary>Number of streamed GfxImage definitions planned so far.</summary>
    public int StreamedGfxImageCount => _streamedGfxImageContributions.Count;

    /// <summary>Previously inline-materialized strings eligible for packed aliases.</summary>
    public IDictionary<string, EmissionAddress> StringAliases => _stringAliases;

    /// <summary>
    /// First persistent pointer cells for nested XAsset identities. AliasCell
    /// packed pointers dereference these cells; they must never point at the
    /// child placeholder root itself. TEMP cells are intentionally excluded
    /// because native-scoped TEMP storage is rewound and reused.
    /// </summary>
    public IDictionary<string, EmissionAddress> PersistentXAssetAliasCells =>
        _persistentXAssetAliasCells;

    /// <summary>
    /// First materialized snd_alias_list_name cell for each symbolic alias.
    /// Packed sound pointers target this four-byte nested cell directly.
    /// </summary>
    public IDictionary<string, EmissionAddress> PersistentSoundAliasCells =>
        _persistentSoundAliasCells;

    /// <summary>
    /// Relocates imported direct MaterialTechnique roots. Unlike nested
    /// XAsset aliases these pointers target the LARGE technique body itself,
    /// not an identity-bearing XAsset pointer cell.
    /// </summary>
    internal bool TryGetMaterialTechniqueOwner(
        int importedRaw,
        out EmissionAddress address) =>
        _materialTechniqueOwnersByImportedRaw.TryGetValue(
            importedRaw,
            out address);

    internal void RegisterMaterialTechniqueOwner(
        int importedRaw,
        EmissionAddress address)
    {
        if (XPointerCodec.GetType(importedRaw) != PointerType.Offset)
        {
            throw new InvalidDataException(
                $"MaterialTechnique owner raw 0x{unchecked((uint)importedRaw):X8} is not packed.");
        }
        if (address.Block != XFileBlockType.LARGE)
        {
            throw new InvalidDataException(
                $"MaterialTechnique owner {address} is not in LARGE.");
        }
        if (_materialTechniqueOwnersByImportedRaw.TryGetValue(
                importedRaw,
                out EmissionAddress existing))
        {
            if (existing != address)
            {
                throw new InvalidDataException(
                    $"MaterialTechnique imported owner 0x{unchecked((uint)importedRaw):X8} " +
                    $"was relocated to both {existing} and {address}.");
            }
            return;
        }
        _materialTechniqueOwnersByImportedRaw.Add(importedRaw, address);
    }

    internal bool TryGetMaterialVertexDeclarationOwner(
        int importedRaw,
        out EmissionAddress address) =>
        _materialVertexDeclarationOwnersByImportedRaw.TryGetValue(
            importedRaw,
            out address);

    internal void RegisterMaterialVertexDeclarationOwner(
        int importedRaw,
        EmissionAddress address) =>
        RegisterLargeDirectOwner(
            _materialVertexDeclarationOwnersByImportedRaw,
            importedRaw,
            address,
            "MaterialVertexDeclaration");

    internal bool TryGetMaterialLiteralOwner(
        int importedRaw,
        out EmissionAddress address) =>
        _materialLiteralOwnersByImportedRaw.TryGetValue(
            importedRaw,
            out address);

    internal void RegisterMaterialLiteralOwner(
        int importedRaw,
        EmissionAddress address) =>
        RegisterLargeDirectOwner(
            _materialLiteralOwnersByImportedRaw,
            importedRaw,
            address,
            "MaterialShaderLiteralConstant");

    internal bool TryGetMaterialWaterOwner(
        int importedRaw,
        out EmissionAddress address) =>
        _materialWaterOwnersByImportedRaw.TryGetValue(
            importedRaw,
            out address);

    internal void RegisterMaterialWaterOwner(
        int importedRaw,
        EmissionAddress address) =>
        RegisterLargeDirectOwner(
            _materialWaterOwnersByImportedRaw,
            importedRaw,
            address,
            "MaterialWater");

    internal bool TryGetMaterialShaderBytecodeAliasCell(
        int importedRaw,
        out EmissionAddress address) =>
        _materialShaderBytecodeAliasCellsByImportedRaw.TryGetValue(
            importedRaw,
            out address);

    internal void RegisterMaterialShaderBytecodeAliasCell(
        int importedRaw,
        EmissionAddress address) =>
        RegisterLargeDirectOwner(
            _materialShaderBytecodeAliasCellsByImportedRaw,
            importedRaw,
            address,
            "MaterialShaderBytecode alias cell");

    private static void RegisterLargeDirectOwner(
        Dictionary<int, EmissionAddress> owners,
        int importedRaw,
        EmissionAddress address,
        string memberName)
    {
        if (XPointerCodec.GetType(importedRaw) != PointerType.Offset)
        {
            throw new InvalidDataException(
                $"{memberName} owner raw 0x{unchecked((uint)importedRaw):X8} is not packed.");
        }
        if (address.Block != XFileBlockType.LARGE)
        {
            throw new InvalidDataException(
                $"{memberName} owner {address} is not in LARGE.");
        }
        if (owners.TryGetValue(importedRaw, out EmissionAddress existing))
        {
            if (existing != address)
            {
                throw new InvalidDataException(
                    $"{memberName} imported owner 0x{unchecked((uint)importedRaw):X8} " +
                    $"was relocated to both {existing} and {address}.");
            }
            return;
        }
        owners.Add(importedRaw, address);
    }

    internal bool TryGetMaterialLoadBitsAliasCell(
        int token,
        out EmissionAddress address)
    {
        if (token <= 0)
            throw new ArgumentOutOfRangeException(nameof(token));
        return _materialLoadBitsAliasCells.TryGetValue(token, out address);
    }

    internal void RegisterMaterialLoadBitsAliasCell(
        int token,
        EmissionAddress address)
    {
        if (token <= 0)
            throw new ArgumentOutOfRangeException(nameof(token));
        if (address.Block == XFileBlockType.TEMP)
        {
            throw new InvalidDataException(
                "A GfxStateBits loadBits alias owner cannot reside in native-scoped TEMP storage.");
        }
        if (_materialLoadBitsAliasCells.TryGetValue(
                token,
                out EmissionAddress existing))
        {
            if (existing != address)
            {
                throw new InvalidDataException(
                    "One GfxStateBits loadBits alias identity was assigned two destination cells.");
            }
            return;
        }
        _materialLoadBitsAliasCells.Add(token, address);
    }

    /// <summary>
    /// Appends one streamed GfxImage's selected-language sidecar records.
    /// This collection deliberately has no replacement/removal operation:
    /// DB-header order must remain identical to definition planning order.
    /// </summary>
    internal void AppendStreamedGfxImageContribution(
        string imageName,
        IReadOnlyList<DbHeaderImageStreamEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);
        ArgumentNullException.ThrowIfNull(entries);
        _streamedGfxImageContributions.Add(
            new StreamedGfxImageEmissionContribution(
                new ZoneAssetKey(
                    XAssetType.Image,
                    imageName),
                entries));
    }

    internal bool TryGetReusableStorage(
        int token,
        ReadOnlySpan<byte> semanticFingerprint,
        out EmissionAddress address)
    {
        if (token <= 0)
            throw new ArgumentOutOfRangeException(nameof(token));
        if (_invalidReusableStorage.Contains(token))
        {
            address = default;
            return false;
        }
        if (!_reusableStorage.TryGetValue(token, out ReusableStorageMaterialization? existing))
        {
            address = default;
            return false;
        }
        if (!semanticFingerprint.SequenceEqual(existing.SemanticFingerprint))
        {
            _invalidReusableStorage.Add(token);
            address = default;
            return false;
        }
        address = existing.Address;
        return true;
    }

    internal void RegisterReusableStorage(
        int token,
        ReadOnlySpan<byte> semanticFingerprint,
        EmissionAddress address)
    {
        if (token <= 0)
            throw new ArgumentOutOfRangeException(nameof(token));
        if (_invalidReusableStorage.Contains(token))
            return;
        if (_reusableStorage.TryGetValue(token, out ReusableStorageMaterialization? existing))
        {
            if (existing.Address != address ||
                !semanticFingerprint.SequenceEqual(existing.SemanticFingerprint))
            {
                _invalidReusableStorage.Add(token);
            }
            return;
        }
        _reusableStorage.Add(
            token,
            new ReusableStorageMaterialization(address, semanticFingerprint.ToArray()));
    }

    /// <summary>
    /// Finds a previously emitted identity-bearing child that resides in
    /// persistent destination storage. TEMP roots are deliberately excluded
    /// because native-scoped TEMP frames are reused after every asset.
    /// </summary>
    internal bool TryGetPersistentObjectAlias(
        object value,
        out EmissionAddress address) =>
        _persistentObjectAliases.TryGetValue(value, out address);

    /// <summary>
    /// Finds an identity-bearing region only when both its semantic object
    /// identity and serialized view agree. The view includes the wire kind
    /// and length so one backing object cannot be reused through an
    /// incompatible interpretation.
    /// </summary>
    internal bool TryGetPersistentObjectAlias(
        object value,
        string serializedView,
        out EmissionAddress address)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(serializedView);
        if (_persistentObjectViewAliases.TryGetValue(value, out Dictionary<string, EmissionAddress>? views))
            return views.TryGetValue(serializedView, out address);
        address = default;
        return false;
    }

    internal void RegisterPersistentObjectAlias(
        object value,
        EmissionAddress address)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (address.Block == XFileBlockType.TEMP)
            return;
        if (_persistentObjectAliases.TryGetValue(value, out EmissionAddress existing))
        {
            if (existing != address)
                throw new InvalidDataException("One semantic object identity was assigned two destination addresses.");
            return;
        }
        _persistentObjectAliases.Add(value, address);
    }

    internal void RegisterPersistentObjectAlias(
        object value,
        string serializedView,
        EmissionAddress address)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(serializedView);
        if (address.Block == XFileBlockType.TEMP)
            return;
        if (!_persistentObjectViewAliases.TryGetValue(value, out Dictionary<string, EmissionAddress>? views))
        {
            views = new Dictionary<string, EmissionAddress>(StringComparer.Ordinal);
            _persistentObjectViewAliases.Add(value, views);
        }
        if (views.TryGetValue(serializedView, out EmissionAddress existing))
        {
            if (existing != address)
                throw new InvalidDataException(
                    "One semantic object identity and serialized view were assigned two destination addresses.");
            return;
        }
        views.Add(serializedView, address);
    }

    public void Push(XFileBlockType block, string? owner = null)
    {
        ValidateBlock(block);
        _stack.Push(new BlockFrame(_current, block, _cursors[(int)block], owner));
        _current = block;
    }

    public void Pop(XFileBlockType expected)
    {
        if (_stack.Count == 0)
            throw new InvalidDataException($"Cannot pop {expected}; the emitter block stack is empty.");

        BlockFrame frame = _stack.Peek();
        if (_current != expected || frame.PushedBlock != expected)
            throw new InvalidDataException($"Cannot pop {expected}; current emitter block is {_current}.");

        _stack.Pop();
        if (expected == XFileBlockType.TEMP && _tempAllocationMode == TempAllocationMode.NativeScoped)
            _cursors[(int)XFileBlockType.TEMP] = frame.PushedBlockEntryCursor;
        _current = frame.PreviousBlock;
    }

    public void Align(int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        int aligned = checked((_cursors[(int)_current] + alignment - 1) & ~(alignment - 1));
        _cursors[(int)_current] = aligned;
        _highWater[(int)_current] = Math.Max(_highWater[(int)_current], aligned);
    }

    public EmissionAddress Allocate(int byteCount, int alignment = 1, string? owner = null)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        Align(alignment);
        int offset = _cursors[(int)_current];
        int end = checked(offset + byteCount);
        if (end >= 0x10000000)
            throw new InvalidDataException($"{_current} exceeds the 28-bit packed-address range.");
        _cursors[(int)_current] = end;
        _highWater[(int)_current] = Math.Max(_highWater[(int)_current], end);
        _allocations.Add(new EmissionAllocation(
            _current,
            offset,
            byteCount,
            owner));
        return new EmissionAddress(_current, offset);
    }

    public EmissionAddress AllocateWithoutSource(
        int byteCount,
        int alignment,
        string owner,
        string memberPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberPath);
        return Allocate(
            byteCount,
            alignment,
            $"{owner}.{memberPath}");
    }

    /// <summary>
    /// Allocates the four-byte insert-pointer alias cell from LARGE regardless
    /// of the block active at the call site.
    /// </summary>
    public EmissionAddress AllocateInsertPointerCell(
        string owner,
        string memberPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberPath);

        Push(XFileBlockType.LARGE, $"{owner}.{memberPath}");
        try
        {
            return AllocateWithoutSource(
                sizeof(int),
                sizeof(int),
                owner,
                memberPath);
        }
        finally
        {
            Pop(XFileBlockType.LARGE);
        }
    }

    /// <summary>Fails when a caller tries to finalize an incomplete block
    /// sequence.  The compiler calls this before exposing a decoded zone.</summary>
    public void EnsureBalanced()
    {
        if (_stack.Count != 0)
        {
            BlockFrame frame = _stack.Peek();
            throw new InvalidDataException(
                $"Emitter block stack is unbalanced: {frame.PushedBlock} pushed from {frame.PreviousBlock}" +
                (frame.Owner is null ? "." : $" for {frame.Owner}."));
        }
        if (_current != XFileBlockType.TEMP)
            throw new InvalidDataException($"Emitter block stack ended in {_current}, not TEMP.");
    }

    private static void ValidateBlock(XFileBlockType block)
    {
        if (block is < XFileBlockType.TEMP or >= XFileBlockType.COUNT)
            throw new ArgumentOutOfRangeException(nameof(block));
    }

    private sealed record ReusableStorageMaterialization(
        EmissionAddress Address,
        byte[] SemanticFingerprint);
}
