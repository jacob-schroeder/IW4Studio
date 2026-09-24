using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Iw4Radiant.Compilation;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MapBuildWindow : Window
{
    private readonly MapDocument? _document;
    private readonly string? _sourcePath;
    private readonly string? _bspPath;
    private readonly IReadOnlyDictionary<string, MaterialSource>? _materials;
    private readonly IReadOnlyDictionary<string, XModelSource>? _models;
    private readonly Func<SelectionPath, bool>? _navigate;
    private CancellationTokenSource? _buildCancellation;
    private SelectionPath? _errorLocation;

    public MapBuildWindow()
    {
        InitializeComponent();
        BuildButton.IsEnabled = false;
        ScopeText.Text = MapCompiler.Scope;
        Closing += (_, e) =>
        {
            if (_buildCancellation is null) return;
            e.Cancel = true;
            CancelBuild();
        };
    }

    private MapBuildWindow(MapDocument document,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models) : this()
    {
        _document = document;
        _materials = materials;
        _models = models;
        GameModesText.Text = "Game modes: " + MapCompiler.GetGameModeSummary(document);
        GameModesText.IsVisible = true;
    }

    internal MapBuildWindow(MapDocument document, string bspPath,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        string? sourcePath, Func<SelectionPath, bool> navigate)
        : this(document, materials, models)
    {
        _navigate = navigate;
        _bspPath = bspPath;
        _sourcePath = sourcePath;
        Title = "Build .d3dbsp";
        MinWidth = 500;
        MinHeight = 320;
        Width = 600;
        Height = 420;
        SourceName.Text = Path.GetFileName(bspPath);
        BuildInputs.IsVisible = BuildButton.IsVisible = false;
        Opened += (_, _) => Build_Click(this, new RoutedEventArgs());
    }

    internal MapBuildWindow(MapDocument document, string sourcePath,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        string linkerPath, string emitterAssetDirectory, string outputFolder,
        Func<SelectionPath, bool> navigate) : this(document, materials, models)
    {
        _navigate = navigate;
        _sourcePath = sourcePath;
        SourceName.Text = Path.GetFileName(sourcePath);
        LinkerPathBox.Text = linkerPath;
        EmitterAssetDirectoryBox.Text = emitterAssetDirectory;
        OutputFolderBox.Text = outputFolder;
        BuildButton.IsEnabled = true;
    }

    internal string? CompletedDirectory { get; private set; }
    internal string? CompletedBspPath { get; private set; }
    internal bool PreviewRequested { get; private set; }
    internal string LinkerPath => LinkerPathBox.Text?.Trim() ?? "";
    internal string EmitterAssetDirectory => EmitterAssetDirectoryBox.Text?.Trim() ?? "";
    internal string OutputFolder => OutputFolderBox.Text?.Trim() ?? "";

    private async void BrowseLinker_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select D3dbspLinker", AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("D3dbspLinker") { Patterns = ["D3dbspLinker", "D3dbspLinker.exe", "D3dbspLinker.dll"] },
                FilePickerFileTypes.All
            ]
        });
        if (files.Count != 0 && files[0].TryGetLocalPath() is { } path) LinkerPathBox.Text = path;
    }

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select map build output folder", AllowMultiple = false
        });
        if (folders.Count != 0 && folders[0].TryGetLocalPath() is { } path) OutputFolderBox.Text = path;
    }

    private async void BrowseEmitterAssets_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select raw asset library", AllowMultiple = false
        });
        if (folders.Count != 0 && folders[0].TryGetLocalPath() is { } path)
            EmitterAssetDirectoryBox.Text = path;
    }

    private async void Build_Click(object? sender, RoutedEventArgs e)
    {
        if (_buildCancellation is not null || CompletedDirectory is not null || CompletedBspPath is not null ||
            _document is null || (_sourcePath is null && _bspPath is null) || _materials is null || _models is null) return;
        using var cancellation = new CancellationTokenSource();
        _buildCancellation = cancellation;
        BuildInputs.IsEnabled = BuildButton.IsEnabled = false;
        CloseButton.Content = "Cancel build";
        BuildStatus.Text = "Building…";
        ProgressOutput.Text = "";
        ErrorLocation.IsVisible = false;
        ShowErrorButton.IsEnabled = true;
        _errorLocation = null;
        try
        {
            var progress = new Progress<string>(AppendProgress);
            if (_bspPath is { } bspPath)
            {
                AppendProgress("Compiling geometry and collision; baking sunlight, local lights and reflections…");
                await MapBuildPipeline.BuildBspAsync(_document, bspPath, _materials, _models, cancellation.Token, _sourcePath);
                CompletedBspPath = bspPath;
            }
            else if (_sourcePath is { } sourcePath)
                CompletedDirectory = await MapBuildPipeline.BuildAsync(_document, sourcePath, _materials, _models,
                    LinkerPath, EmitterAssetDirectory, OutputFolder,
                    progress, cancellation.Token);
            BuildStatus.Text = "Build complete";
            AppendProgress($"Build complete: {CompletedBspPath ?? CompletedDirectory}");
            BuildButton.IsVisible = false;
            PreviewButton.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            BuildStatus.Text = "Build cancelled";
            AppendProgress("The build was cancelled.");
        }
        catch (Exception exception)
        {
            BuildStatus.Text = "Build failed";
            AppendProgress(exception.Message);
            if (exception is MapBuildLocationException located &&
                ReferenceEquals(located.Document, _document) && _navigate is not null)
            {
                _errorLocation = located.Location;
                MapEntity entity = located.Document.Entities[located.Location.Entity];
                string owner = entity.ClassName == "worldspawn" ? "the world" : entity.ClassName;
                int terrainIndex = located.Location.Terrain;
                bool isCurve = terrainIndex >= 0 && entity.Terrains[terrainIndex].IsCurve;
                int surfaceNumber = terrainIndex >= 0
                    ? entity.Terrains.Take(terrainIndex + 1).Count(surface => surface.IsCurve == isCurve)
                    : 0;
                ErrorLocationText.Text = located.Location.Brush >= 0
                    ? $"Brush {located.Location.Brush + 1} in {owner}"
                    : terrainIndex >= 0
                        ? $"{(isCurve ? "Curve" : "Terrain")} {surfaceNumber} in {owner}"
                        : $"{entity.ClassName} entity";
                ErrorLocation.IsVisible = true;
            }
        }
        finally
        {
            _buildCancellation = null;
            BuildInputs.IsEnabled = BuildButton.IsEnabled = CompletedDirectory is null && CompletedBspPath is null;
            CloseButton.IsEnabled = true;
            CloseButton.Content = "Close";
        }
    }

    private void Preview_Click(object? sender, RoutedEventArgs e)
    {
        if (CompletedBspPath is null && CompletedDirectory is null) return;
        PreviewRequested = true;
        Close();
    }

    private void AppendProgress(string message)
    {
        ProgressOutput.Text += message + Environment.NewLine;
        ProgressOutput.CaretIndex = ProgressOutput.Text.Length;
    }

    private void ShowError_Click(object? sender, RoutedEventArgs e)
    {
        if (_errorLocation is not { } location || _navigate is null) return;
        if (_navigate(location)) Close();
        else
        {
            ShowErrorButton.IsEnabled = false;
            ErrorLocationText.Text = "This object is no longer selectable. Check its visibility and rebuild.";
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (_buildCancellation is not null) CancelBuild();
        else Close();
    }

    private void CancelBuild()
    {
        _buildCancellation?.Cancel();
        BuildStatus.Text = "Cancelling…";
        CloseButton.Content = "Cancelling…";
        CloseButton.IsEnabled = false;
    }
}
