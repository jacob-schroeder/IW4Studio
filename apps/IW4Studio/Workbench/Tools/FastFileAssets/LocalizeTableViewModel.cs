using System.Text.RegularExpressions;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

/// <summary>Localize values and pending edits for one fastfile navigator.</summary>
public sealed class LocalizeTableViewModel : ObservableObject
{
    private readonly FastFileEditingSession _session;
    private readonly Dictionary<TargetZoneRowIdentity, LocalizeTableRowViewModel> _ownedRows = [];
    private readonly Dictionary<TargetZoneRowIdentity, WorkspaceAssetCatalogEntry> _ownedEntries = [];
    private LocalizeTableRowViewModel? _expanded;
    private int _pendingCount;

    public LocalizeTableViewModel(FastFileEditingSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public int PendingCount
    {
        get => _pendingCount;
        private set
        {
            if (SetProperty(ref _pendingCount, value))
                OnPropertyChanged(nameof(HasPendingChanges));
        }
    }

    public bool HasPendingChanges => PendingCount != 0;

    public bool HasPendingEdit(TargetZoneRowIdentity identity) =>
        _ownedRows.TryGetValue(identity, out LocalizeTableRowViewModel? row) &&
        row.HasPendingChange;

    public bool HasPendingReplacement(IReadOnlySet<AssetKey> replacing) =>
        _ownedRows.Any(pair => pair.Value.HasPendingChange &&
            _ownedEntries[pair.Key].NormalizedName is { } name &&
            replacing.Contains(AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(XAssetType.Localize), name)));

    /// <summary>Attach values before the Contents list binds or searches these rows.</summary>
    public void SetRows(IReadOnlyList<FastFileContentsRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        IReadOnlyDictionary<long, string?> poolValues = rows.Any(row =>
            row.AssetType == XAssetType.Localize && row.PoolSlot is not null)
                ? _session.CaptureCurrentPoolLocalizeValues()
                : new Dictionary<long, string?>();
        foreach (FastFileContentsRow row in rows)
        {
            if (row.AssetType != XAssetType.Localize)
            {
                row.Localize = null;
                continue;
            }

            AssetExplorerEntryViewModel? catalog = row.CatalogEntry;
            string? value = null;
            bool readPhysicalSlot = row.PoolSlot is { CanEdit: false };
            bool available = !readPhysicalSlot && catalog is not null &&
                _session.TryGetCurrentLocalizeValue(catalog.Entry, out value);
            if (!available && row.PoolSlot is { } slot)
                available = poolValues.TryGetValue(slot.ActiveProviderId.Value, out value);
            string? currentValue = available ? value : null;
            TargetZoneRowIdentity? ownedIdentity = catalog?.Identity.TargetRowIdentity;
            bool editable = catalog is not null && ownedIdentity is not null &&
                catalog.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
                catalog.Access == WorkspaceAssetAccess.Editable &&
                !row.IsReferencePlaceholder &&
                (row.PoolSlot is null ||
                 row.PoolSlot.CanEdit &&
                 catalog.Entry.ResolvedProvider?.ProviderId == row.PoolSlot.ActiveProviderId.Value);

            if (editable && catalog is { } ownedCatalog && ownedIdentity is { } identity &&
                _ownedRows.TryGetValue(identity, out LocalizeTableRowViewModel? existing))
            {
                existing.Refresh(row.Name, row.SourceFile, currentValue, available);
                _ownedEntries[identity] = ownedCatalog.Entry;
                row.Localize = existing;
                continue;
            }

            var localize = new LocalizeTableRowViewModel(
                row.Name, row.SourceFile,
                row.PoolSlot is { } providerSlot
                    ? providerSlot.ActiveProviderId.Value
                    : catalog?.Identity,
                currentValue, available, editable,
                editable ? ownedIdentity : null, delta => PendingCount += delta);
            if (_expanded is { IsEditable: false } expanded &&
                Equals(expanded.Identity, localize.Identity))
            {
                expanded.IsExpanded = false;
                _expanded = localize;
                localize.IsExpanded = true;
            }
            if (editable && catalog is { } newCatalog && ownedIdentity is { } newIdentity)
            {
                _ownedRows.Add(newIdentity, localize);
                _ownedEntries.Add(newIdentity, newCatalog.Entry);
            }
            row.Localize = localize;
        }
    }

    public bool MatchesSearch(FastFileContentsRow row, string query) =>
        row.Localize is { } localize &&
        (localize.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
         localize.ValueInput.Contains(query, StringComparison.OrdinalIgnoreCase));

