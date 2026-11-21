using IW4.Game.Assets.Fx;
using IW4.Game.Assets.ImpactFx;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

/// <summary>A detached edit of the native ImpactFx effect matrix.</summary>
public sealed class ImpactFxDraft
{
    private const int SlotCount = FxImpactEntry.SurfaceEffectCount + FxImpactEntry.FleshEffectCount;
    private readonly int _offset;
    private readonly XRuntimeAddress? _runtimeAddress;
    private readonly XPointer<string> _namePointer;
    private readonly XPointer<FxImpactEntry[]> _originalEntriesPointer;
    private readonly bool _originalHasTable;
    private readonly int[] _entryOffsets;
    private readonly FxEffectDefAsset?[,] _originalEffects;
    private readonly XPointer<FxEffectDefAsset>[,] _originalPointers;
    private FxEffectDefAsset?[,] _effects;
    private XPointer<FxEffectDefAsset>[,] _pointers;
    private XPointer<FxImpactEntry[]> _entriesPointer;
    private bool _hasTable;

    internal ImpactFxDraft(FxImpactTableAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Name = asset.Name;
        _offset = asset.Offset;
        _runtimeAddress = asset.RuntimeAddress;
        _namePointer = asset.NamePointer;
        _entriesPointer = asset.EntriesPointer;
        _originalEntriesPointer = asset.EntriesPointer;
        IReadOnlyList<FxImpactEntry> entries = asset.Entries ??
            throw new InvalidDataException("ImpactFx entries cannot be null.");
        if (entries.Count != 0 && entries.Count != FxImpactTableAsset.EntryCount)
            throw new InvalidDataException("ImpactFx must contain exactly fifteen native rows.");
        _hasTable = entries.Count != 0;
        _originalHasTable = _hasTable;
        _entryOffsets = new int[FxImpactTableAsset.EntryCount];
        _effects = new FxEffectDefAsset?[FxImpactTableAsset.EntryCount, SlotCount];
        _pointers = new XPointer<FxEffectDefAsset>[FxImpactTableAsset.EntryCount, SlotCount];
        for (int row = 0; row < entries.Count; row++)
        {
            FxImpactEntry entry = entries[row] ??
                throw new InvalidDataException($"ImpactFx row {row} is null.");
            if (entry.SurfaceEffects.Count != FxImpactEntry.SurfaceEffectCount ||
                entry.SurfaceEffectPointers.Count != FxImpactEntry.SurfaceEffectCount ||
                entry.FleshEffects.Count != FxImpactEntry.FleshEffectCount ||
                entry.FleshEffectPointers.Count != FxImpactEntry.FleshEffectCount)
                throw new InvalidDataException($"ImpactFx row {row} has an invalid effect band length.");
            _entryOffsets[row] = entry.Offset;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                _effects[row, slot] = slot < FxImpactEntry.SurfaceEffectCount
                    ? entry.SurfaceEffects[slot]
                    : entry.FleshEffects[slot - FxImpactEntry.SurfaceEffectCount];
                _pointers[row, slot] = slot < FxImpactEntry.SurfaceEffectCount
                    ? entry.SurfaceEffectPointers[slot]
                    : entry.FleshEffectPointers[slot - FxImpactEntry.SurfaceEffectCount];
            }
        }
        _originalEffects = (FxEffectDefAsset?[,])_effects.Clone();
        _originalPointers = (XPointer<FxEffectDefAsset>[,])_pointers.Clone();
    }

    private ImpactFxDraft(ImpactFxDraft source)
    {
        Name = source.Name;
        _offset = source._offset;
        _runtimeAddress = source._runtimeAddress;
        _namePointer = source._namePointer;
        _originalEntriesPointer = source._originalEntriesPointer;
        _originalHasTable = source._originalHasTable;
        _entryOffsets = source._entryOffsets;
        _originalEffects = source._originalEffects;
        _originalPointers = source._originalPointers;
        _effects = (FxEffectDefAsset?[,])source._effects.Clone();
        _pointers = (XPointer<FxEffectDefAsset>[,])source._pointers.Clone();
        _entriesPointer = source._entriesPointer;
        _hasTable = source._hasTable;
    }

    private string? Name { get; }
    public int RowCount => FxImpactTableAsset.EntryCount;

    public FxEffectDefAsset? GetEffect(int row, int slot)
    {
        RequireCell(row, slot);
        return _effects[row, slot];
    }

    public bool IsAssigned(int row, int slot)
    {
        RequireCell(row, slot);
        return _effects[row, slot] is not null || _pointers[row, slot].Raw != 0;
    }

    public bool SetEffect(int row, int slot, FxEffectDefAsset? effect)
    {
        RequireCell(row, slot);
        if (effect is not null)
            _ = AssetKey.FromDefinition(effect);
        if (SameProvider(_effects[row, slot], effect) &&
            (effect is not null || _pointers[row, slot].Raw == 0))
            return false;

        if (effect is null)
        {
            _effects[row, slot] = null;
            _pointers[row, slot] = default;
            if (!_originalHasTable && !HasAssignedCell())
            {
                _hasTable = false;
                _entriesPointer = _originalEntriesPointer;
            }
            return true;
        }

        if (!_hasTable)
        {
            _hasTable = true;
            _entriesPointer = new XPointer<FxImpactEntry[]>(-1, XPointerResolutionMode.Direct);
        }
        if (SameProvider(_originalEffects[row, slot], effect))
        {
            _effects[row, slot] = _originalEffects[row, slot];
            _pointers[row, slot] = _originalPointers[row, slot];
        }
        else
        {
            _effects[row, slot] = effect;
            _pointers[row, slot] = new XPointer<FxEffectDefAsset>(-1, XPointerResolutionMode.AliasCell);
        }
        return true;
    }

    public ImpactFxDraft Clone() => new(this);

    public bool Matches(ImpactFxDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Name != other.Name || _hasTable != other._hasTable ||
            _entriesPointer != other._entriesPointer)
            return false;
        for (int row = 0; row < RowCount; row++)
            for (int slot = 0; slot < SlotCount; slot++)
                if (!SameCell(this, other, row, slot))
                    return false;
        return true;
    }

    /// <summary>Applies changed cells, rejecting stale edits to those cells.</summary>
    public void ApplyEdits(ImpactFxDraft before, ImpactFxDraft after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (Name != before.Name || Name != after.Name)
            throw new InvalidOperationException("An ImpactFx edit cannot change its asset identity.");
        for (int row = 0; row < RowCount; row++)
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (SameCell(before, after, row, slot))
                    continue;
                if (!SameCell(this, before, row, slot))
                    throw new InvalidOperationException(
                        $"ImpactFx row {row}, slot {slot} changed since the edit began.");
            }
        for (int row = 0; row < RowCount; row++)
            for (int slot = 0; slot < SlotCount; slot++)
                if (!SameCell(before, after, row, slot))
                    SetEffect(row, slot, after._effects[row, slot]);
    }

    internal FxImpactTableAsset ToAsset()
    {
        var entries = new FxImpactEntry[_hasTable ? RowCount : 0];
        for (int row = 0; row < entries.Length; row++)
        {
            var surfaces = new FxEffectDefAsset?[FxImpactEntry.SurfaceEffectCount];
            var surfacePointers = new XPointer<FxEffectDefAsset>[surfaces.Length];
            var flesh = new FxEffectDefAsset?[FxImpactEntry.FleshEffectCount];
            var fleshPointers = new XPointer<FxEffectDefAsset>[flesh.Length];
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (slot < surfaces.Length)
                {
                    surfaces[slot] = _effects[row, slot];
                    surfacePointers[slot] = _pointers[row, slot];
                }
                else
                {
                    int fleshSlot = slot - surfaces.Length;
                    flesh[fleshSlot] = _effects[row, slot];
                    fleshPointers[fleshSlot] = _pointers[row, slot];
                }
            }
            entries[row] = new FxImpactEntry
            {
                Offset = _entryOffsets[row],
                SurfaceEffects = surfaces,
                SurfaceEffectPointers = surfacePointers,
                FleshEffects = flesh,
                FleshEffectPointers = fleshPointers
            };
        }
        return new FxImpactTableAsset
        {
            Offset = _offset,
            RuntimeAddress = _runtimeAddress,
            NamePointer = _namePointer,
            Name = Name,
            EntriesPointer = _entriesPointer,
            Entries = entries
        };
    }

    private static bool SameCell(ImpactFxDraft left, ImpactFxDraft right, int row, int slot) =>
        left._pointers[row, slot] == right._pointers[row, slot] &&
        SameProvider(left._effects[row, slot], right._effects[row, slot]);

    private static bool SameProvider(FxEffectDefAsset? left, FxEffectDefAsset? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        AssetKey.FromDefinition(left) == AssetKey.FromDefinition(right);

    private bool HasAssignedCell()
    {
        for (int row = 0; row < RowCount; row++)
            for (int slot = 0; slot < SlotCount; slot++)
                if (_effects[row, slot] is not null || _pointers[row, slot].Raw != 0)
                    return true;
        return false;
    }

    private static void RequireCell(int row, int slot)
    {
        if ((uint)row >= FxImpactTableAsset.EntryCount)
            throw new ArgumentOutOfRangeException(nameof(row));
        if ((uint)slot >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
    }
}

internal sealed class ImpactFxAdapter : AssetAuthoringAdapter<FxImpactTableAsset, ImpactFxDraft>
{
    public override XAssetType AssetType => XAssetType.ImpactFx;
    public override ImpactFxDraft CreateDraft(FxImpactTableAsset asset) => new(asset);
    public override ImpactFxDraft CloneDraft(ImpactFxDraft draft) => draft.Clone();
    public override FxImpactTableAsset CreateDefinition(ImpactFxDraft draft) => draft.ToAsset();
    public override bool SemanticallyEquals(ImpactFxDraft left, ImpactFxDraft right) => left.Matches(right);
}

public sealed partial class AssetEditorSession
{
    /// <summary>Resolves a named Fx provider with full content for an ImpactFx cell.</summary>
    public bool TryResolveImpactEffect(string name, out FxEffectDefAsset? effect)
    {
        ThrowIfClosed();
        return _session.TryResolveImpactEffect(name, out effect);
    }
}

public sealed partial class FastFileEditingSession
{
    internal bool TryResolveImpactEffect(string name, out FxEffectDefAsset? effect)
    {
        effect = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;
        AssetKey key;
        try
        {
            key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(XAssetType.Fx), name);
        }
        catch (ArgumentException)
        {
            return false;
        }

        lock (_gate)
        {
            ThrowIfDisposedCore();
            WorkspaceAssetCatalogEntry? owned = Document.Rows.FirstOrDefault(entry =>
                entry.AssetType == XAssetType.Fx &&
                entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
                entry.NormalizedName == key.NormalizedName);
            if (owned is not null && CurrentDefinitionForEntry(owned) is FxEffectDefAsset current)
            {
                effect = current;
                return true;
            }

            effect = Workspace.AssetCatalog.Entries
                .Where(entry => entry.AssetType == XAssetType.Fx &&
                    entry.NormalizedName == key.NormalizedName &&
                    entry.ContentSource == WorkspaceAssetContentSource.ResolvedProvider)
                .Select(entry => entry.Definition)
                .OfType<FxEffectDefAsset>()
                .FirstOrDefault();
            return effect is not null;
        }
    }
}
