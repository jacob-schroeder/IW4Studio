using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using IW4.Studio.Desktop.Editors.Menu;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Font;

public sealed partial class FontViewerView : UserControl
{
    private bool _isImportInProgress;

    public FontViewerView() => AvaloniaXamlLoader.Load(this);

    private async void ReplaceFontButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isImportInProgress ||
            DataContext is not FontViewerViewModel viewModel ||
            !viewModel.CanReplace)
        {
            return;
        }

        _isImportInProgress = true;
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                viewModel.ReportReplacementFailure(
                    "The desktop file picker is unavailable.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Replace IW4 Font from OpenType",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("OpenType fonts")
                        {
                            Patterns = ["*.ttf", "*.TTF", "*.otf", "*.OTF"]
                        },
                        new FilePickerFileType("TrueType fonts")
                        {
                            Patterns = ["*.ttf", "*.TTF"]
                        },
                        new FilePickerFileType("OpenType/CFF fonts")
                        {
                            Patterns = ["*.otf", "*.OTF"]
                        }
                    ]
                });
            IStorageFile? file = files.FirstOrDefault();
            if (file is null)
                return;

            byte[] sourceBytes;
            await using (Stream stream = await file.OpenReadAsync())
            {
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                sourceBytes = memory.ToArray();
            }

            FontReplacementCandidate candidate = await Task.Run(() =>
                viewModel.CompileReplacement(sourceBytes));
            _ = viewModel.TryStageReplacement(
                candidate,
                file.TryGetLocalPath() ?? file.Name,
                out _);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            viewModel.ReportReplacementFailure(exception.Message);
        }
        finally
        {
            _isImportInProgress = false;
        }
    }

    private void ApplyFontButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        (DataContext as FontViewerViewModel)?.ApplyCompiledDraft();

    private void RevertFontButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        (DataContext as FontViewerViewModel)?.RevertDraft();

    private void ResetPreviewButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        (DataContext as FontViewerViewModel)?.ResetPreviewText();

    private void PreviewControl_MaterialResolutionCompleted(
        object? sender,
        MenuPreviewMaterialResolutionCompletedEventArgs e) =>
        (DataContext as FontViewerViewModel)?
            .ReportMaterialPreviewStatus(e.Status);

    private void PreviewControl_TextResolutionCompleted(
        object? sender,
        MenuPreviewTextResolutionCompletedEventArgs e) =>
        (DataContext as FontViewerViewModel)?
            .ReportTextPreviewStatus(e.Status);
}
