using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.PackFilePak;

/// <summary>
/// Searchable, read-only view over packed streamed-sound rows referenced by
/// the Sound definitions loaded into the current workspace.
/// </summary>
public sealed class PackFilePakToolViewModel : ObservableObject, IDisposable
{
    private readonly IWorkbenchSelectionContext _selectionContext;
    private readonly IReadOnlyList<PackFilePakEntryViewModel> _allEntries;
    private readonly IReadOnlyDictionary<
        WorkbenchStreamedSoundIdentity,
        PackFilePakEntryViewModel> _entriesByIdentity;
    private IReadOnlyList<PackFilePakEntryViewModel> _visibleEntries;
    private PackFilePakEntryViewModel? _selectedEntry;
    private string _searchText = string.Empty;
    private bool _disposed;

    public PackFilePakToolViewModel(
        FastFileWorkspace workspace,
        IWorkbenchSelectionContext selectionContext)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _selectionContext = selectionContext
            ?? throw new ArgumentNullException(nameof(selectionContext));
        _allEntries = CaptureEntries(workspace);
        _entriesByIdentity = _allEntries.ToDictionary(entry => entry.Identity);
        _visibleEntries = _allEntries;
        _selectionContext.SelectionChanged += SelectionContext_SelectionChanged;
    }

    public IReadOnlyList<PackFilePakEntryViewModel> VisibleEntries
    {
        get => _visibleEntries;
        private set => SetProperty(ref _visibleEntries, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _searchText, value))
                return;

            RebuildProjection();
        }
    }

    public PackFilePakEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
                return;

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasNoSelection));
            if (value is not null)
                _selectionContext.Select(value.ToSelection());
        }
    }

    public int TotalCount => _allEntries.Count;

    public int VisibleCount => VisibleEntries.Count;

    public bool HasEntries => VisibleCount != 0;

    public bool HasSelection => SelectedEntry is not null;

    public bool HasNoSelection => !HasSelection;

    public string ResultText => string.IsNullOrWhiteSpace(SearchText)
        ? $"{TotalCount:N0} packed sounds"
        : $"{VisibleCount:N0} of {TotalCount:N0}";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _selectionContext.SelectionChanged -= SelectionContext_SelectionChanged;
    }

    internal PackFilePakEntryViewModel RequireEntry(
        WorkbenchStreamedSoundIdentity identity) =>
        _entriesByIdentity.TryGetValue(
            identity,
            out PackFilePakEntryViewModel? entry)
                ? entry
                : throw new KeyNotFoundException(
                    "The packed sound is not part of this workspace.");

    private static IReadOnlyList<PackFilePakEntryViewModel> CaptureEntries(
        FastFileWorkspace workspace)
    {
        var entries = new List<PackFilePakEntryViewModel>();
        if (workspace.LoadedZones.Count == 0)
            return Array.AsReadOnly(entries.ToArray());

        var zonesByOwner = workspace.LoadedZones
            .Where(zone => !zone.LoadResult.Context.ZoneOwner.IsNone)
            .ToDictionary(
                zone => zone.LoadResult.Context.ZoneOwner,
                zone => zone);
        var capturedProviderIds = new HashSet<long>();
        XAssetProviderContribution[] soundProviders = workspace.LoadedZones[0]
            .LoadResult.Context.AssetPool.Slots
            .Where(slot => slot.AssetType == XAssetType.Sound)
            .SelectMany(slot => slot.Providers)
            .Where(provider =>
                !provider.IsReferencePlaceholder &&
                provider.Asset is SoundAliasListAsset &&
                zonesByOwner.ContainsKey(provider.Owner))
            .OrderBy(provider => provider.RegistrationSequence)
            .ToArray();

        foreach (XAssetProviderContribution provider in soundProviders)
        {
            if (!capturedProviderIds.Add(provider.Id.Value))
                continue;

            WorkspaceZone zone = zonesByOwner[provider.Owner];
            var sound = (SoundAliasListAsset)provider.Asset;

            for (int aliasIndex = 0;
                 aliasIndex < sound.Aliases.Count;
                 aliasIndex++)
            {
                SndAlias alias = sound.Aliases[aliasIndex];
                for (int fileIndex = 0;
                     fileIndex < alias.SoundFiles.Count;
                     fileIndex++)
                {
                    SoundFile file = alias.SoundFiles[fileIndex];
                    if (file.Exists == 0 ||
                        file.Streamed is not
                        {
                            FileIndex: > 0,
                            StreamFile: { StreamFileLength: > 0 }
                        })
                    {
                        continue;
                    }

                    entries.Add(new PackFilePakEntryViewModel(
                        sound,
                        zone.LoadResult.SoundPayloadResolver,
                        zone.PhysicalPath,
                        aliasIndex,
                        fileIndex,
                        new WorkbenchStreamedSoundIdentity(
                            workspace.Document.DocumentId,
                            entries.Count)));
                }
            }
        }

        return Array.AsReadOnly(entries.ToArray());
    }

    private void RebuildProjection()
    {
        string query = SearchText.Trim();
        PackFilePakEntryViewModel[] visible = query.Length == 0
            ? _allEntries.ToArray()
            : _allEntries
                .Where(entry => string.Join(
                        ' ',
                        entry.Name,
                        entry.AliasName,
                        entry.ChoiceText,
                        entry.OwningFastFileName,
                        entry.PackageName,
                        entry.RangeText)
                    .Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        bool selectedEntryIsHidden =
            SelectedEntry is not null &&
            !visible.Contains(SelectedEntry);
        VisibleEntries = Array.AsReadOnly(visible);
        if (selectedEntryIsHidden)
        {
            _selectionContext.Clear(
                WorkbenchAssetSelectionSource.PackFilePak);
        }

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ResultText));
    }

    private void SelectionContext_SelectionChanged(
        object? sender,
        WorkbenchSelectionChangedEventArgs args)
    {
        PackFilePakEntryViewModel? desired =
            args.Current?.Identity.StreamedSoundIdentity is { } identity &&
            _entriesByIdentity.TryGetValue(
                identity,
                out PackFilePakEntryViewModel? entry)
                ? entry
                : null;
        if (ReferenceEquals(_selectedEntry, desired))
            return;

        _selectedEntry = desired;
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
    }
}
