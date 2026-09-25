using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Zone;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class MaterialBrowser
{
    private Func<string?>? _findBootstrapDirectory;
    private readonly SemaphoreSlim _sourceReadGate = new(1);
    private CancellationTokenSource? _sourceReadCancellation;
    private string? _selectedSourcePath;

    private void ClearSourceReadiness()
    {
        _sourceReadCancellation?.Cancel();
        _sourceReadCancellation?.Dispose();
        _sourceReadCancellation = null;
        _selectedSourcePath = null;
        SourceReadinessRow.IsVisible = false;
        MaterialInfo.IsVisible = true;
    }

    private async Task RefreshSourceReadinessAsync()
    {
        ClearSourceReadiness();
        if (MaterialList.SelectedItem is not MaterialThumbnail selected) return;
        SourceReadinessRow.IsVisible = true;
        MaterialInfo.IsVisible = false;
        ShowSourceButton.IsEnabled = false;
        SourceReadiness.Text = "Checking source…";
        ToolTip.SetTip(SourceReadiness, null);
        if (ClipBrushMaterial.IsPlayerClip(selected.Name) || CaulkMaterial.IsCaulk(selected.Name))
        {
            SourceReadiness.Text = "Built in · collision surface";
            return;
        }
        if (_libraryRoot is not { } root)
        {
            SourceReadiness.Text = "Choose an asset library";
            return;
        }

        _sourceReadCancellation = new CancellationTokenSource();
        CancellationToken token = _sourceReadCancellation.Token;
        string? bootstrap = _findBootstrapDirectory?.Invoke();
        try
        {
            WaterMaterialDefinition? water = _session is { } session
                ? WaterMaterialAuthoring.ReadDefinitions(session.Document.World.Properties).GetValueOrDefault(selected.Name)
                : null;
            string name = water?.SourceMaterial ?? selected.Name;
            var result = await Task.Run(async () =>
            {
                // Read only the selected graph, off the UI thread. Superseded selections never queue disk work.
                await _sourceReadGate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    var sources = new MaterialSourceCompiler(root, bootstrap);
                    string path = sources.ResolveMaterialSourcePath(name);
                    try
                    {
                        sources.LoadMaterial(name);
                        string origin = Path.GetRelativePath(root, path).StartsWith("..", StringComparison.Ordinal)
                            ? "bootstrap" : Path.GetFileName(root);
                        return (Status: water is null ? $"Source ready · {origin}" : "Base source ready · map water",
                            Detail: $"Material and dependencies accepted by the PS3 source readers.\n{path}" +
                                (water is null ? "" : "\nWater settings are stored in this map."), Path: path);
                    }
                    catch (MaterialSourceException exception)
                    {
                        string kind = exception.AssetType switch
                        {
                            XAssetType.Material => "material",
                            XAssetType.Image => "image",
                            XAssetType.Techset => "technique set",
                            XAssetType.VertexShader => "vertex shader",
                            XAssetType.PixelShader => "pixel shader",
                            _ => "source"
                        };
                        string asset = exception.AssetName;
                        if (exception.SourcePath.Contains($"{Path.DirectorySeparatorChar}techniques{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                        {
                            kind = "technique";
                            asset = Path.GetFileName(exception.SourcePath).Replace(".technique.json", "", StringComparison.Ordinal);
                        }
                        string status = exception.IsMissing ? $"Missing {kind} · {asset}" : $"Needs update · {kind} {asset}";
                        if (exception.IsMissing && exception.AssetType == XAssetType.Material &&
                            !Directory.Exists(Path.Combine(root, "materials")) && selected.Preview is not null)
                            status = "Preview only · material source needed";
                        return (Status: status, Detail: $"{exception.GetBaseException().Message}\n{exception.SourcePath}\nMaterial: {path}",
                            Path: exception.SourcePath);
                    }
                }
                finally { _sourceReadGate.Release(); }
            }, token);
            if (token.IsCancellationRequested) return;
            SourceReadiness.Text = result.Status;
            ToolTip.SetTip(SourceReadiness, result.Detail);
            _selectedSourcePath = result.Path;
            ShowSourceButton.IsEnabled = true;
            ToolTip.SetTip(ShowSourceButton, result.Path);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            if (token.IsCancellationRequested) return;
            SourceReadiness.Text = "Source unavailable";
            ToolTip.SetTip(SourceReadiness, exception.Message);
        }
    }

    private async Task ShowSourceAsync()
    {
        if (_selectedSourcePath is not { } path || TopLevel.GetTopLevel(this) is not { } owner) return;
        try
        {
            DirectoryInfo? folder = new(Path.GetDirectoryName(path) ?? path);
            while (folder is not null && !folder.Exists) folder = folder.Parent;
            if (folder is not null && !await owner.Launcher.LaunchDirectoryInfoAsync(folder))
                _setStatus?.Invoke($"Cannot open source folder: {folder.FullName}");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            _setStatus?.Invoke($"Cannot open source folder: {exception.Message}");
        }
    }
}
