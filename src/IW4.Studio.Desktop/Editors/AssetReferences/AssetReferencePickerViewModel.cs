using Avalonia.Media.Imaging;
using Avalonia.Threading;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;
using IW4.Studio.Documents.AssetReferences;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.AssetReferences;

public sealed class AssetReferenceCandidateViewModel
    : ObservableObject,
      IDisposable
{
    private readonly AssetReferenceMaterialPreviewLoader?
        _materialPreviewLoader;
    private Bitmap? _materialPreview;
    private string _materialPreviewMessage =
        "Material preview will load when this candidate is visible.";
    private Task? _materialPreviewTask;
    private bool _disposed;

    internal AssetReferenceCandidateViewModel(
        string name,
        string origin,
        string? providerZone,
        bool isResolved,
        bool isEditableTarget,
        bool isCurrent,
        AssetReferenceMaterialPreviewLoader? materialPreviewLoader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        Name = name;
        Origin = origin;
        ProviderZone = providerZone ?? string.Empty;
        IsResolved = isResolved;
        IsEditableTarget = isEditableTarget;
        IsCurrent = isCurrent;
        _materialPreviewLoader = materialPreviewLoader;
    }

    public string Name { get; }

    public string Origin { get; }

    public string ProviderZone { get; }

    public bool HasProviderZone => !string.IsNullOrWhiteSpace(ProviderZone);

    public bool IsResolved { get; }

    public bool IsEditableTarget { get; }

    public bool IsCurrent { get; }

    public string AccessText => IsResolved ? "RESOLVED" : "UNRESOLVED";

    public string CurrentText => IsCurrent ? "CURRENT" : string.Empty;

    public bool ShowsMaterialPreview => _materialPreviewLoader is not null;

    public Bitmap? MaterialPreview
    {
        get => _materialPreview;
        private set
        {
            if (ReferenceEquals(_materialPreview, value))
                return;

            Bitmap? previous = _materialPreview;
            if (!SetProperty(ref _materialPreview, value))
                return;

            previous?.Dispose();
            OnPropertyChanged(nameof(HasMaterialPreview));
        }
    }

    public bool HasMaterialPreview => MaterialPreview is not null;

    public string MaterialPreviewMessage
    {
        get => _materialPreviewMessage;
        private set => SetProperty(ref _materialPreviewMessage, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        MaterialPreview = null;
    }

    internal void EnsureMaterialPreview()
    {
        if (_disposed ||
            _materialPreviewLoader is null ||
            _materialPreviewTask is not null)
        {
            return;
        }

        _materialPreviewTask = LoadMaterialPreviewAsync(
            _materialPreviewLoader);
    }

    private async Task LoadMaterialPreviewAsync(
        AssetReferenceMaterialPreviewLoader loader)
    {
        AssetReferenceMaterialPreviewLoadResult result;
        try
        {
            result = await loader.LoadAsync(Name);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = AssetReferenceMaterialPreviewLoadResult.Failed(
                $"Material preview could not be loaded: {exception.Message}");
        }

        if (_disposed || result.IsCanceled)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed)
                return;

            MaterialPreviewMessage = result.Message;
            if (result.PngBytes is null)
                return;

            try
            {
                using var stream = new MemoryStream(
                    result.PngBytes,
                    writable: false);
                MaterialPreview = new Bitmap(stream);
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                MaterialPreviewMessage =
                    $"Material preview could not be displayed: " +
                    exception.Message;
            }
        });
    }
}