    public void Expand(LocalizeTableRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (ReferenceEquals(_expanded, row))
            return;
        if (_expanded is not null)
            _expanded.IsExpanded = false;
        _expanded = row;
        row.IsExpanded = true;
    }

    public void ToggleExpanded(LocalizeTableRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (ReferenceEquals(_expanded, row))
        {
            row.IsExpanded = false;
            _expanded = null;
        }
        else
            Expand(row);
    }

    public int ApplyAll()
    {
        LocalizeValueEdit[] edits = _ownedRows.Values
            .Where(row => row.HasPendingChange)
            .Select(row => row.CreateEdit())
            .ToArray();
        if (edits.Length == 0)
            return 0;

        int changed = _session.ApplyLocalizeValueEdits(edits);
        foreach (LocalizeTableRowViewModel row in _ownedRows.Values)
            if (row.HasPendingChange)
                row.AcceptAppliedValue();
        return changed;
    }

    public void DiscardAll()
    {
        foreach ((TargetZoneRowIdentity identity, LocalizeTableRowViewModel row) in _ownedRows)
        {
            if (!row.HasPendingChange)
                continue;
            bool available = _session.TryGetCurrentLocalizeValue(
                _ownedEntries[identity], out string? value);
            row.Refresh(row.Key, row.SourceFile, value, available);
            row.Undo();
        }
    }
}

/// <summary>One displayed Localize key and value; only owned rows hold editable state.</summary>
public sealed class LocalizeTableRowViewModel : ObservableObject
{
    private static readonly Regex PlaceholderPattern = new(@"&&[0-9]+", RegexOptions.Compiled);
    private readonly TargetZoneRowIdentity? _ownedIdentity;
    private readonly Action<int> _pendingChanged;
    private readonly bool _canEdit;
    private string? _appliedValue;
    private string? _observedValue;
    private string _valueInput;
    private bool _available;
    private bool _expanded;
    private bool _inlineEligible;
    private bool _wasPending;

    internal LocalizeTableRowViewModel(
        string key, string sourceFile, object? identity,
        string? value, bool available, bool editable,
        TargetZoneRowIdentity? ownedIdentity, Action<int> pendingChanged)
    {
        Key = key;
        SourceFile = sourceFile;
        Identity = identity;
        _appliedValue = value;
        _observedValue = value;
        _valueInput = value ?? string.Empty;
        _available = available;
        _canEdit = editable;
        _ownedIdentity = ownedIdentity;
        _pendingChanged = pendingChanged;
        _inlineEligible = IsShort(value);
    }

    public string Key { get; private set; }
    public string SourceFile { get; private set; }
    internal object? Identity { get; }
    public bool IsEditable => _canEdit && _available;
    public bool IsContentAvailable => _available;
    public bool HasValue => _available && _appliedValue is not null;
    public bool IsLongValue => !IsShort(ValueInput);
    public bool HasPendingChange => _canEdit &&
        !string.Equals(ValueInput, _appliedValue ?? string.Empty, StringComparison.Ordinal);
    public bool HasExternalChange => HasPendingChange &&
        !string.Equals(_appliedValue, _observedValue, StringComparison.Ordinal);
    public bool ShowInlineInput => IsEditable && !IsExpanded && _inlineEligible;
    public bool ShowPreview => !ShowInlineInput;
    public bool IsExpanded
    {
        get => _expanded;
        internal set
        {
            if (!SetProperty(ref _expanded, value))
                return;
            _inlineEligible = IsShort(ValueInput);
            OnPropertyChanged(nameof(ShowInlineInput));
            OnPropertyChanged(nameof(ShowPreview));
        }
    }

    public string ValueInput
    {
        get => _valueInput;
        set
        {
            if (!IsEditable)
                return;
            value ??= string.Empty;
            if (!SetProperty(ref _valueInput, value))
                return;
            NotifyValueChanged();
        }
    }

    public string ValuePreview
    {
        get
        {
            if (!_available)
                return "Content unavailable";
            if (!HasValue && !HasPendingChange)
                return "NULL";
            int firstBreak = ValueInput.IndexOfAny(['\r', '\n']);
            string firstLine = firstBreak < 0 ? ValueInput : ValueInput[..firstBreak];
            return firstLine.Length > 70 ? firstLine[..70] + "…" : firstLine;
        }
    }

    public string LineCountText
    {
        get
        {
            if (!IsLongValue)
                return string.Empty;
            int lines = 1;
            for (int index = 0; index < ValueInput.Length; index++)
            {
                if (ValueInput[index] == '\r')
                {
                    lines++;
                    if (index + 1 < ValueInput.Length && ValueInput[index + 1] == '\n')
                        index++;
                }
                else if (ValueInput[index] == '\n')
                    lines++;
            }
            return $"{lines} {(lines == 1 ? "line" : "lines")}";
        }
    }

