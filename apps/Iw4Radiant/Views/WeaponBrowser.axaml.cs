using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class WeaponBrowser : UserControl
{
    private XModelSource? _model;
    private IReadOnlyDictionary<string, MaterialSource> _materials = new Dictionary<string, MaterialSource>();
    private WeaponThumbnail? _thumbnail;
    private int _loadRevision;

    public WeaponBrowser() => InitializeComponent();

    internal event Action? PlacementRequested;
    internal event Action? AssetsChanged;

    internal XModelSource? ResolveModel(string name) =>
        name == GameplayEntityEditing.TurretModelName ? _model : null;

    internal MaterialSource? ResolveMaterial(string name) => _materials.GetValueOrDefault(name);

    internal void InitializeActions(EditorDialogs dialogs, Action finishGestures)
    {
        WeaponList.ItemsSource = new[] { new WeaponThumbnail(null, "Loading mounted minigun preview…") };
        WeaponList.SelectedIndex = 0;
        WeaponList.AddHandler(PointerPressedEvent, async (_, e) =>
        {
            if (dialogs.BlocksInput || !e.GetCurrentPoint(WeaponList).Properties.IsLeftButtonPressed) return;
            bool pressedTile = false;
            for (Control? current = e.Source as Control; current is not null && !ReferenceEquals(current, WeaponList);
                 current = current.Parent as Control)
                if (current.DataContext is WeaponThumbnail) { pressedTile = true; break; }
            if (!pressedTile) return;
            finishGestures();
            try { await DragDrop.DoDragDropAsync(e, WeaponDrag.Create(), DragDropEffects.Copy); }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
            { StatusText.Text = exception.Message; }
        }, handledEventsToo: true);
        PlaceButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures();
            PlacementRequested?.Invoke();
        };
    }

    internal async Task LoadAssetsAsync(string bootstrapRoot)
    {
        int revision = ++_loadRevision;
        StatusText.Text = "Loading mounted minigun preview…";
        XModelSource? model = null;
        Dictionary<string, MaterialSource> materials = new(StringComparer.Ordinal);
        Bitmap? preview = null;
        string status;
        try
        {
            var loaded = await Task.Run(() =>
            {
                var assets = new NativeModelPreviewAssets(bootstrapRoot);
                XModelSource source = assets.LoadSource(GameplayEntityEditing.TurretModelName);
                var sceneMaterials = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
                string? materialError = null;
                foreach (var material in source.Document.Materials)
                {
                    try
                    {
                        if (MaterialCatalog.ReadOne(bootstrapRoot, material.Name) is { } resolved)
                            sceneMaterials[material.Name] = resolved;
                        else materialError ??= $"Material '{material.Name}' has no usable source.";
                    }
                    catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                    { materialError ??= exception.Message; }
                }
                Bitmap? image = null;
                string? previewError = null;
                try { image = new XModelPreviewRenderer(assets.ResolveTexture).Render(source, 144); }
                catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
                { previewError = exception.Message; }
                return (source, sceneMaterials, image, previewError, materialError);
            });
            model = loaded.source;
            materials = loaded.sceneMaterials;
            preview = loaded.image;
            status = loaded.previewError is null
                ? "Drag onto a camera surface or grid, or use Place mounted minigun."
                : $"Preview unavailable: {loaded.previewError} You can still place the mounted minigun.";
            if (loaded.materialError is not null)
                status += $" Scene material unavailable: {loaded.materialError}";
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            status = $"Mounted minigun assets unavailable: {exception.Message} You can still place the point entity.";
        }
        if (revision != _loadRevision)
        {
            preview?.Dispose();
            return;
        }
        _model = model;
        _materials = materials;
        _thumbnail?.Preview?.Dispose();
        _thumbnail = new WeaponThumbnail(preview, status);
        WeaponList.ItemsSource = new[] { _thumbnail };
        WeaponList.SelectedIndex = 0;
        StatusText.Text = status;
        AssetsChanged?.Invoke();
    }

    internal void ReleaseImages()
    {
        _loadRevision++;
        WeaponList.ItemsSource = null;
        _thumbnail?.Preview?.Dispose();
        _thumbnail = null;
        _model = null;
        _materials = new Dictionary<string, MaterialSource>();
    }
}

internal sealed record WeaponThumbnail(Bitmap? Preview, string Status)
{
    public string Name => "Mounted minigun";
    public bool IsPreviewUnavailable => Preview is null;
}
