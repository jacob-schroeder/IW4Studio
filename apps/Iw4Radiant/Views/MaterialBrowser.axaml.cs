using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class MaterialBrowser : UserControl
{
    private readonly Dictionary<string, MaterialThumbnail> _materials = new(StringComparer.Ordinal);
    private HashSet<string> _usedMaterials = new(StringComparer.Ordinal);
    private bool _filtering;
    private Bitmap? _preview;
    private EditorDialogs? _dialogs;
    private EditorSession? _session;
    private Action<string>? _setStatus;

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
    internal event Func<string, Task>? FolderLoaded;
    internal MaterialSource? ResolveMaterial(string name) => _materials.GetValueOrDefault(name)?.Material;
    internal IReadOnlyList<MaterialSource> AvailableSkies => _materials.Values
        .Where(material => material.IsSky && material.Preview is not null)
        .Select(material => material.Material).OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    internal void ChooseMaterial(string name)
    {
        if (!_materials.TryGetValue(name, out var material) || material.Preview is null)
            throw new ArgumentException($"Material '{name}' has no available preview.");
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

    internal void ReleaseImages()
    {
        MaterialList.ItemsSource = null;
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

    internal async Task<bool> LoadFolderAsync(string root)
    {
        if (_dialogs is not { } dialogs || _session is not { } session || _setStatus is not { } setStatus) return false;
        bool loaded = false;
        try
        {
            dialogs.SetBusy(true);
            var (materials, skipped) = await Task.Run(() => LoadThumbnails(root));
            ReleaseImages();
            foreach (var material in materials) _materials.Add(material.Name, material);
            session.Material = "";
            MaterialName.Text = "";
            MaterialFilter.Text = "";
            FilterMaterials();
            CatalogChanged?.Invoke();
            loaded = true;
            setStatus($"Loaded {materials.Count(material => material.Preview is not null)} material previews from {root}." +
                (skipped > 0 ? $" {skipped} images could not be read." : ""));
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { dialogs.SetBusy(false); await dialogs.MessageAsync("Cannot read materials", exception.Message); }
        finally { dialogs.SetBusy(false); }
        if (loaded && FolderLoaded is { } loadRelatedAssets) await loadRelatedAssets(root);
        return loaded;
    }

    private static (List<MaterialThumbnail> Materials, int Skipped) LoadThumbnails(string root)
    {
        var thumbnails = new List<MaterialThumbnail>();
        int skipped = 0;
        try
        {
            foreach (var material in MaterialCatalog.Read(root))
            {
                try
                {
                    var preview = MaterialImages.Load(material.Value, 96);
                    thumbnails.Add(new MaterialThumbnail(material.Value, preview));
                }
                catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                {
                    skipped++;
                    if (material.Value.IsSky)
                        thumbnails.Add(new MaterialThumbnail(material.Value, null));
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
        int available = _materials.Values.Count(material => material.Preview is not null);
        MaterialThumbnail[] matches = _materials.Values.Where(material => material.Preview is not null &&
                (!inUse || _usedMaterials.Contains(material.Name)) &&
                material.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(material => material.Name.Equals(filter, StringComparison.OrdinalIgnoreCase))
            .ThenBy(material => material.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var shown = matches.Take(2000).ToArray();
        var selected = MaterialList.SelectedItem as MaterialThumbnail;
        _filtering = true;
        try
        {
            MaterialList.ItemsSource = shown;
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
