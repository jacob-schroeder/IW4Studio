using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IW4.Assets.XModel.Export;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.XModel;

public sealed partial class XModelEditorView : UserControl
{
    private readonly XModelPreviewControl? _preview;
    private readonly XModelBoneTagOverlay? _boneTagOverlay;
    private bool _isAttached;
    private bool _isExportInProgress;
    private bool _isImportInProgress;

    public XModelEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        _preview =
            this.FindControl<XModelPreviewControl>("Preview");
        Border? previewInputSurface =
            this.FindControl<Border>("PreviewInputSurface");
        _boneTagOverlay =
            this.FindControl<XModelBoneTagOverlay>("BoneTagOverlay");
        if (_preview is not null && previewInputSurface is not null)
            _preview.AttachCameraInput(previewInputSurface);
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_isAttached)
            return;

        _isAttached = true;
        if (_preview is not null)
        {
            _preview.RendererStatusChanged += Preview_RendererStatusChanged;
            _boneTagOverlay?.Attach(_preview);
        }
    }

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        if (_isAttached)
        {
            if (_preview is not null)
            {
                _preview.RendererStatusChanged -=
                    Preview_RendererStatusChanged;
            }
            _boneTagOverlay?.Detach();
            _isAttached = false;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void FitButton_Click(object? sender, RoutedEventArgs e) =>
        this.FindControl<XModelPreviewControl>("Preview")?.Fit();

    private void RevertButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as XModelEditorViewModel)?.RevertDraft();

    private async void AddLodButton_Click(object? sender, RoutedEventArgs e) =>
        await ImportLodAsync(replaceSelected: false);

    private async void ReplaceLodButton_Click(object? sender, RoutedEventArgs e) =>
        await ImportLodAsync(replaceSelected: true);

    private void RemoveLodButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as XModelEditorViewModel)?.RemoveSelectedAssemblyLod();

    private async Task ImportLodAsync(bool replaceSelected)
    {
        if (_isImportInProgress || DataContext is not XModelEditorViewModel viewModel)
            return;
        _isImportInProgress = true;
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                viewModel.ReportXModelExportStatus("XMODEL_EXPORT import blocked: the desktop file picker is unavailable.", IW4.Studio.Documents.AssetValidationSeverity.Error);
                return;
            }
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = replaceSelected ? "Replace XModel LOD from XMODEL_EXPORT" : "Add XModel LOD from XMODEL_EXPORT",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("XMODEL_EXPORT files") { Patterns = ["*.XMODEL_EXPORT", "*.xmodel_export"] }]
            });
            IStorageFile? file = files.FirstOrDefault();
            if (file is null) return;
            string contents;
            await using (Stream stream = await file.OpenReadAsync())
            using (var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
                contents = await reader.ReadToEndAsync();
            if (!XModelExportReader.TryRead(new StringReader(contents), out XModelExportDocument? document, out IReadOnlyList<XModelExportParseIssue> issues) || document is null)
            {
                string detail = string.Join(" ", issues.Take(3).Select(issue => $"Line {issue.Line}: {issue.Message}"));
                viewModel.ReportXModelExportStatus($"XMODEL_EXPORT import blocked: {detail}", IW4.Studio.Documents.AssetValidationSeverity.Error);
                return;
            }
            if (!viewModel.TryStageImportedLod(document, file.TryGetLocalPath() ?? file.Name, replaceSelected, out string? error))
                viewModel.ReportXModelExportStatus($"XMODEL_EXPORT import blocked: {error}", IW4.Studio.Documents.AssetValidationSeverity.Error);
            else
                viewModel.ReportXModelExportSuccess($"Staged {file.Name}; runtime compilation and material remapping are required before Apply.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            viewModel.ReportXModelExportStatus($"XMODEL_EXPORT import failed: {exception.Message}", IW4.Studio.Documents.AssetValidationSeverity.Error);
        }
        finally { _isImportInProgress = false; }
    }

    private async void ExportXModelButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_isExportInProgress)
            return;

        _isExportInProgress = true;
        try
        {
        if (DataContext is not XModelEditorViewModel viewModel)
            return;

        if (!viewModel.TryCreateXModelExportDocument(
                out XModelExportDocument? document,
                out IReadOnlyList<string> blockers) || document is null)
        {
            string detail = blockers.Count > 0
                ? string.Join(" ", blockers)
                : "The selected LOD could not be projected to XMODEL_EXPORT.";
            viewModel.ReportXModelExportStatus(
                $"XMODEL_EXPORT blocked: {detail}",
                IW4.Studio.Documents.AssetValidationSeverity.Error);
            return;
        }

        XModelExportDocument exportDocument = document;
        string modelName = viewModel.Name;
        int lodIndex = viewModel.SelectedLodIndex;
        string exported;
        try
        {
            using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            XModelExportWriter.Write(text, exportDocument);
            exported = text.ToString();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            viewModel.ReportXModelExportStatus(
                $"XMODEL_EXPORT blocked: {exception.Message}",
                IW4.Studio.Documents.AssetValidationSeverity.Error);
            return;
        }

        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            viewModel.ReportXModelExportStatus(
                "XMODEL_EXPORT blocked: the desktop file picker is unavailable.",
                IW4.Studio.Documents.AssetValidationSeverity.Error);
            return;
        }

        IStorageFile? destination;
        try
        {
            destination = await storageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export XMODEL_EXPORT version 6",
                    SuggestedFileName = SuggestedExportFileName(modelName, lodIndex),
                    DefaultExtension = "XMODEL_EXPORT",
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("XMODEL_EXPORT files")
                        {
                            Patterns = ["*.XMODEL_EXPORT"]
                        },
                        FilePickerFileTypes.All
                    ]
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            viewModel.ReportXModelExportStatus(
                $"XMODEL_EXPORT destination selection failed: {exception.Message}",
                IW4.Studio.Documents.AssetValidationSeverity.Error);
            return;
        }
        if (destination is null)
            return;

        try
        {
            string? localPath = destination.TryGetLocalPath();
            if (localPath is not null)
            {
                string directory = Path.GetDirectoryName(localPath) ?? throw new IOException(
                    "The selected destination has no parent directory.");
                string temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(localPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllTextAsync(
                        temporaryPath,
                        exported,
                        new System.Text.UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false));
                    File.Move(temporaryPath, localPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            else
            {
                await using Stream stream = await destination.OpenWriteAsync();
                if (!stream.CanSeek)
                    throw new NotSupportedException(
                        "The selected storage destination does not support a safe replacement write.");
                stream.SetLength(0);
                stream.Position = 0;
                await using var writer = new StreamWriter(
                    stream,
                    new System.Text.UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false),
                    leaveOpen: true);
                await writer.WriteAsync(exported);
                await writer.FlushAsync();
            }
            viewModel.ReportXModelExportSuccess(
                $"Exported XMODEL_EXPORT version 6 for {modelName} LOD {lodIndex} to {destination.Name}.");
        }
        catch (Exception exception) when (exception is IOException or
                   UnauthorizedAccessException or NotSupportedException or
                   ArgumentException)
        {
            viewModel.ReportXModelExportStatus(
                $"XMODEL_EXPORT write failed: {exception.Message}",
                IW4.Studio.Documents.AssetValidationSeverity.Error);
        }
        }
        finally
        {
            _isExportInProgress = false;
        }
    }

    private static string SuggestedExportFileName(string modelName, int lodIndex)
    {
        var name = new System.Text.StringBuilder(modelName.Length);
        foreach (char value in modelName)
        {
            name.Append(char.IsControl(value) ||
                "<>:\"/\\|?*".IndexOf(value) >= 0
                ? '_'
                : value);
        }

        string sanitized = name.ToString().Trim(' ', '.');
        if (string.IsNullOrEmpty(sanitized))
            sanitized = "xmodel";
        return $"{sanitized}_lod{lodIndex}.XMODEL_EXPORT";
    }

    private void Preview_RendererStatusChanged(
        object? sender,
        EventArgs e)
    {
        if (sender is not XModelPreviewControl preview)
            return;

        int lodIndex = preview.ReportedLodIndex;
        XModelViewerUploadResult? uploadResult = preview.UploadResult;
        string? rendererFailure = preview.RendererFailure;
        long statusRevision = preview.RendererStatusRevision;
        XModelRenderScene? scene = preview.Scene;
        void ApplyStatus()
        {
            if (!ReferenceEquals(preview.Scene, scene) ||
                preview.RendererStatusRevision != statusRevision ||
                preview.ReportedLodIndex != lodIndex ||
                DataContext is not XModelEditorViewModel viewModel ||
                !ReferenceEquals(viewModel.Scene, scene))
            {
                return;
            }

            viewModel.UpdateRendererStatus(
                lodIndex,
                uploadResult,
                rendererFailure);
        }

        if (Dispatcher.UIThread.CheckAccess())
            ApplyStatus();
        else
            Dispatcher.UIThread.Post(ApplyStatus);
    }
}
