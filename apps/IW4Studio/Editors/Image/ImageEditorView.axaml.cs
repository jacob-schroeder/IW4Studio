using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace IW4.Studio.Desktop.Editors.Image;

public sealed partial class ImageEditorView : UserControl
{
    private static readonly FilePickerFileType PngFiles = new("PNG image")
    {
        Patterns = ["*.png", "*.PNG"],
        MimeTypes = ["image/png"],
        AppleUniformTypeIdentifiers = ["public.png"]
    };
    private bool _compact;

    public ImageEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        bool narrow = e.NewSize.Width < 660;
        if (narrow == _compact || Panels is null)
            return;
        _compact = narrow;
        Panels.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,290");
        Panels.RowDefinitions = new RowDefinitions(narrow ? "3*,2*" : "*,0");
        Panels.RowSpacing = narrow ? 8 : 0;
        Grid.SetRow(Inspector, narrow ? 1 : 0);
        Grid.SetColumn(Inspector, narrow ? 0 : 1);
    }

    private void Revert_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ImageEditorViewModel)?.RevertChanges();

    private void Apply_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as ImageEditorViewModel)?.ApplyChanges();

    private void Checker_Click(object? sender, RoutedEventArgs e) =>
        Viewport.TransparencyBrush = CheckerToggle.IsChecked == true
            ? (IBrush?)Resources["Checkerboard"] : null;

    private async void Replace_Click(object? sender, RoutedEventArgs e) =>
        await ReviewReplacementAsync(null);

    private void DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DataContext is ImageEditorViewModel { CanReplace: true } && GetDroppedPng(e) is not null
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (GetDroppedPng(e) is { } png)
            await ReviewReplacementAsync(png);
    }

    private static IStorageFile? GetDroppedPng(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        return files is [IStorageFile file] &&
            string.Equals(Path.GetExtension(file.Name), ".png", StringComparison.OrdinalIgnoreCase)
            ? file : null;
    }

    private async Task ReviewReplacementAsync(IStorageFile? file)
    {
        if (DataContext is not ImageEditorViewModel { CanReplace: true } vm ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;
        vm.SetFileOperation(true);
        try
        {
            if (file is null)
            {
                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Replace Image from PNG",
                    FileTypeFilter = [PngFiles],
                    AllowMultiple = false
                });
                file = files.FirstOrDefault();
            }
            if (file is null || vm.IsDisposed)
                return;

            await using Stream input = await file.OpenReadAsync();
            using var review = await vm.PrepareReplacementAsync(input, file.Name);
            if (vm.IsDisposed || TopLevel.GetTopLevel(this) != owner)
                return;
            var dialog = new ImageReplacementWindow { DataContext = review };
            if (await dialog.ShowDialog<bool>(owner) && !vm.IsDisposed)
                vm.StageReplacement(review);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            vm.ReportStatus($"Replacement failed: {ex.Message}");
        }
        finally
        {
            if (!vm.IsDisposed)
                vm.SetFileOperation(false);
        }
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImageEditorViewModel { CanExport: true } vm ||
            vm.FullResolutionMip is not { } level || TopLevel.GetTopLevel(this) is not { } top)
            return;
        vm.SetFileOperation(true);
        try
        {
            string name = Path.GetFileName(vm.Name.Replace('\\', '/')).Replace('*', '_');
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export full-resolution PNG",
                SuggestedFileName = SaveFilePickerName.WithoutDefaultExtension(name, "png"),
                DefaultExtension = "png",
                FileTypeChoices = [PngFiles],
                ShowOverwritePrompt = true
            });
            if (file is null || vm.IsDisposed)
                return;
            byte[] bytes = await Task.Run(() => ImageEditorPixels.EncodePng(level));
            await AssetExportFile.WriteAsync(file, bytes);
            vm.ReportStatus($"Exported full-resolution PNG to {file.TryGetLocalPath() ?? file.Name}.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            vm.ReportStatus($"PNG export failed: {ex.Message}");
        }
        finally
        {
            if (!vm.IsDisposed)
                vm.SetFileOperation(false);
        }
    }
}
