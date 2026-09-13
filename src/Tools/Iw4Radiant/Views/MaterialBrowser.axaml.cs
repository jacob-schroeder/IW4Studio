using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class MaterialBrowser : UserControl
{
    private readonly Dictionary<string, MaterialThumbnail> _materials = new(StringComparer.Ordinal);
    private Bitmap? _preview;

    public MaterialBrowser() => InitializeComponent();

    internal void InitializeActions(Window owner, EditorSession session, EditorDialogs dialogs,
        Action finishGestures, Action<string> setStatus)
    {
        BrowseButton.Click += async (_, _) => await BrowseAsync(owner, session, dialogs, finishGestures, setStatus);
        MaterialFilter.TextChanged += (_, _) => FilterMaterials();
        MaterialList.SelectionChanged += (_, _) => PreviewMaterial(session);
        ApplyMaterialButton.Click += async (_, _) => await ApplyMaterialAsync(session, dialogs, finishGestures);
    }

    internal event Action? CatalogChanged;
    internal string? ResolveTexturePath(string name) => _materials.GetValueOrDefault(name)?.ImagePath;

    internal void ReleaseImages()
    {
        MaterialList.ItemsSource = null;
        ReleasePreview();
        foreach (var material in _materials.Values) material.Preview.Dispose();
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
            setStatus($"Loaded {materials.Count} material previews from {root}." +
                (skipped > 0 ? $" {skipped} images could not be read." : ""));
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { dialogs.SetBusy(false); await dialogs.MessageAsync("Cannot read materials", exception.Message); }
        finally { dialogs.SetBusy(false); }
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
                    thumbnails.Add(new MaterialThumbnail(material.Key, material.Value, preview));
                }
                catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { skipped++; }
            }
            return (thumbnails, skipped);
        }
        catch
        {
            foreach (var thumbnail in thumbnails) thumbnail.Preview.Dispose();
            throw;
        }
    }

    private void FilterMaterials()
    {
        string filter = MaterialFilter.Text ?? "";
        MaterialThumbnail[] matches = _materials.Values.Where(material => material.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        MaterialList.ItemsSource = matches.Take(2000).ToArray();
        MaterialInfo.Text = $"{_materials.Count} available materials" +
            (matches.Length > 2000 ? " · showing first 2,000; filter to narrow" : "");
    }
    private void PreviewMaterial(EditorSession session)
    {
        ReleasePreview();
        PreviewInfo.Text = "Choose an available material to preview it.";
        if (MaterialList.SelectedItem is not MaterialThumbnail material) return;
        MaterialName.Text = material.Name;
        session.Material = "";
        try
        {
            _preview = MaterialImages.Load(material.ImagePath, 256);
            MaterialPreview.Source = _preview;
            PreviewInfo.Text = Path.GetFileName(material.ImagePath);
            session.Material = material.Name;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { PreviewInfo.Text = exception.Message; }
    }
    private async Task ApplyMaterialAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        finishGestures();
        try
        {
            string name = (MaterialName.Text ?? "").Trim();
            if (!_materials.TryGetValue(name, out var material) || !File.Exists(material.ImagePath))
                throw new ArgumentException("Choose a material with an available image from the browser.");
            using var image = MaterialImages.Load(material.ImagePath, 256);
            SelectionEditing.ApplyMaterial(session, name);
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Material name", exception.Message); }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { await dialogs.MessageAsync("Cannot read material image", exception.Message); }
    }

}
