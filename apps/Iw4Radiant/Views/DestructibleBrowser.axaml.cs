using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class DestructibleBrowser : UserControl
{
    private XModelBrowser? _models;
    private readonly List<DestructibleThumbnail> _thumbnails = [];
    private int _previewRevision;

    public DestructibleBrowser() => InitializeComponent();

    internal event Action<DestructiblePreset>? PlacementRequested;

    internal void InitializeActions(XModelBrowser models, EditorDialogs dialogs, Action finishGestures)
    {
        _models = models;
        models.CatalogReset += ReleaseImages;
        models.CatalogChanged += () => _ = RefreshAsync();
        PresetList.SelectionChanged += (_, _) => UpdateSelection();
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
        PresetList.ItemsSource = null;
        foreach (DestructibleThumbnail thumbnail in _thumbnails) thumbnail.Preview?.Dispose();
        _thumbnails.Clear();
        PlaceButton.IsEnabled = false;
        PresetInfo.Text = "Load extracted XModels to preview and place a destructible.";
    }

    private async Task RefreshAsync()
    {
        ReleaseImages();
        if (_models is not { } models) return;
        int revision = _previewRevision;
        foreach (DestructiblePreset preset in DestructiblePresets.All)
        {
            XModelSource? source = models.ResolveModel(preset.ModelName, out string? loadError);
            bool unavailable = source is null;
            string status = unavailable
                ? $"Intact model unavailable: {loadError}"
                : preset.Description;
            Bitmap? preview = null;
            if (source is not null)
            {
                try
                {
                    preview = await Task.Run(() => new XModelPreviewRenderer(models.ResolveMaterial).Render(source, 144));
                }
                catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                {
                    status = $"{preset.Description} Preview unavailable: {exception.Message}";
                }
            }
            if (revision != _previewRevision)
            {
                preview?.Dispose();
                return;
            }
            _thumbnails.Add(new DestructibleThumbnail(preset, preview, unavailable, status));
        }
        PresetList.ItemsSource = _thumbnails.ToArray();
        if (_thumbnails.Count > 0) PresetList.SelectedIndex = 0;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = PresetList.SelectedItem as DestructibleThumbnail;
        PlaceButton.IsEnabled = selected is { IsUnavailable: false };
        PresetInfo.Text = selected is { IsUnavailable: false }
            ? $"{selected.Status} Drag onto a camera surface or grid, or use Place destructible."
            : selected?.Status ?? "Choose a destructible to place in a viewport.";
    }
}

internal sealed record DestructibleThumbnail(DestructiblePreset Preset, Bitmap? Preview, bool IsUnavailable, string Status)
{
    public string Name => Preset.Name;
    public string Description => Preset.Description;
}
