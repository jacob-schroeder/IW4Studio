using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Iw4Radiant.Compilation;
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
    private readonly ObservableCollection<string> _providers = [];
    private CancellationTokenSource? _buildCancellation;

    public MapBuildWindow()
    {
        InitializeComponent();
        BuildButton.IsEnabled = false;
        ScopeText.Text = MapCompiler.Scope;
        ProviderList.ItemsSource = _providers;
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
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models, string? sourcePath)
        : this(document, materials, models)
    {
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
        string linkerPath, string templatePath,
        IReadOnlyList<string> providerPaths, string outputFolder) : this(document, materials, models)
    {
        _sourcePath = sourcePath;
        SourceName.Text = Path.GetFileName(sourcePath);
        LinkerPathBox.Text = linkerPath;
        TemplatePathBox.Text = templatePath;
        OutputFolderBox.Text = outputFolder;
        foreach (string path in providerPaths) _providers.Add(path);
        BuildButton.IsEnabled = true;
    }

    internal string? CompletedDirectory { get; private set; }
    internal string? CompletedBspPath { get; private set; }
    internal string LinkerPath => LinkerPathBox.Text?.Trim() ?? "";
    internal string TemplatePath => TemplatePathBox.Text?.Trim() ?? "";
    internal string OutputFolder => OutputFolderBox.Text?.Trim() ?? "";
    internal IReadOnlyList<string> ProviderPaths => _providers.ToArray();

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

    private async void BrowseTemplate_Click(object? sender, RoutedEventArgs e)
    {
        var files = await PickFastFilesAsync("Select official PS3 bootstrap fastfile", false);
        if (files.Count != 0 && files[0].TryGetLocalPath() is { } path) TemplatePathBox.Text = path;
    }

    private async void AddProvider_Click(object? sender, RoutedEventArgs e)
    {
        var files = await PickFastFilesAsync("Add provider PS3 fastfiles", true);
        foreach (IStorageFile file in files)
            if (file.TryGetLocalPath() is { } path && !_providers.Contains(path, StringComparer.Ordinal))
                _providers.Add(path);
    }

    private Task<IReadOnlyList<IStorageFile>> PickFastFilesAsync(string title, bool multiple) =>
        StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = multiple,
            FileTypeFilter = [new FilePickerFileType("PS3 fastfiles") { Patterns = ["*.ff"] }]
        });

    private void RemoveProvider_Click(object? sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is string path) _providers.Remove(path);
    }

    private void Provider_Changed(object? sender, SelectionChangedEventArgs e) =>
        RemoveProviderButton.IsEnabled = ProviderList.SelectedItem is string;

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select map build output folder", AllowMultiple = false
        });
        if (folders.Count != 0 && folders[0].TryGetLocalPath() is { } path) OutputFolderBox.Text = path;
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
                    LinkerPath, TemplatePath, ProviderPaths, OutputFolder, progress, cancellation.Token);
            BuildStatus.Text = "Build complete";
            AppendProgress($"Build complete: {CompletedBspPath ?? CompletedDirectory}");
            BuildButton.IsVisible = false;
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
        }
        finally
        {
            _buildCancellation = null;
            BuildInputs.IsEnabled = BuildButton.IsEnabled = CompletedDirectory is null && CompletedBspPath is null;
            CloseButton.IsEnabled = true;
            CloseButton.Content = "Close";
        }
        if (CompletedBspPath is not null) Close();
    }

    private void AppendProgress(string message)
    {
        ProgressOutput.Text += message + Environment.NewLine;
        ProgressOutput.CaretIndex = ProgressOutput.Text.Length;
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
