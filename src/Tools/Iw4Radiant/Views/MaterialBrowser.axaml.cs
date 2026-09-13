using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class MaterialBrowser : UserControl
{
    private readonly Dictionary<string, string> _materials = new(StringComparer.Ordinal);
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
    internal string? ResolveTexturePath(string name) => _materials.GetValueOrDefault(name);

    internal void ReleasePreview()
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
            var materials = await Task.Run(() => MaterialCatalog.Read(root));
            _materials.Clear();
            foreach (var material in materials) _materials.Add(material.Key, material.Value);
            session.Material = "";
            MaterialName.Text = "";
            MaterialFilter.Text = "";
            FilterMaterials();
            CatalogChanged?.Invoke();
            setStatus($"Loaded materials from {root}.");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { dialogs.SetBusy(false); await dialogs.MessageAsync("Cannot read materials", exception.Message); }
        finally { dialogs.SetBusy(false); }
    }
    private void FilterMaterials()
    {
        string filter = MaterialFilter.Text ?? "";
        string[] matches = _materials.Keys.Where(name => name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        MaterialList.ItemsSource = matches.Take(2000).ToArray();
        MaterialInfo.Text = $"{_materials.Count} available materials" +
            (matches.Length > 2000 ? " · showing first 2,000; filter to narrow" : "");
    }
    private void PreviewMaterial(EditorSession session)
    {
        ReleasePreview();
        PreviewInfo.Text = "Choose an available material to preview it.";
        if (MaterialList.SelectedItem is not string name || !_materials.TryGetValue(name, out string? path)) return;
        MaterialName.Text = name;
        session.Material = "";
        try
        {
            _preview = MaterialImages.Load(path, 256);
            MaterialPreview.Source = _preview;
            PreviewInfo.Text = Path.GetFileName(path);
            session.Material = name;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { PreviewInfo.Text = exception.Message; }
    }
    private async Task ApplyMaterialAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        finishGestures();
        try
        {
            string name = (MaterialName.Text ?? "").Trim();
            if (!_materials.TryGetValue(name, out string? path) || !File.Exists(path))
                throw new ArgumentException("Choose a material with an available image from the browser.");
            using var image = MaterialImages.Load(path, 256);
            session.ApplyMaterial(name);
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Material name", exception.Message); }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { await dialogs.MessageAsync("Cannot read material image", exception.Message); }
    }

}
