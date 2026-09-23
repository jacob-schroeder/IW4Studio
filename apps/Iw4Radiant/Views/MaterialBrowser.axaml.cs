using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class MaterialBrowser : UserControl
{
    private readonly Dictionary<string, MaterialThumbnail> _materials = new(StringComparer.Ordinal);
    private readonly AvaloniaList<MaterialThumbnail> _visibleMaterials = [];
    private HashSet<string> _usedMaterials = new(StringComparer.Ordinal);
    private bool _filtering;
    private Bitmap? _preview;
    private EditorDialogs? _dialogs;
    private EditorSession? _session;
    private Action<string>? _setStatus;
    private RadiantSettings? _settings;
    private bool _updatingCollections;
    private int _loadRevision;

    public MaterialBrowser() => InitializeComponent();

    internal void InitializeActions(Window owner, EditorSession session, EditorDialogs dialogs,
        Action finishGestures, Action<string> setStatus)
    {
        _dialogs = dialogs;
        _session = session;
        _setStatus = setStatus;
        BrowseButton.Click += async (_, _) => await BrowseAsync(owner, session, dialogs, finishGestures, setStatus);
        MaterialFilter.TextChanged += (_, _) => FilterMaterials();
        InUseToggle.IsCheckedChanged += (_, _) =>
        {
            if (InUseToggle.IsChecked == true) RefreshUsedMaterials(session);
            FilterMaterials();
        };
        session.Changed += (_, _) =>
        {
            if (InUseToggle.IsChecked == true && RefreshUsedMaterials(session)) FilterMaterials();
        };
        MaterialList.SelectionChanged += (_, _) =>
        {
            if (!_filtering) PreviewMaterial(session);
        };
        MaterialList.AddHandler(InputElement.PointerReleasedEvent, OnMaterialPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        MaterialList.AddHandler(InputElement.KeyDownEvent, OnMaterialKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        ManageFavoritesButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            string? selectedMaterial = (MaterialList.SelectedItem as MaterialThumbnail)?.Name;
            finishGestures();
            await ManageFavoritesAsync(owner, dialogs, selectedMaterial);
        };
        FindReplaceButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures();
            await FindReplaceAsync(owner, session, dialogs, setStatus);
        };

        async void OnMaterialPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left || dialogs.BlocksInput ||
                e.Source is not Control { DataContext: MaterialThumbnail material } ||
                !ReferenceEquals(MaterialList.SelectedItem, material)) return;
            await ApplyMaterialAsync(session, dialogs, finishGestures, material);
        }

        async void OnMaterialKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Space) || dialogs.BlocksInput ||
                MaterialList.SelectedItem is not MaterialThumbnail material) return;
            e.Handled = true;
            await ApplyMaterialAsync(session, dialogs, finishGestures, material);
        }
    }

    internal event Action? CatalogChanged;
    internal event Func<string, bool, Task>? FolderLoaded;
    internal MaterialSource? ResolveMaterial(string name) => _materials.GetValueOrDefault(name)?.Material;
    internal IReadOnlyList<MaterialSource> AvailableSkies => _materials.Values
        .Where(material => material.IsSky && material.Preview is not null)
        .Select(material => material.Material).OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    internal void InitializeFavorites(RadiantSettings settings)
    {
        _settings = settings;
        FavoriteCollection.SelectionChanged += (_, _) =>
        {
            if (!_updatingCollections) FilterMaterials();
        };
        RefreshCollectionChoices(null);
    }

    private MaterialFavoriteCollection? ActiveFavoriteCollection()
    {
        int index = FavoriteCollection.SelectedIndex - 1;
        return _settings is { } settings && index >= 0 && index < settings.MaterialFavorites.Count
            ? settings.MaterialFavorites[index] : null;
    }

    private void RefreshCollectionChoices(MaterialFavoriteCollection? selected)
    {
        if (_settings is not { } settings) return;
        _updatingCollections = true;
        try
        {
            FavoriteCollection.ItemsSource = new[] { "All materials" }
                .Concat(settings.MaterialFavorites.Select(collection => collection.Name)).ToArray();
            int index = selected is null ? -1 : settings.MaterialFavorites.IndexOf(selected);
            FavoriteCollection.SelectedIndex = index + 1;
        }
        finally { _updatingCollections = false; }
        FilterMaterials();
    }

    private async Task ManageFavoritesAsync(Window owner, EditorDialogs dialogs, string? selectedMaterial)
    {
        if (dialogs.BlocksInput || _settings is not { } settings) return;
        var collections = new ComboBox { Width = 330 };
        var name = new TextBox { Width = 240, PlaceholderText = "Collection name" };
        var materials = new ListBox { Height = 165 };
        var unavailable = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var add = new Button { Content = "Add selected material", MinWidth = 138 };
        var remove = new Button { Content = "Remove saved material", MinWidth = 145 };
        var rename = new Button { Content = "Rename" };
        var delete = new Button { Content = "Delete collection" };
        var create = new Button { Content = "New collection" };
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = "Material collections", Width = 560, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Collections keep exact material names across catalog changes." },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { new TextBlock { Text = "Collection", VerticalAlignment = VerticalAlignment.Center }, collections } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { name, create, rename } },
                new TextBlock { Text = selectedMaterial is null
                    ? "Select a material thumbnail before opening this window to add it."
                    : $"Selected material: {selectedMaterial}", TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { add, remove, delete } },
                materials, unavailable, status, close
            }
        };

        string[] shownNames = [];
        MaterialFavoriteCollection? pendingDelete = null;
        MaterialFavoriteCollection? SelectedCollection()
        {
            int index = collections.SelectedIndex;
            return index >= 0 && index < settings.MaterialFavorites.Count
                ? settings.MaterialFavorites[index] : null;
        }

        void RefreshMaterials()
        {
            MaterialFavoriteCollection? collection = SelectedCollection();
            shownNames = collection?.Materials.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            materials.ItemsSource = shownNames.Select(value => _materials.TryGetValue(value, out var thumbnail) &&
                thumbnail.Preview is not null ? value : $"{value} · unavailable").ToArray();
            string[] missing = shownNames.Where(value => !_materials.TryGetValue(value, out var thumbnail) ||
                thumbnail.Preview is null).ToArray();
            unavailable.Text = missing.Length == 0 ? "" : $"{missing.Length} unavailable names are marked above.";
            add.IsEnabled = collection is not null && selectedMaterial is not null &&
                !collection.Materials.Contains(selectedMaterial, StringComparer.Ordinal);
            remove.IsEnabled = collection is not null && materials.SelectedIndex >= 0;
            rename.IsEnabled = delete.IsEnabled = collection is not null;
        }

        void RefreshCollections(MaterialFavoriteCollection? selected)
        {
            collections.ItemsSource = settings.MaterialFavorites.Select(collection => collection.Name).ToArray();
            collections.SelectedIndex = selected is null ? -1 : settings.MaterialFavorites.IndexOf(selected);
            RefreshMaterials();
        }

        void Save(string message)
        {
            status.Text = settings.Save() ? message : "Changes are in memory but could not be saved to user settings.";
        }

        bool TryName(MaterialFavoriteCollection? except, out string value)
        {
            string candidate = (name.Text ?? "").Trim();
            value = candidate;
            if (candidate.Length is 0 or > 64 || candidate.Equals("All materials", StringComparison.OrdinalIgnoreCase) ||
                settings.MaterialFavorites.Any(collection => !ReferenceEquals(collection, except) &&
                    collection.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                status.Text = "Enter a unique collection name of 1–64 characters other than ‘All materials’.";
                return false;
            }
            return true;
        }

        collections.SelectionChanged += (_, _) =>
        {
            pendingDelete = null;
            status.Text = "";
            name.Text = SelectedCollection()?.Name ?? "";
            RefreshMaterials();
        };
        materials.SelectionChanged += (_, _) => remove.IsEnabled = SelectedCollection() is not null &&
            materials.SelectedIndex >= 0;
        create.Click += (_, _) =>
        {
            if (!TryName(null, out string value)) return;
            var collection = new MaterialFavoriteCollection { Name = value };
            settings.MaterialFavorites.Add(collection);
            RefreshCollections(collection);
            Save($"Created ‘{value}’.");
        };
        rename.Click += (_, _) =>
        {
            if (SelectedCollection() is not { } collection || !TryName(collection, out string value)) return;
            collection.Name = value;
            pendingDelete = null;
            RefreshCollections(collection);
            Save($"Renamed collection to ‘{value}’.");
        };
        delete.Click += (_, _) =>
        {
            if (SelectedCollection() is not { } collection) return;
            if (!ReferenceEquals(pendingDelete, collection))
            {
                pendingDelete = collection;
                status.Text = $"Click Delete collection again to remove ‘{collection.Name}’ and its saved names.";
                return;
            }
            settings.MaterialFavorites.Remove(collection);
            pendingDelete = null;
            RefreshCollections(null);
            Save("Deleted collection.");
        };
        add.Click += (_, _) =>
        {
            if (SelectedCollection() is not { } collection || selectedMaterial is null ||
                collection.Materials.Contains(selectedMaterial, StringComparer.Ordinal)) return;
            collection.Materials.Add(selectedMaterial);
            pendingDelete = null;
            RefreshMaterials();
            Save($"Saved ‘{selectedMaterial}’.");
        };
        remove.Click += (_, _) =>
        {
            int index = materials.SelectedIndex;
            if (SelectedCollection() is not { } collection ||
                index < 0 || index >= shownNames.Length) return;
            string removed = shownNames[index];
            collection.Materials.Remove(removed);
            pendingDelete = null;
            RefreshMaterials();
            Save($"Removed ‘{removed}’.");
        };
        close.Click += (_, _) => dialog.Close();
        RefreshCollections(ActiveFavoriteCollection());
        await dialogs.ShowModalAsync(() => dialog.ShowDialog<object?>(owner));
        RefreshCollectionChoices(SelectedCollection());
    }

    private async Task FindReplaceAsync(Window owner, EditorSession session, EditorDialogs dialogs,
        Action<string> setStatus)
    {
        string? selectedName = SurfaceEditing.GetFaces(session).FirstOrDefault()?.Face.Material ??
            session.Selection.Items.Select(EditorSelection.Owner).OfType<MapTerrain>().FirstOrDefault()?.Material;
        var from = new TextBox { Text = selectedName ?? session.Material, PlaceholderText = "Exact material name" };
        var to = new TextBox { PlaceholderText = "Replacement material name" };
        var chooseFrom = new Button { Content = "Choose from map…" };
        var chooseTo = new Button { Content = "Browse materials…" };
        var fromImage = new Image { Width = 72, Height = 72, Stretch = Stretch.Uniform };
        var toImage = new Image { Width = 72, Height = 72, Stretch = Stretch.Uniform };
        var fromPreviewInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var toPreviewInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var selectedOnly = new CheckBox { Content = "Selected surfaces only" };
        var count = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var replace = new Button { Content = "Replace", HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" };
        var fromRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 8 };
        fromRow.Children.Add(from);
        Grid.SetColumn(chooseFrom, 1);
        fromRow.Children.Add(chooseFrom);
        var toRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 8 };
        toRow.Children.Add(to);
        Grid.SetColumn(chooseTo, 1);
        toRow.Children.Add(chooseTo);
        static StackPanel PreviewRow(Image image, TextBlock info) => new()
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children =
            {
                new Border { Width = 72, Height = 72, Background = new SolidColorBrush(Color.Parse("#303237")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#45484E")), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Child = image },
                info
            }
        };
        var dialog = new Window
        {
            Title = "Find / replace materials", Width = 610, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 9,
            Children =
            {
                new TextBlock { Text = "Find material", FontWeight = FontWeight.SemiBold },
                fromRow, PreviewRow(fromImage, fromPreviewInfo),
                new TextBlock { Text = "Replace with", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) },
                toRow, PreviewRow(toImage, toPreviewInfo), selectedOnly, count, error,
                new TextBlock { Text = "Names match exactly, including case. Geometry and texture mapping are preserved; prefab source maps are unchanged.",
                    TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, replace } }
            }
        };
        dialog.Opened += (_, _) => (string.IsNullOrEmpty(from.Text) ? from : to).Focus();

        Bitmap? fromPreview = null, toPreview = null;
        void RefreshFromPreview()
        {
            fromImage.Source = null;
            fromPreview?.Dispose();
            fromPreview = LoadDialogPreview(from.Text, fromImage, fromPreviewInfo);
        }
        void RefreshToPreview()
        {
            toImage.Source = null;
            toPreview?.Dispose();
            toPreview = LoadDialogPreview(to.Text, toImage, toPreviewInfo);
        }

        void RefreshCount()
        {
            string source = from.Text ?? "";
            int matches = SelectionEditing.CountMaterialMatches(session, source, selectedOnly.IsChecked == true);
            count.Text = $"{matches} matching surface{(matches == 1 ? "" : "s")} in " +
                (selectedOnly.IsChecked == true ? "the current selection" : "the current map") + ".";
            error.Text = "";
            replace.IsEnabled = matches > 0 && !string.IsNullOrEmpty(to.Text) &&
                !string.Equals(source, to.Text, StringComparison.Ordinal);
        }

        from.TextChanged += (_, _) => { RefreshCount(); RefreshFromPreview(); };
        to.TextChanged += (_, _) => { RefreshCount(); RefreshToPreview(); };
        selectedOnly.IsCheckedChanged += (_, _) => RefreshCount();
        chooseFrom.Click += async (_, _) =>
        {
            MaterialPickerOption[] options = session.Document.Brushes.SelectMany(brush => brush.Faces)
                .Select(face => face.Material).Concat(session.Document.Terrains.Select(terrain => terrain.Material))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name, StringComparer.Ordinal)
                .Select(group => new MaterialPickerOption(group.Key, ResolveMaterial(group.Key), group.Count()))
                .ToArray();
            if (await MaterialPickerDialog.ShowAsync(dialog, "Choose material in this map", options, from.Text) is { } name)
                from.Text = name;
        };
        chooseTo.Click += async (_, _) =>
        {
            MaterialPickerOption[] options = _materials.Values
                .Select(material => new MaterialPickerOption(material.Name, material.Material, null)).ToArray();
            if (await MaterialPickerDialog.ShowAsync(dialog, "Choose replacement material", options, to.Text) is { } name)
                to.Text = name;
        };
        cancel.Click += (_, _) => dialog.Close(false);
        replace.Click += (_, _) =>
        {
            try { SelectionEditing.ValidateMaterial(to.Text ?? ""); }
            catch (ArgumentException exception) { error.Text = exception.Message; return; }
            dialog.Close(true);
        };
        RefreshCount();
        RefreshFromPreview();
        RefreshToPreview();
        bool confirmed;
        try { confirmed = await dialogs.ShowModalAsync(() => dialog.ShowDialog<bool>(owner)); }
        finally
        {
            fromImage.Source = toImage.Source = null;
            fromPreview?.Dispose();
            toPreview?.Dispose();
        }
        if (!confirmed) return;
        try
        {
            string source = from.Text ?? "";
            string replacement = to.Text ?? "";
            int changed = SelectionEditing.ReplaceMaterials(session, source, replacement, selectedOnly.IsChecked == true);
            setStatus($"Replaced {changed} material surface{(changed == 1 ? "" : "s")} from ‘{source}’ to ‘{replacement}’.");
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Cannot replace materials", exception.Message); }
    }

    private Bitmap? LoadDialogPreview(string? name, Image image, TextBlock info)
    {
        if (string.IsNullOrEmpty(name))
        {
            info.Text = "Choose a material to preview it.";
            return null;
        }
        if (ResolveMaterial(name) is not { } material)
        {
            info.Text = "Preview unavailable. The exact name can still be used.";
            return null;
        }
        try
        {
            Bitmap preview = MaterialImages.Load(material, 96);
            image.Source = preview;
            info.Text = material.IsSky ? "Sky cube · +X face" : material.IsWater ? "Water tint" : "Loaded material preview";
            return preview;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            info.Text = "Preview unavailable. The exact name can still be used.";
            return null;
        }
    }

    internal void ChooseMaterial(string name)
    {
        if (!_materials.TryGetValue(name, out var material) || material.Preview is null)
            throw new ArgumentException($"Material '{name}' has no available preview.");
        if (ActiveFavoriteCollection() is { } collection &&
            !collection.Materials.Contains(name, StringComparer.Ordinal)) FavoriteCollection.SelectedIndex = 0;
        if (InUseToggle.IsChecked == true && !_usedMaterials.Contains(name)) InUseToggle.IsChecked = false;
        MaterialFilter.Text = name;
        FilterMaterials();
        MaterialList.SelectedItem = material;
        MaterialList.ScrollIntoView(material);
    }

    internal void UsePlayerClip(EditorSession session)
    {
        MaterialList.SelectedItem = null;
        ReleasePreview();
        MaterialName.Text = ClipBrushMaterial.PlayerClip;
        PreviewInfo.Text = "Player clip · invisible in game; blocks players. Magenta outlines show the editable volume.";
        session.Material = ClipBrushMaterial.PlayerClip;
        session.Refresh();
    }

    internal void ReleaseImages(bool invalidateLoad = true)
    {
        if (invalidateLoad) _loadRevision++;
        MaterialList.ItemsSource = null;
        _visibleMaterials.Clear();
        ReleasePreview();
        foreach (var material in _materials.Values) material.Preview?.Dispose();
        _materials.Clear();
    }

    private void ReleasePreview()
    {
        MaterialPreview.Source = null;
        _preview?.Dispose();
        _preview = null;
    }

    private async Task BrowseAsync(Window owner, EditorSession session, EditorDialogs dialogs,
        Action finishGestures, Action<string> setStatus)
    {
        if (dialogs.BlocksInput) return;
        finishGestures();
        var folders = await dialogs.ShowModalAsync(() => owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose raw assets or a texture folder", AllowMultiple = false }));
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } root) return;
        await LoadFolderAsync(root);
    }

    internal async Task<bool> LoadFolderAsync(string root, bool nonBlocking = false)
    {
        if (_dialogs is not { } dialogs || _session is not { } session || _setStatus is not { } setStatus) return false;
        int revision = ++_loadRevision;
        bool loaded = false;
        bool catalogInstalled = false;
        try
        {
            if (nonBlocking) setStatus("Loading saved material previews…");
            else dialogs.SetBusy(true);
            var (catalog, unsupportedMaterials) = await Task.Run(() => MaterialCatalog.Read(root));
            if (revision != _loadRevision)
                return false;
            ReleaseImages(invalidateLoad: false);
            MaterialSource[] sources = catalog.Values.ToArray();
            catalogInstalled = true;
            session.Material = "";
            MaterialName.Text = "";
            MaterialFilter.Text = "";
            FilterMaterials();
            int skippedImages = 0;
            for (int index = 0; index < sources.Length;)
            {
                int batchSize = index == 0 ? 16 : 64;
                var batch = sources[index..Math.Min(index + batchSize, sources.Length)];
                var (thumbnails, skipped) = await Task.Run(() => LoadThumbnails(batch));
                if (revision != _loadRevision)
                {
                    foreach (var thumbnail in thumbnails) thumbnail.Preview?.Dispose();
                    return false;
                }
                skippedImages += skipped;
                foreach (var thumbnail in thumbnails)
                {
                    _materials.Add(thumbnail.Name, thumbnail);
                }
                FilterMaterials();
                index += batch.Length;
            }
            CatalogChanged?.Invoke();
            loaded = true;
            setStatus($"Loaded {_materials.Values.Count(material => material.Preview is not null)} material previews from {root}." +
                (skippedImages > 0 ? $" {skippedImages} images could not be read." : "") +
                (unsupportedMaterials > 0 ? $" {unsupportedMaterials} unsupported material definitions were skipped." : ""));
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            if (revision == _loadRevision)
            {
                if (catalogInstalled) ReleaseImages(invalidateLoad: false);
                if (nonBlocking) setStatus($"Could not load saved materials: {exception.Message}");
                else
                {
                    dialogs.SetBusy(false);
                    await dialogs.MessageAsync("Cannot read materials", exception.Message);
                }
            }
        }
        catch
        {
            if (catalogInstalled && revision == _loadRevision) ReleaseImages(invalidateLoad: false);
            throw;
        }
        finally { if (!nonBlocking) dialogs.SetBusy(false); }
        if (loaded && FolderLoaded is { } loadRelatedAssets) await loadRelatedAssets(root, nonBlocking);
        return loaded;
    }

    private static (List<MaterialThumbnail> Materials, int SkippedImages) LoadThumbnails(
        IReadOnlyList<MaterialSource> sources)
    {
        var thumbnails = new List<MaterialThumbnail>();
        int skipped = 0;
        try
        {
            foreach (var material in sources)
            {
                try
                {
                    var preview = MaterialImages.Load(material, 96);
                    thumbnails.Add(new MaterialThumbnail(material, preview));
                }
                catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                {
                    skipped++;
                    if (material.IsSky) thumbnails.Add(new MaterialThumbnail(material, null));
                }
            }
            return (thumbnails, skipped);
        }
        catch
        {
            foreach (var thumbnail in thumbnails) thumbnail.Preview?.Dispose();
            throw;
        }
    }

    private bool RefreshUsedMaterials(EditorSession session)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        Add(session.Document);
        foreach (var instance in session.Document.Entities.Where(PrefabLibrary.IsPrefab))
            if (session.Prefabs.GetPreview(instance, session.FilePath) is { } preview) Add(preview);
        if (_usedMaterials.SetEquals(used)) return false;
        _usedMaterials = used;
        return true;

        void Add(MapDocument document)
        {
            used.UnionWith(document.Brushes.SelectMany(brush => brush.Faces).Select(face => face.Material));
            used.UnionWith(document.Terrains.Select(terrain => terrain.Material));
        }
    }

    private void FilterMaterials()
    {
        string filter = MaterialFilter.Text ?? "";
        bool inUse = InUseToggle.IsChecked == true;
        MaterialFavoriteCollection? collection = ActiveFavoriteCollection();
        HashSet<string>? favorites = collection is null ? null : new(collection.Materials, StringComparer.Ordinal);
        int available = _materials.Values.Count(material => material.Preview is not null);
        MaterialThumbnail[] matches = _materials.Values.Where(material => material.Preview is not null &&
                (favorites is null || favorites.Contains(material.Name)) &&
                (!inUse || _usedMaterials.Contains(material.Name)) &&
                material.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(material => material.Name.Equals(filter, StringComparison.OrdinalIgnoreCase))
            .ThenBy(material => material.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var shown = matches.Take(2000).ToArray();
        var selected = MaterialList.SelectedItem as MaterialThumbnail;
        _filtering = true;
        try
        {
            if (!ReferenceEquals(MaterialList.ItemsSource, _visibleMaterials)) MaterialList.ItemsSource = _visibleMaterials;
            for (int index = 0; index < shown.Length; index++)
            {
                if (index < _visibleMaterials.Count && ReferenceEquals(_visibleMaterials[index], shown[index])) continue;
                int currentIndex = _visibleMaterials.IndexOf(shown[index]);
                if (currentIndex >= index) _visibleMaterials.RemoveAt(currentIndex);
                _visibleMaterials.Insert(index, shown[index]);
            }
            while (_visibleMaterials.Count > shown.Length) _visibleMaterials.RemoveAt(_visibleMaterials.Count - 1);
            MaterialList.SelectedItem = selected is not null && shown.Contains(selected) ? selected : null;
        }
        finally { _filtering = false; }
        if (selected is not null && MaterialList.SelectedItem is null)
        {
            ReleasePreview();
            PreviewInfo.Text = "Choose a material to preview.";
        }
        MaterialInfo.Text = (matches.Length == available
            ? $"{available} materials"
            : $"{matches.Length} of {available} materials") + (inUse ? " · in use" : "") +
            (collection is null ? "" : $" · {collection.Name}") +
            (matches.Length > 2000 ? " · first 2,000 shown; narrow search" : "");
    }
    private void PreviewMaterial(EditorSession session)
    {
        ReleasePreview();
        PreviewInfo.Text = "Choose a material to preview.";
        if (MaterialList.SelectedItem is not MaterialThumbnail material) return;
        MaterialName.Text = material.Name;
        session.Material = "";
        try
        {
            _preview = MaterialImages.Load(material.Material, 256);
            MaterialPreview.Source = _preview;
            PreviewInfo.Text = material.Material.IsWater
                ? "Native PS3 water tint · camera uses GPU waves and authored reflection probes."
                : Path.GetFileName(material.Material.ImagePath) + (material.IsSky ? " · Sky cube, +X face" : "");
            session.Material = material.Name;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            PreviewInfo.Text = exception.Message;
            PreviewToggle.IsChecked = true;
        }
        session.Refresh();
    }
    private async Task ApplyMaterialAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        MaterialThumbnail material)
    {
        if (dialogs.BlocksInput) return;
        finishGestures();
        try
        {
            string name = material.Name;
            if (!material.Material.IsWater && !File.Exists(material.Material.ImagePath))
                throw new ArgumentException("Choose a material with an available image or recognized native water profile from the browser.");
            bool hasSurfaceSelection = SurfaceEditing.GetFaces(session).Any() ||
                session.Selection.Items.Select(EditorSelection.Owner).OfType<MapTerrain>().Any() ||
                session.Selection.Items.OfType<MapEntity>().Any(entity => entity.Terrains.Count > 0);
            if (material.IsSky && hasSurfaceSelection) SkyEditing.Apply(session, material.Material);
            else
            {
                using var image = MaterialImages.Load(material.Material, 256);
                SelectionEditing.ApplyMaterial(session, name);
            }
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Material name", exception.Message); }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { await dialogs.MessageAsync("Cannot read material image", exception.Message); }
    }

}
