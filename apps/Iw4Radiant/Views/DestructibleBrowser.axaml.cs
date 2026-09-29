using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Views;

public partial class DestructibleBrowser : UserControl
{
    private XModelBrowser? _models;
    private readonly List<DestructibleThumbnail> _thumbnails = [];
    private readonly Dictionary<string, DestructiblePreset> _selectedVariants = new(StringComparer.Ordinal);
    private int _previewRevision;
    private CancellationTokenSource? _preparationCancellation;
    private bool _checkingAssets, _updatingVariants;

    public DestructibleBrowser() => InitializeComponent();

    internal event Action<DestructiblePreset>? PlacementRequested;

    internal void InitializeActions(Window owner, XModelBrowser models, EditorDialogs dialogs, Action finishGestures)
    {
        _models = models;
        models.CatalogReset += ReleaseImages;
        models.CatalogChanged += () => _ = RefreshAsync();
        PresetList.SelectionChanged += (_, _) => UpdateSelection();
        CategoryBox.ItemsSource = new[] { "All categories" }.Concat(DestructiblePresets.All.Select(preset => preset.Category).Distinct()).ToArray();
        CategoryBox.SelectedIndex = 0;
        CategoryBox.SelectionChanged += (_, _) => FilterPresets();
        PresetFilter.TextChanged += (_, _) => FilterPresets();
        ShowUnavailableBox.IsCheckedChanged += (_, _) => FilterPresets();
        VariantBox.SelectionChanged += (_, _) =>
        {
            if (_updatingVariants || VariantBox.SelectedItem is not DestructibleThumbnail selected) return;
            _selectedVariants[selected.Name] = selected.Preset;
            FilterPresets(selected.Preset);
        };
        BrowseButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures();
            await models.BrowseFolderAsync(owner);
        };
        RefreshButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures();
            if (models.SourceDirectory is { } root) await models.LoadFolderAsync(root);
            else await models.BrowseFolderAsync(owner);
        };
        IssuesButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput || PresetList.SelectedItem is not DestructibleThumbnail selected) return;
            await dialogs.MessageAsync(selected.Preset.Name + " assets", selected.Status +
                "\n\nChoose the library containing these assets, or install their exported sources into the current library and use Refresh assets.");
        };
        PresetList.AddHandler(PointerPressedEvent, async (_, e) =>
        {
            if (dialogs.BlocksInput || !e.GetCurrentPoint(PresetList).Properties.IsLeftButtonPressed) return;
            DestructibleThumbnail? selected = null;
            for (Control? current = e.Source as Control; current is not null && !ReferenceEquals(current, PresetList);
                 current = current.Parent as Control)
                if (current.DataContext is DestructibleThumbnail thumbnail) { selected = thumbnail; break; }
            if (selected is null) return;
            PresetList.SelectedItem = selected;
            if (selected.IsUnavailable) return;
            finishGestures();
            try
            {
                await DragDrop.DoDragDropAsync(e, DestructibleDrag.Create(selected.Preset), DragDropEffects.Copy);
            }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
            { PresetInfo.Text = exception.Message; }
        }, handledEventsToo: true);
        PlaceButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput || PresetList.SelectedItem is not DestructibleThumbnail { IsUnavailable: false } selected)
                return;
            finishGestures();
            PlacementRequested?.Invoke(selected.Preset);
        };
        _ = RefreshAsync();
    }

    internal void ReleaseImages()
    {
        _previewRevision++;
        _preparationCancellation?.Cancel();
        _preparationCancellation = null;
        PresetList.ItemsSource = null;
        foreach (DestructibleThumbnail thumbnail in _thumbnails) thumbnail.Preview?.Dispose();
        _thumbnails.Clear();
        _checkingAssets = false;
        PlaceButton.IsEnabled = false;
        IssuesButton.IsVisible = false;
        VariantRow.IsVisible = false;
        CatalogSummary.Text = null;
        EmptyState.Text = "Choose an asset library to browse destructibles.";
        EmptyState.IsVisible = true;
        PresetInfo.Text = "Load extracted XModels to preview and place a destructible.";
    }

    private async Task RefreshAsync()
    {
        DestructiblePreset? selected = (PresetList.SelectedItem as DestructibleThumbnail)?.Preset;
        ReleaseImages();
        if (_models is not { } models) return;
        int revision = _previewRevision;
        using var cancellation = new CancellationTokenSource();
        _preparationCancellation = cancellation;
        string? root = models.SourceDirectory;
        _checkingAssets = root is not null;
        Task<IReadOnlyDictionary<DestructiblePreset, DestructibleReadiness>> readiness = root is null
            ? Task.FromResult<IReadOnlyDictionary<DestructiblePreset, DestructibleReadiness>>(new Dictionary<DestructiblePreset, DestructibleReadiness>())
            : Task.Run(() => DestructibleAssets.Check(root, DestructiblePresets.All, cancellation.Token), cancellation.Token);
        FilterPresets(selected);
        try
        {
            foreach (DestructiblePreset preset in DestructiblePresets.All)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var loaded = await Task.Run(() =>
                {
                    XModelSource? source = models.ResolveModel(preset.ModelName, out string? error);
                    var previewErrors = new List<string>();
                    foreach (string name in preset.Preview.Stages.Select(stage => stage.ModelName).OfType<string>()
                                 .Where(name => name != preset.ModelName).Distinct(StringComparer.Ordinal))
                        if (models.ResolveModel(name, out string? previewError) is null)
                            previewErrors.Add($"Preview model '{name}' is unavailable: {previewError}");
                    return (Source: source, Error: error, PreviewErrors: previewErrors);
                }, cancellation.Token);
                bool unavailable = loaded.Source is null;
                string status = unavailable ? $"Intact model unavailable: {loaded.Error}" : preset.Description;
                if (loaded.PreviewErrors.Count != 0) status += "\n" + string.Join('\n', loaded.PreviewErrors);
                Bitmap? preview = null;
                if (loaded.Source is { } source)
                {
                    try
                    {
                        preview = await Task.Run(() =>
                        {
                            XModelSource intact = preset.Preview.Parts is { Count: > 0 } parts
                                ? DestructibleModelPreview.Create(source, parts, null) : source;
                            return new XModelPreviewRenderer(models.ResolveMaterial).Render(intact, 144);
                        }, cancellation.Token);
                    }
                    catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                    { status += $" Preview unavailable: {exception.Message}"; }
                }
                if (revision != _previewRevision) { preview?.Dispose(); return; }
                _thumbnails.Add(new(preset, preview, unavailable, status,
                    root is null ? "Choose library" : "Checking assets…", PreviewUnavailable: loaded.PreviewErrors.Count != 0));
                FilterPresets(selected);
            }
            var results = await readiness;
            if (revision != _previewRevision) return;
            for (int index = 0; index < _thumbnails.Count; index++)
            {
                var thumbnail = _thumbnails[index];
                if (!results.TryGetValue(thumbnail.Preset, out var result)) continue;
                _thumbnails[index] = thumbnail with
                {
                    ReadinessLabel = (thumbnail.IsUnavailable || thumbnail.PreviewUnavailable) && result.IsReady
                        ? "Preview model unavailable" : result.Label,
                    Status = result.IsReady ? thumbnail.Status : thumbnail.Status + "\n" + string.Join('\n', result.Issues),
                    HasIssues = !result.IsReady || thumbnail.IsUnavailable || thumbnail.PreviewUnavailable,
                    IsReady = result.IsReady && !thumbnail.IsUnavailable && !thumbnail.PreviewUnavailable
                };
            }
            _checkingAssets = false;
            FilterPresets();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            if (revision == _previewRevision)
            {
                _checkingAssets = false;
                for (int index = 0; index < _thumbnails.Count; index++)
                    _thumbnails[index] = _thumbnails[index] with { ReadinessLabel = "Check unavailable", HasIssues = true,
                        Status = _thumbnails[index].Status + "\n" + exception.Message };
                FilterPresets();
            }
        }
        finally
        {
            // Observe a cancelled/failed check even when thumbnail work exits first.
            try { await readiness; }
            catch (Exception exception) when (exception is OperationCanceledException || FileOperationErrors.IsExpected(exception)) { }
            if (ReferenceEquals(_preparationCancellation, cancellation)) _preparationCancellation = null;
        }
    }

    private void FilterPresets(DestructiblePreset? preferred = null)
    {
        preferred ??= (PresetList.SelectedItem as DestructibleThumbnail)?.Preset;
        string filter = PresetFilter.Text?.Trim() ?? "";
        string? category = CategoryBox.SelectedIndex > 0 ? CategoryBox.SelectedItem as string : null;
        var visible = MatchingThumbnails()
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Select(group => group.FirstOrDefault(item => item.Preset == preferred) ??
                group.FirstOrDefault(item => item.Preset == _selectedVariants.GetValueOrDefault(group.Key)) ??
                group.FirstOrDefault(item => item.IsReady) ?? group.First())
            .ToArray();
        PresetList.ItemsSource = visible;
        PresetList.SelectedItem = visible.FirstOrDefault(item => item.Preset == preferred) ?? visible.FirstOrDefault();
        int ready = _thumbnails.Count(item => item.IsReady);
        CatalogSummary.Text = _checkingAssets ? "Checking preset assets…" :
            $"{visible.Length} families shown · {ready} of {_thumbnails.Count} presets ready";
        EmptyState.IsVisible = visible.Length == 0;
        EmptyState.Text = _checkingAssets ? "Preparing destructible previews…" :
            filter.Length > 0 || category is not null ? "No destructibles match these filters." :
            ShowUnavailableBox.IsChecked == true ? "Choose an asset library to browse destructibles." :
            "No presets are ready. Enable Show unavailable to review the catalog and its asset requirements.";
        UpdateSelection();
    }

    private IEnumerable<DestructibleThumbnail> MatchingThumbnails()
    {
        string filter = PresetFilter.Text?.Trim() ?? "";
        string? category = CategoryBox.SelectedIndex > 0 ? CategoryBox.SelectedItem as string : null;
        return _thumbnails.Where(item =>
            (ShowUnavailableBox.IsChecked == true || item.IsReady) &&
            (category is null || item.Preset.Category == category) &&
            (item.Preset.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
             item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
             item.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateSelection()
    {
        var selected = PresetList.SelectedItem as DestructibleThumbnail;
        _updatingVariants = true;
        try
        {
            var variants = selected is null ? [] : MatchingThumbnails()
                .Where(item => item.Name == selected.Name).ToArray();
            VariantBox.ItemsSource = variants;
            VariantBox.SelectedItem = selected;
            VariantRow.IsVisible = variants.Length > 1;
        }
        finally { _updatingVariants = false; }
        PlaceButton.IsEnabled = selected is { IsUnavailable: false };
        IssuesButton.IsVisible = selected is { HasIssues: true };
        IssuesButton.Content = selected?.ReadinessLabel switch
        {
            "Build unavailable" => "Review build requirements…",
            "Incompatible assets" => "Review incompatible assets…",
            _ => "Review missing assets…"
        };
        string summary = selected?.Status.Split('\n')[0] ?? "Choose a destructible to place in a viewport.";
        PresetInfo.Text = selected is { IsUnavailable: false }
            ? $"{summary} {selected.ReadinessLabel}. Drag onto a camera surface or grid."
            : summary;
    }
}

internal sealed record DestructibleThumbnail(DestructiblePreset Preset, Bitmap? Preview, bool IsUnavailable, string Status,
    string ReadinessLabel, bool HasIssues = false, bool IsReady = false, bool PreviewUnavailable = false)
{
    public string Name => Preset.Family ?? Preset.Name;
    public string VariantLabel => $"{Preset.Variant ?? Preset.Name} · {ReadinessLabel}";
    public string Description => Preset.Description;
}
