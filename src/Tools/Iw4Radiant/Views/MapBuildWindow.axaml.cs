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

    internal MapBuildWindow(MapDocument document, string sourcePath,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        string linkerPath, string templatePath,
        IReadOnlyList<string> providerPaths, string outputFolder) : this()
    {
        _document = document;
        _sourcePath = sourcePath;
        _materials = materials;
        _models = models;
        SourceName.Text = Path.GetFileName(sourcePath);
        GameModesText.Text = "Game modes: " + MapCompiler.GetGameModeSummary(document);
        GameModesText.IsVisible = true;
        LinkerPathBox.Text = linkerPath;
        TemplatePathBox.Text = templatePath;
        OutputFolderBox.Text = outputFolder;
        foreach (string path in providerPaths) _providers.Add(path);
        BuildButton.IsEnabled = true;
    }

    internal string? CompletedDirectory { get; private set; }
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
        if (_buildCancellation is not null || CompletedDirectory is not null ||
            _document is null || _sourcePath is null || _materials is null || _models is null) return;
        using var cancellation = new CancellationTokenSource();
        _buildCancellation = cancellation;
        BuildInputs.IsEnabled = BuildButton.IsEnabled = false;
        CloseButton.Content = "Cancel build";
        BuildStatus.Text = "Building…";
        ProgressOutput.Text = "";
        try
        {
            var progress = new Progress<string>(AppendProgress);
            CompletedDirectory = await MapBuildPipeline.BuildAsync(_document, _sourcePath, _materials, _models,
                LinkerPath, TemplatePath, ProviderPaths, OutputFolder, progress, cancellation.Token);
            BuildStatus.Text = "Build complete";
            AppendProgress($"Build complete: {CompletedDirectory}");
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
            BuildInputs.IsEnabled = BuildButton.IsEnabled = CompletedDirectory is null;
            CloseButton.IsEnabled = true;
            CloseButton.Content = "Close";
        }
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
