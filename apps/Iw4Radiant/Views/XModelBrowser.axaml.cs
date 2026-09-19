using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Views;

public partial class XModelBrowser : UserControl
{
    private XModelCatalog? _catalog;
    private readonly List<XModelThumbnail> _thumbnails = [];
    private EditorDialogs? _dialogs;
    private Action<string>? _setStatus;
    private int _loadRevision;
    private CancellationTokenSource? _filterCancellation;

    public XModelBrowser() => InitializeComponent();

    internal event Action? CatalogChanged;
    internal event Action<string>? FolderLoaded;
    internal event Action<XModelSource, bool>? PlacementRequested;
    internal event Action? DropRequested;
    internal XModelSource? ResolveModel(string name)
    {
        var source = _catalog?.Resolve(name);
        if (source is null) return null;
        try { _ = source.Document; return source; }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { return null; }
    }
    internal MaterialSource? ResolveMaterial(string name) => _catalog?.ResolveMaterial(name);

    internal void InitializeActions(Window owner, EditorSession session, EditorDialogs dialogs,
        Action finishGestures, Action<string> setStatus)
    {
        _dialogs = dialogs;
        _setStatus = setStatus;
        BrowseButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures();
            var folders = await dialogs.ShowModalAsync(() => owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = "Choose extracted raw assets containing xmodel and model_export", AllowMultiple = false }));
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } root) await LoadFolderAsync(root);
        };
        ModelFilter.TextChanged += async (_, _) => await FilterModelsAsync();
        ModelList.SelectionChanged += (_, _) =>
        {
            var selected = ModelList.SelectedItem as XModelThumbnail;
            PlaceButton.IsEnabled = selected is not null;
            PreviewButton.IsEnabled = selected?.Preview is not null;
            FindButton.IsEnabled = ReplaceButton.IsEnabled = selected is not null;
            if (selected is not null)
            {
                ModelInfo.Text = selected.Description;
                ToolTip.SetTip(ModelInfo, selected.Description);
            }
        };
        PlaceButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput || ModelList.SelectedItem is not XModelThumbnail selected) return;
            finishGestures();
            try { _ = selected.Model.Document; PlacementRequested?.Invoke(selected.Model, AlignSurface.IsChecked == true); }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                { await dialogs.MessageAsync("Cannot place model", exception.Message); }
        };
        DropButton.Click += (_, _) => { if (!dialogs.BlocksInput) { finishGestures(); DropRequested?.Invoke(); } };
        FindButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput || ModelList.SelectedItem is not XModelThumbnail selected) return;
            finishGestures();
            int count = XModelEditing.FindInstances(session, selected.Name);
            setStatus($"Selected {count} instances of {selected.Name}.");
        };
        ReplaceButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput || ModelList.SelectedItem is not XModelThumbnail selected) return;
            finishGestures();
            await ReplaceAsync(owner, session, dialogs, selected.Model, setStatus);
        };
        PreviewButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput || ModelList.SelectedItem is not XModelThumbnail selected || _catalog is null) return;
            finishGestures();
            await PreviewAsync(owner, dialogs, selected.Model, _catalog);
        };
    }

    internal async Task<bool> LoadFolderAsync(string root)
    {
        if (_dialogs is not { } dialogs) return false;
        int revision = ++_loadRevision;
        try
        {
            dialogs.SetBusy(true);
            var catalog = await Task.Run(() => XModelCatalog.Read(root));
            if (revision != _loadRevision) return false;
            ReleaseImages();
            _catalog = catalog;
            ModelFilter.Text = "";
            await FilterModelsAsync();
            CatalogChanged?.Invoke();
            _setStatus?.Invoke($"Loaded {catalog.Models.Count} XModels from {root}. Search by name to browse matching thumbnails.");
            FolderLoaded?.Invoke(root);
            return true;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            dialogs.SetBusy(false);
            await dialogs.MessageAsync("Cannot load models", exception.Message);
        }
        finally { dialogs.SetBusy(false); }
        return false;
    }

    internal void ReleaseImages()
    {
        _filterCancellation?.Cancel();
        ModelList.ItemsSource = null;
        foreach (var thumbnail in _thumbnails) thumbnail.Preview?.Dispose();
        _thumbnails.Clear();
        _catalog = null;
    }

    private async Task FilterModelsAsync()
    {
        _filterCancellation?.Cancel();
        if (_catalog is not { } catalog) return;
        using var cancellation = new CancellationTokenSource();
        _filterCancellation = cancellation;
        string filter = ModelFilter.Text ?? "";
        var models = catalog.Models.Where(model => model.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(model => model.Name.Equals(filter, StringComparison.OrdinalIgnoreCase)).ThenBy(model => model.Name, StringComparer.Ordinal).ToArray();
        var visible = models.Take(120).ToArray();
        var cached = _thumbnails.ToDictionary(thumbnail => thumbnail.Name, StringComparer.Ordinal);
        XModelSource[] missing = visible.Where(model => !cached.ContainsKey(model.Name)).ToArray();
        var loaded = new List<XModelThumbnail>();
        try
        {
            await Task.Delay(180, cancellation.Token);
            ModelInfo.Text = $"Loading {visible.Length} model previews…";
            await Task.Run(() =>
            {
                var renderer = new XModelPreviewRenderer(catalog.ResolveMaterial);
                foreach (var model in missing)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    try { loaded.Add(new XModelThumbnail(model, renderer.Render(model, 96), null)); }
                    catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                        { loaded.Add(new XModelThumbnail(model, null, exception.Message)); }
                }
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _thumbnails.AddRange(loaded);
            foreach (var thumbnail in loaded) cached[thumbnail.Name] = thumbnail;
            loaded.Clear();
            var previews = visible.Select(model => cached[model.Name]).ToArray();
            var unavailable = previews.Where(preview => preview.Preview is null).ToArray();
            ModelList.ItemsSource = previews.Where(preview => preview.Preview is not null).ToArray();
            ModelInfo.Text = $"{models.Length} of {catalog.Models.Count} models" +
                (models.Length > visible.Length ? " · first 120 matches; narrow search" : "") +
                (unavailable.Length > 0 ? $" · {unavailable.Length} unavailable previews omitted" : "") + " · Place model, then click a viewport.";
            ToolTip.SetTip(ModelInfo, unavailable.Length > 0 ? string.Join('\n', unavailable.Select(preview => $"{preview.Name}: {preview.Error}")) : ModelInfo.Text);
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var thumbnail in loaded) thumbnail.Preview?.Dispose();
            if (ReferenceEquals(_filterCancellation, cancellation)) _filterCancellation = null;
        }
    }

    private static async Task ReplaceAsync(Window owner, EditorSession session, EditorDialogs dialogs,
        XModelSource replacement, Action<string> setStatus)
    {
        string[] names = session.Document.Entities.Where(XModelGeometry.IsModel).Select(entity => entity.Properties.GetValueOrDefault("model"))
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (names.Length == 0) { await dialogs.MessageAsync("Find / replace models", "This map has no model instances."); return; }
        var from = new ComboBox { ItemsSource = names, SelectedItem = names.Contains(replacement.Name) ? replacement.Name : names[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        var selectedOnly = new CheckBox { Content = "Selected instances only" };
        var replace = new Button { Content = "Replace", HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" };
        var dialog = new Window { Title = "Find / replace models", Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children =
        {
            new TextBlock { Text = "Find model" }, from,
            new TextBlock { Text = $"Replace with: {replacement.Name}", TextWrapping = TextWrapping.Wrap }, selectedOnly,
            new TextBlock { Text = "Positions, rotations, scales and other entity properties are preserved.", TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, replace } }
        } };
        cancel.Click += (_, _) => dialog.Close(false);
        replace.Click += (_, _) => dialog.Close(true);
        if (!await dialogs.ShowModalAsync(() => dialog.ShowDialog<bool>(owner)) || from.SelectedItem is not string name) return;
        try { setStatus($"Replaced {XModelEditing.ReplaceInstances(session, name, replacement, selectedOnly.IsChecked == true)} instances of {name} with {replacement.Name}."); }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { await dialogs.MessageAsync("Cannot replace models", exception.Message); }
    }

    private static async Task PreviewAsync(Window owner, EditorDialogs dialogs, XModelSource model, XModelCatalog catalog)
    {
        var renderer = new XModelPreviewRenderer(catalog.ResolveMaterial);
        var image = new Image { Stretch = Stretch.Uniform, MinHeight = 320 };
        var yaw = new Slider { Minimum = -180, Maximum = 180, Value = -45 };
        var pitch = new Slider { Minimum = -85, Maximum = 85, Value = 25 };
        var zoom = new Slider { Minimum = 0.5, Maximum = 3, Value = 1 };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Bitmap? bitmap = null;
        var dialog = new Window { Title = model.Name, Width = 640, Height = 690, MinWidth = 400, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var grid = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto,Auto,Auto,Auto"), Margin = new Thickness(12), RowSpacing = 8 };
        grid.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#303237")), Child = image });
        AddSlider("Orbit", yaw, 1); AddSlider("Elevation", pitch, 2); AddSlider("Zoom", zoom, 3);
        Grid.SetRow(status, 4); grid.Children.Add(status);
        dialog.Content = grid;
        yaw.ValueChanged += (_, _) => Render(); pitch.ValueChanged += (_, _) => Render(); zoom.ValueChanged += (_, _) => Render();
        Render();
        try { await dialogs.ShowModalAsync(() => dialog.ShowDialog<bool?>(owner)); }
        finally { image.Source = null; bitmap?.Dispose(); }

        void Render()
        {
            try
            {
                var next = renderer.Render(model, 512, (float)yaw.Value, (float)pitch.Value, (float)zoom.Value);
                image.Source = next;
                bitmap?.Dispose(); bitmap = next;
                var bounds = model.Bounds;
                var size = bounds.Max - bounds.Min;
                status.Text = FormattableString.Invariant($"{model.Document.Triangles.Count:N0} triangles · {model.Document.Materials.Count} materials · {size.X:0.#} × {size.Y:0.#} × {size.Z:0.#} units");
            }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { status.Text = exception.Message; }
        }

        void AddSlider(string label, Slider slider, int row)
        {
            var line = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("70,*"), ColumnSpacing = 8 };
            line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(slider, 1); line.Children.Add(slider); Grid.SetRow(line, row); grid.Children.Add(line);
        }
    }
}
