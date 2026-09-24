using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Iw4Radiant.Views;

public partial class FxSoundBrowser : UserControl
{
    private IReadOnlyList<FxSoundAsset> _assets = [];
    private EditorDialogs? _dialogs;
    private Action? _finishGestures;
    private Action<string>? _setStatus;
    private FxSoundAsset? _shownAsset;
    private bool _previewing;
    private int _loadRevision;

    public FxSoundBrowser() => InitializeComponent();

    public bool IsSoundBrowser { get; set; }
    internal string? SourceDirectory { get; private set; }
    internal event Action<string, bool>? PlacementRequested;
    internal event Action<FxSoundAsset>? PreviewRequested;
    internal event Action? PreviewStopRequested;
    internal event Action<string>? SourceLoaded;

    internal void ShowReference(string name)
    {
        SearchBox.Text = name;
        FilterAssets();
        FxSoundAsset? match = _assets.FirstOrDefault(asset => asset.IsSound == IsSoundBrowser && asset.Name == name);
        if (match is not null)
        {
            AssetList.SelectedItem = match;
            ShowSelected();
            return;
        }

        AssetName.Text = name;
        AssetDetail.Text = SourceDirectory is null
            ? "Reference preserved. Choose the raw library used to build this map."
            : $"No source file for this reference in {Path.GetFileName(SourceDirectory)}. Choose another library or edit the reference.";
        PreviewButton.IsEnabled = false;
        PreviewStateText.Text = "This reference has no source file in the selected library.";
        ToolTip.SetTip(PreviewStateText, null);
    }

    internal void InitializeActions(Window owner, EditorDialogs dialogs, Action finishGestures,
        Action<string> setStatus)
    {
        _dialogs = dialogs;
        _finishGestures = finishGestures;
        _setStatus = setStatus;
        ToolTip.SetTip(PreviewButton, IsSoundBrowser
            ? "Hear this sound before placing it in the map."
            : "See this effect in the camera before placing it in the map.");
        ChooseSourceButton.Click += async (_, _) => await ChooseSourceAsync(owner);
        SearchBox.TextChanged += (_, _) => FilterAssets();
        AssetList.SelectionChanged += (_, _) => ShowSelected();
        PlaceButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput || SelectedAsset() is not { } asset) return;
            finishGestures();
            PlacementRequested?.Invoke(asset.Name, asset.IsSound);
        };
        PreviewButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            if (_previewing) PreviewStopRequested?.Invoke();
            else if (SelectedAsset() is { } asset) PreviewRequested?.Invoke(asset);
        };
    }

    private async Task ChooseSourceAsync(Window owner)
    {
        if (_dialogs is not { } dialogs || dialogs.BlocksInput) return;
        _finishGestures?.Invoke();
        var folders = await dialogs.ShowModalAsync(() => owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose the raw folder containing fx and soundaliases", AllowMultiple = false }));
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            await LoadDirectoryAsync(path);
    }

    internal async Task<bool> LoadDirectoryAsync(string path, bool nonBlocking = false)
    {
        if (_dialogs is not { } dialogs) return false;
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        int revision = ++_loadRevision;
        try
        {
            if (!nonBlocking) dialogs.SetBusy(true);
            _setStatus?.Invoke($"Reading on-disk FX and sounds from {Path.GetFileName(path)}…");
            IReadOnlyList<FxSoundAsset> assets = await FxSoundAssetCatalog.ReadAsync(path, CancellationToken.None);
            if (revision != _loadRevision) return false;
            PreviewStopRequested?.Invoke();
            _assets = assets;
            SourceDirectory = path;
            SourceText.Text = Path.GetFileName(path);
            ToolTip.SetTip(SourceText, SourceDirectory);
            FilterAssets();
            SourceLoaded?.Invoke(SourceDirectory);
            _setStatus?.Invoke($"Loaded {assets.Count} FX and sounds from {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            if (nonBlocking) _setStatus?.Invoke($"Cannot read FX and sounds: {exception.Message}");
            else
            {
                dialogs.SetBusy(false);
                await dialogs.MessageAsync("Cannot browse FX and sounds", exception.Message);
            }
            return false;
        }
        finally { if (!nonBlocking) dialogs.SetBusy(false); }
    }

    private void FilterAssets()
    {
        string query = SearchBox.Text?.Trim() ?? "";
        var matches = _assets.Where(asset => asset.IsSound == IsSoundBrowser &&
                                             (asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                              asset.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                              asset.Category.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        AssetList.ItemsSource = IsSoundBrowser
            ? matches.OrderByDescending(asset => asset.IsKnownLoop)
                .ThenBy(asset => asset.DisplayName, StringComparer.Ordinal).ToArray()
            : matches;
        AssetList.SelectedItem = null;
        ResultText.Text = SourceDirectory is null ? "Use the same raw library as Build PS3 map." :
            $"{matches.Length} matching on-disk {(IsSoundBrowser ? "sounds" : "effects")}";
        ShowSelected();
    }

    private FxSoundAsset? SelectedAsset() => AssetList.SelectedItem as FxSoundAsset;

    private void ShowSelected()
    {
        FxSoundAsset? asset = SelectedAsset();
        if (!Equals(asset, _shownAsset))
        {
            if (_previewing) PreviewStopRequested?.Invoke();
            _previewing = false;
            _shownAsset = asset;
            PreviewStateText.Text = asset is null ? "Select an asset to preview." :
                asset.IsSound ? "Listen before placing." : "Preview in the camera view before placing.";
            ToolTip.SetTip(PreviewStateText, null);
        }
        PreviewButton.IsEnabled = asset is not null && SourceDirectory is not null;
        PreviewButton.Content = _previewing ? "Stop preview" : IsSoundBrowser ? "Listen" : "Preview FX";
        PlaceButton.IsEnabled = asset is not null && (!asset.IsSound || asset.IsKnownLoop);
        AssetName.Text = asset?.Name ?? "Select an asset";
        AssetDetail.Text = asset is null ? "Exact asset name appears here." :
            (asset.IsSound ? "Sound" : $"{asset.Category} · FX") +
            (asset.IsSound ? asset.IsKnownLoop
                ? " · Used as a loop in PS3 map scripts"
                : " · Loop use unverified; placement unavailable" : "");
    }

    internal void SetPreviewState(bool playing, string message, string? detail = null)
    {
        _previewing = playing;
        PreviewStateText.Text = message;
        ToolTip.SetTip(PreviewStateText, detail);
        PreviewButton.Content = playing ? "Stop preview" : IsSoundBrowser ? "Listen" : "Preview FX";
    }
}
