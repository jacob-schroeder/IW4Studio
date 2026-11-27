using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MapConverter;
using MapConverter.CommandLine;

namespace IW4.Studio.Desktop.Views;

public sealed partial class MapConverterWindow : Window
{
    private CancellationTokenSource? _conversion;
    private bool _closeAfterConversion;

    public MapConverterWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        PlatformPicker.SelectionChanged += (_, _) => UpdatePlatformInputs();
        UpdatePlatformInputs();
    }

    private SourcePlatform? SelectedPlatform =>
        PlatformPicker.SelectedItem is ComboBoxItem { Tag: SourcePlatform platform } ? platform : null;

    private void UpdatePlatformInputs()
    {
        PcInputs.IsVisible = SelectedPlatform == SourcePlatform.Pc;
        Ps3Inputs.IsVisible = SelectedPlatform == SourcePlatform.Ps3;
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextBox field })
            return;

        try
        {
            IStorageItem? selected;
            if (field == MapPath || field == LoadPath || field == IwdPath)
            {
                bool archive = field == IwdPath;
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = archive ? "Select the map’s texture archive" :
                        field == MapPath ? "Select the map fastfile" : "Select the load fastfile",
                    FileTypeFilter = [new FilePickerFileType(archive ? "IWD archive" : "Fastfile")
                        { Patterns = [archive ? "*.iwd" : "*.ff"] }]
                });
                selected = files.FirstOrDefault();
            }
            else
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = field == OutputPath ? "Choose where to save the converted map" :
                        field == Ps3StringsPath ? "Select the localization folder" : "Select the source folder"
                });
                selected = folders.FirstOrDefault();
            }

            if (selected is not null)
                field.Text = selected.TryGetLocalPath()
                    ?? throw new IOException("Map Converter requires a local file or folder.");
        }
        catch (Exception error)
        {
            ShowReport("Could not select a path.", error.ToString());
        }
    }

    private async void Convert_Click(object? sender, RoutedEventArgs e)
    {
        if (_conversion is not null ||
            SourceGamePicker.SelectedItem is not ComboBoxItem { Tag: SourceGame game } ||
            SelectedPlatform is not { } platform)
            return;

        if (!ReadRequiredText(MapPath, "Choose the map fastfile.", out string map) ||
            !ReadRequiredText(LoadPath, "Choose the load fastfile.", out string load) ||
            !ReadRequiredText(OutputPath, "Choose a destination folder.", out string output))
            return;

        (string Title, string Description)? uiTextOverride = null;
        if (OverwriteUi.IsChecked == true)
        {
            if (!ReadRequiredText(MapDisplayName, "Enter a map name.", out string title) ||
                !ReadRequiredText(MapDescription, "Enter a map description.", out string description))
                return;
            uiTextOverride = (title, description);
        }

        using var cancellation = new CancellationTokenSource();
        try
        {
            map = map.Trim();
            bool ps3 = platform == SourcePlatform.Ps3;
            string? sourceLibrary = OptionalPath(ps3 ? Ps3StringsPath : PcLibraryPath);
            var options = new MapConverterOptions(game, platform, map, load.Trim(),
                IwdPath: ps3 ? null : OptionalPath(IwdPath),
                SourceFastFileDirectory: ps3 ? OptionalPath(Ps3StockPath) ?? Path.GetDirectoryName(Path.GetFullPath(map)) : null,
                SourceLibraryDirectory: sourceLibrary,
                BootstrapFastFilePath: null, ImageFileIndex: -1,
                OutputDirectory: output.Trim(), WorldTemplatePath: null, UiTextOverride: uiTextOverride);

            _conversion = cancellation;
            SetRunning(true);
            string name = Path.GetFileNameWithoutExtension(map);
            ShowReport($"Converting {name}…");
            MapConversionReport result = await Task.Run(
                () => MapConversion.ConvertAsync(options, cancellation.Token), cancellation.Token);
            ShowReport(result.WarningLines.Count == 0 ? $"Converted {name}." : $"Converted {name} with warnings.",
                string.Join(Environment.NewLine, result.OutputLines.Concat(result.WarningLines)));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ShowReport("Conversion canceled.");
        }
        catch (Exception error)
        {
            ShowReport("Conversion failed.", error.ToString());
        }
        finally
        {
            _conversion = null;
            SetRunning(false);
            if (_closeAfterConversion)
                Close();
        }
    }

    private static string? OptionalPath(TextBox field) =>
        string.IsNullOrWhiteSpace(field.Text) ? null : field.Text.Trim();

    private bool ReadRequiredText(TextBox field, string message, out string value)
    {
        value = field.Text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(value))
            return true;
        ShowReport(message);
        field.Focus();
        return false;
    }

    private void ShowReport(string summary, string? details = null)
    {
        Status.Text = summary;
        Report.Text = details;
        Report.IsVisible = !string.IsNullOrEmpty(details);
    }

    private void SetRunning(bool running)
    {
        Inputs.IsEnabled = ConvertButton.IsEnabled = !running;
        CancelConversion.IsVisible = CancelConversion.IsEnabled = running;
        Progress.IsVisible = Progress.IsIndeterminate = running;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => RequestCancellation();

    private void RequestCancellation()
    {
        CancelConversion.IsEnabled = false;
        Status.Text = "Canceling conversion…";
        _conversion?.Cancel();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_conversion is not null)
        {
            e.Cancel = true;
            _closeAfterConversion = true;
            RequestCancellation();
        }
        base.OnClosing(e);
    }
}