/// <summary>
/// Searchable scalar projection of one asset-reference candidate set. The
/// current unresolved spelling is inserted as a synthetic row so opening the
/// picker can never erase or normalize it implicitly.
/// </summary>
public sealed class AssetReferencePickerViewModel
    : ObservableObject,
      IDisposable
{
    private readonly AssetReferenceCandidateViewModel[] _allCandidates;
    private readonly AssetReferenceMaterialPreviewLoader?
        _materialPreviewLoader;
    private string _searchText = string.Empty;
    private IReadOnlyList<AssetReferenceCandidateViewModel> _candidates = [];
    private AssetReferenceCandidateViewModel? _selectedCandidate;
    private bool _disposed;

    public AssetReferencePickerViewModel(
        WorkspaceAssetReferenceCatalog catalog,
        XAssetType assetType,
        string? currentName,
        IMenuPreviewMaterialResolver? materialResolver = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (assetType is not (
                XAssetType.Material or
                XAssetType.Sound or
                XAssetType.Menu or
                XAssetType.PhysPreset or
                XAssetType.PhysCollmap))
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetType),
                assetType,
                "The asset-reference picker does not support this asset type.");
        }

        AssetType = assetType;
        _materialPreviewLoader =
            assetType == XAssetType.Material && materialResolver is not null
                ? new AssetReferenceMaterialPreviewLoader(materialResolver)
                : null;
        CurrentName = LogicalName(currentName);
        WorkspaceAssetReferenceCandidate? current = catalog.Find(
            assetType,
            CurrentName);

        var candidates = catalog.Capture(assetType)
            .Select(candidate => Candidate(
                candidate,
                current is not null &&
                string.Equals(
                    candidate.NormalizedName,
                    current.NormalizedName,
                    StringComparison.Ordinal)))
            .ToList();
        if (CurrentName is { Length: > 0 } unresolved && current is null)
        {
            candidates.Insert(
                0,
                new AssetReferenceCandidateViewModel(
                    unresolved,
                    "Current unresolved value",
                    providerZone: null,
                    isResolved: false,
                    isEditableTarget: false,
                    isCurrent: true,
                    materialPreviewLoader: _materialPreviewLoader));
        }

        _allCandidates = candidates.ToArray();
        RefreshFilter(selectCurrent: true);
    }

    public XAssetType AssetType { get; }

    public string AssetTypeText => AssetType.ToString();

    public string? CurrentName { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _searchText, value))
                return;

            RefreshFilter(selectCurrent: false);
        }
    }

    public IReadOnlyList<AssetReferenceCandidateViewModel> Candidates
    {
        get => _candidates;
        private set
        {
            if (!SetProperty(ref _candidates, value))
                return;

            OnPropertyChanged(nameof(HasCandidates));
            OnPropertyChanged(nameof(EmptyMessage));
        }
    }

    public bool HasCandidates => Candidates.Count != 0;

    public string EmptyMessage => string.IsNullOrWhiteSpace(SearchText)
        ? $"No {AssetTypeText} assets are available in the workspace."
        : $"No {AssetTypeText} assets match “{SearchText}”.";

    public AssetReferenceCandidateViewModel? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (!SetProperty(ref _selectedCandidate, value))
                return;

            OnPropertyChanged(nameof(CanSelect));
        }
    }

    public bool CanSelect => SelectedCandidate is not null;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _materialPreviewLoader?.Dispose();
        foreach (AssetReferenceCandidateViewModel candidate in _allCandidates)
            candidate.Dispose();
    }

    private void RefreshFilter(bool selectCurrent)
    {
        string search = SearchText.Trim();
        string? selectedName = SelectedCandidate?.Name;
        AssetReferenceCandidateViewModel[] filtered = _allCandidates
            .Where(candidate => Matches(candidate, search))
            .ToArray();
        Candidates = Array.AsReadOnly(filtered);
        SelectedCandidate = selectCurrent
            ? filtered.FirstOrDefault(candidate => candidate.IsCurrent)
                ?? filtered.FirstOrDefault()
            : filtered.FirstOrDefault(candidate => string.Equals(
                    candidate.Name,
                    selectedName,
                    StringComparison.Ordinal))
                ?? filtered.FirstOrDefault();
    }

    private static bool Matches(
        AssetReferenceCandidateViewModel candidate,
        string search)
    {
        if (search.Length == 0)
            return true;

        return candidate.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            candidate.Origin.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            candidate.ProviderZone.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private AssetReferenceCandidateViewModel Candidate(
        WorkspaceAssetReferenceCandidate value,
        bool isCurrent) =>
        new(
            value.Name,
            Origin(value.Origin),
            value.ProviderZone,
            value.IsResolved,
            value.IsEditableTarget,
            isCurrent,
            _materialPreviewLoader);

    private static string Origin(WorkspaceAssetOrigin value) => value switch
    {
        WorkspaceAssetOrigin.TargetOwnedDefinition => "Target definition",
        WorkspaceAssetOrigin.TargetResolvedReference => "Target reference",
        WorkspaceAssetOrigin.TargetUnresolvedReference => "Target unresolved reference",
        WorkspaceAssetOrigin.DependencyOnly => "Dependency provider",
        WorkspaceAssetOrigin.NullRow => "Target null row",
        WorkspaceAssetOrigin.OpaqueRow => "Target opaque row",
        _ => value.ToString()
    };

    private static string? LogicalName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.StartsWith(",", StringComparison.Ordinal)
            ? value[1..]
            : value;
    }
}
