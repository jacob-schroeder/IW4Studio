using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Material;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private string? _lastBspPath;
    private MapDocument? _lastBspSource;
    private string? _lastBspSourcePath;
    private long _lastBspRevision;
    private DateTime _lastBspWriteTimeUtc;
    private string? _previewBspPath;
    private string? _previewAssetNotice;
    private string? _previewLightmapNotice;
    private IReadOnlyDictionary<string, MaterialSource> _compiledWaterMaterials =
        new Dictionary<string, MaterialSource>(StringComparer.Ordinal);

    private void RememberBuiltBsp(string path, MapDocument source)
    {
        _lastBspPath = Path.GetFullPath(path);
        _lastBspSource = source;
        _lastBspSourcePath = _session.FilePath;
        _lastBspRevision = _session.ContentRevision;
        _lastBspWriteTimeUtc = File.GetLastWriteTimeUtc(_lastBspPath);
        UpdateCompiledPreviewState();
    }

    private async void PreviewLastBsp_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        if (_lastBspPath is not { } path)
        {
            SetStatus("No successful .d3dbsp build is available in this editor session. Build one or open a .d3dbsp preview.");
            await _dialogs.MessageAsync("No compiled map", "Build a .d3dbsp first, or choose Open .d3dbsp preview.");
            return;
        }
        await ShowBspPreviewAsync(path);
    }

    private async void OpenBspPreview_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        var files = await _dialogs.ShowModalAsync(() => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open compiled .d3dbsp preview", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Compiled IW4 map") { Patterns = ["*.d3dbsp"] }]
        }));
        if (files.Count == 0) return;
        if (files[0].TryGetLocalPath() is not { } path)
        {
            await _dialogs.MessageAsync("Cannot preview .d3dbsp", "Choose a local .d3dbsp file.");
            return;
        }
        await ShowBspPreviewAsync(path);
    }

    private async Task ShowBspPreviewAsync(string path)
    {
        FinishGestures();
        try
        {
            _dialogs.SetBusy(true);
            CompiledBspPreview preview = await Task.Run(() => CompiledBspPreview.Read(path));
            var modelSources = preview.ModelNames.ToDictionary(name => name,
                ResolveSceneModel, StringComparer.Ordinal);
            await Task.Run(() => preview.ResolveModels(name => modelSources.GetValueOrDefault(name)));
            (_compiledWaterMaterials, _previewAssetNotice) = ResolveCompiledPreviewMaterials(preview);
            _previewLightmapNotice =
                $"Baked diffuse: {preview.MissingSurfaceCount} surfaces missing lightmap tiles, " +
                $"{preview.UnsupportedSurfaceCount} with unsupported encoding/UVs, " +
                $"{preview.UnlightmappedSurfaceCount} intentionally unlightmapped. " +
                $"{preview.DirectSunOmittedSurfaceCount} baked surfaces show diffuse only. " +
                "Missing/unsupported lightmaps have a magenta material fallback." +
                (preview.LightmapDataError is null ? "" : $" Lightmap data: {preview.LightmapDataError}") +
                (preview.SunDataError is null ? "" : $" Direct sun: {preview.SunDataError}");
            _previewBspPath = Path.GetFullPath(path);
            Workspace.SetCompiledPreview(preview);
            CompiledPreviewBanner.IsVisible = true;
            EditorMenu.IsEnabled = EditorToolbar.IsEnabled = false;
            EditorMenu.IsVisible = EditorToolbar.IsVisible = false;
            RefreshLayoutControls();
            UpdateCompiledPreviewState();
            UpdateCompiledPreviewRenderStatus();
            SetStatus($"Read-only compiled BSP preview: {Path.GetFileName(path)}. Return to source map to edit.");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            SetStatus($"Cannot preview .d3dbsp: {exception.Message}");
            _dialogs.SetBusy(false);
            await _dialogs.MessageAsync("Cannot preview .d3dbsp", exception.Message);
        }
        finally { _dialogs.SetBusy(false); }
    }

    private void ReturnToSource_Click(object? sender, RoutedEventArgs e)
    {
        if (_previewBspPath is null || _dialogs.BlocksInput) return;
        _previewBspPath = null;
        _previewAssetNotice = null;
        _previewLightmapNotice = null;
        _compiledWaterMaterials = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
        Workspace.SetCompiledPreview(null);
        CompiledPreviewBanner.IsVisible = false;
        EditorMenu.IsEnabled = EditorToolbar.IsEnabled = true;
        EditorMenu.IsVisible = EditorToolbar.IsVisible = true;
        RefreshLayoutControls();
        RefreshEditor();
        Workspace.FocusActiveView();
        SetStatus("Returned to editable source map.");
    }

    private void UpdateCompiledPreviewState()
    {
        if (_previewBspPath is not { } path) return;
        string relation;
        if (_lastBspPath is not null &&
            string.Equals(path, _lastBspPath, StringComparison.OrdinalIgnoreCase))
        {
            bool sourceChanged = !ReferenceEquals(_session.Document, _lastBspSource) ||
                _session.ContentRevision != _lastBspRevision ||
                !string.Equals(_session.FilePath, _lastBspSourcePath, StringComparison.Ordinal);
            bool outputChanged = !File.Exists(path) || File.GetLastWriteTimeUtc(path) != _lastBspWriteTimeUtc;
            relation = sourceChanged || outputChanged
                ? "STALE: source map or compiled file changed since the successful build."
                : "Last successful build of the current source map.";
        }
        else relation = "Opened BSP file; its relationship to the current source map is unknown.";
        CompiledPreviewState.Text = $"{Path.GetFileName(path)} · {relation}";
        Title = $"{(_session.IsDirty ? "*" : "")}{Path.GetFileName(_session.FilePath ?? "Untitled.map")} — compiled BSP preview — Iw4Radiant";
    }

    private void UpdateCompiledPreviewRenderStatus()
    {
        if (_previewBspPath is null) return;
        string? renderer = Workspace.Camera.RendererError;
        CompiledPreviewAssets.Text = string.Join("\n", new[]
            { _previewAssetNotice, Workspace.Camera.CompiledPreview?.ModelAssetNotice, _previewLightmapNotice, renderer }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private void OnCompiledPreviewRendererStatus(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateCompiledPreviewRenderStatus);

    private (IReadOnlyDictionary<string, MaterialSource> Materials, string? Notice)
        ResolveCompiledPreviewMaterials(CompiledBspPreview preview)
    {
        var generated = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
        var missing = new List<string>();
        var errors = new List<string>();
        foreach (string name in preview.Batches.Select(batch => batch.Material).Distinct(StringComparer.Ordinal))
        {
            if (!WaterMaterialAuthoring.IsAuthoredMaterialName(name))
            {
                if (Workspace.Materials.ResolveMaterial(name) is null &&
                    Workspace.Models.ResolveMaterial(name) is null && ResolveEmitterMaterial(name) is null)
                    missing.Add(name);
                continue;
            }
            if (!preview.WaterDefinitions.TryGetValue(name, out WaterMaterialDefinition? definition))
            {
                missing.Add(name);
                continue;
            }
            try
            {
                if (CreateAuthoredWaterMaterial(definition) is { } material)
                    generated.Add(name, material with
                    {
                        // WaterMaterialAuthoring.CreateSurfaceStates applies this authored
                        // two-sided, depth-writing surface profile to the compiled material.
                        Surface = material.Surface with
                        {
                            BlendOperation = GfxBlendOperation.Add,
                            Source = GfxBlend.SourceAlpha,
                            Destination = GfxBlend.InverseSourceAlpha,
                            DepthWrite = true,
                            CullFace = GfxCullFace.None,
                            AlphaTest = null,
                            SortKey = (int)WaterMaterialAuthoring.SurfaceSortKey
                        }
                    });
                else missing.Add(name);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or
                                              ArgumentException or FormatException or OverflowException)
            {
                missing.Add(name);
                errors.Add($"{name}: {exception.Message}");
            }
        }
        string notice = string.Join(" ", new[]
        {
            missing.Count == 0 ? null :
                $"Missing local material assets ({missing.Count}): {string.Join(", ", missing.Take(5))}" +
                (missing.Count > 5 ? ", …" : "") + ". DEFAULT images will be used.",
            preview.WaterDataError is null ? null : $"Compiled water definitions: {preview.WaterDataError}",
            errors.Count == 0 ? null : $"Unsupported compiled water: {string.Join("; ", errors)}"
        }.OfType<string>());
        return (generated, notice.Length == 0 ? null : notice);
    }

    private void RefreshCompiledPreviewMaterials()
    {
        if (_previewBspPath is null || Workspace.Camera.CompiledPreview is not { } preview) return;
        (_compiledWaterMaterials, _previewAssetNotice) = ResolveCompiledPreviewMaterials(preview);
        UpdateCompiledPreviewRenderStatus();
    }
}