    public string PlaceholderText
    {
        get
        {
            string[] expected = PlaceholderTokens(_appliedValue ?? string.Empty).ToArray();
            string[] current = PlaceholderTokens(ValueInput).ToArray();
            if (expected.Length == 0 && current.Length == 0)
                return string.Empty;
            var currentCounts = current.GroupBy(token => token)
                .ToDictionary(group => group.Key, group => group.Count());
            var expectedCounts = expected.GroupBy(token => token)
                .ToDictionary(group => group.Key, group => group.Count());
            IEnumerable<string> missing = expectedCounts.Select(pair =>
                currentCounts.GetValueOrDefault(pair.Key) < pair.Value
                    ? $"{pair.Key} missing"
                    : pair.Key);
            IEnumerable<string> added = currentCounts
                .Where(pair => !expectedCounts.ContainsKey(pair.Key))
                .Select(pair => $"{pair.Key} added");
            return string.Join(", ", missing.Concat(added));
        }
    }
    public bool HasPlaceholderText => PlaceholderText.Length != 0;
    public bool HasPlaceholderWarning => HasPendingChange &&
        !PlaceholderTokens(_appliedValue ?? string.Empty)
            .SequenceEqual(PlaceholderTokens(ValueInput), StringComparer.Ordinal);

    public void Undo()
    {
        if (!HasPendingChange)
            return;
        _appliedValue = _observedValue;
        _valueInput = _appliedValue ?? string.Empty;
        _inlineEligible = IsShort(ValueInput);
        OnPropertyChanged(nameof(ValueInput));
        OnPropertyChanged(nameof(HasValue));
        NotifyValueChanged();
    }

    internal void Refresh(string key, string sourceFile, string? value, bool available)
    {
        if (Key != key)
        {
            Key = key;
            OnPropertyChanged(nameof(Key));
        }
        if (SourceFile != sourceFile)
        {
            SourceFile = sourceFile;
            OnPropertyChanged(nameof(SourceFile));
        }
        if (HasPendingChange)
        {
            _observedValue = value;
            _available = available;
            OnPropertyChanged(nameof(IsContentAvailable));
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(HasValue));
            OnPropertyChanged(nameof(ValuePreview));
            OnPropertyChanged(nameof(ShowInlineInput));
            OnPropertyChanged(nameof(ShowPreview));
            OnPropertyChanged(nameof(HasExternalChange));
            return;
        }
        _appliedValue = value;
        _observedValue = value;
        _available = available;
        _valueInput = value ?? string.Empty;
        _inlineEligible = IsShort(value);
        OnPropertyChanged(nameof(IsContentAvailable));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(HasValue));
        OnPropertyChanged(nameof(ValueInput));
        NotifyValueChanged();
    }

    internal LocalizeValueEdit CreateEdit() => new(
        _ownedIdentity ?? throw new InvalidOperationException("This Localize row is read-only."),
        _appliedValue, ValueInput);

    internal void AcceptAppliedValue()
    {
        _appliedValue = ValueInput;
        _observedValue = _appliedValue;
        _inlineEligible = IsShort(ValueInput);
        OnPropertyChanged(nameof(HasValue));
        NotifyValueChanged();
    }

    private void NotifyValueChanged()
    {
        OnPropertyChanged(nameof(HasPendingChange));
        OnPropertyChanged(nameof(HasExternalChange));
        OnPropertyChanged(nameof(IsLongValue));
        OnPropertyChanged(nameof(ValuePreview));
        OnPropertyChanged(nameof(LineCountText));
        OnPropertyChanged(nameof(PlaceholderText));
        OnPropertyChanged(nameof(HasPlaceholderText));
        OnPropertyChanged(nameof(HasPlaceholderWarning));
        OnPropertyChanged(nameof(ShowInlineInput));
        OnPropertyChanged(nameof(ShowPreview));
        bool pending = HasPendingChange;
        if (pending != _wasPending)
        {
            _wasPending = pending;
            _pendingChanged(pending ? 1 : -1);
        }
    }

    private static bool IsShort(string? value) =>
        value is null || value.Length <= 70 && value.IndexOfAny(['\r', '\n']) < 0;

    private static IEnumerable<string> PlaceholderTokens(string value) =>
        PlaceholderPattern.Matches(value).Select(match => match.Value)
            .OrderBy(token => token, StringComparer.Ordinal);
}
