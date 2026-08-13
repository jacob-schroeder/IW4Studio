using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using IW4.Assets.XModel.Export;
using IW4.FastFiles.Zone;
using IW4.Render;
using IW4.Render.Export;
using IW4.Render.OpenGl.XModel;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Editors.AssetReferences;
using SkiaSharp;

namespace IW4.Studio.Desktop.Editors.XModel;

public sealed partial class XModelEditorView : UserControl
{
    private readonly XModelPreviewControl? _preview;
    private readonly XModelBoneTagOverlay? _boneTagOverlay;
    private AssetReferencePickerService? _assetReferencePicker;
    private bool _isAttached;
    private bool _isExportInProgress;
    private bool _isGlbExportInProgress;
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

    internal XModelEditorView(XModelEditorViewModel viewModel, AssetReferencePickerService assetReferencePicker)
        : this()
    {
        _assetReferencePicker = assetReferencePicker ?? throw new ArgumentNullException(nameof(assetReferencePicker));
        DataContext = viewModel;
        viewModel.AssetReferenceSelectionRequested += async (_, args) =>
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                await assetReferencePicker.ShowAsync(owner, args.Row);
        };
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

    private void ApplyButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as XModelEditorViewModel)?.ApplyCompiledDraft();

    private async void AddLodButton_Click(object? sender, RoutedEventArgs e) =>
        await ImportLodAsync(replaceSelected: false);

    private async void ReplaceLodButton_Click(object? sender, RoutedEventArgs e) =>
        await ImportLodAsync(replaceSelected: true);

    private async void ReplaceModelButton_Click(object? sender, RoutedEventArgs e) =>
        await ImportLodAsync(replaceSelected: true, replaceModel: true);

    private void RemoveLodButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as XModelEditorViewModel)?.RemoveSelectedAssemblyLod();

    private async Task ImportLodAsync(bool replaceSelected, bool replaceModel = false)
    {
        if (_isImportInProgress || DataContext is not XModelEditorViewModel viewModel)
            return;
        _isImportInProgress = true;
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                viewModel.ReportXModelExportStatus("XModel geometry import blocked: the desktop file picker is unavailable.", IW4.Studio.Documents.AssetValidationSeverity.Error);
                return;
            }
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = replaceModel
                    ? "Replace complete XModel visual geometry"
                    : replaceSelected ? "Replace XModel LOD geometry" : "Add XModel LOD geometry",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("XModel geometry") { Patterns = ["*.glb", "*.GLB", "*.XMODEL_EXPORT", "*.xmodel_export"] },
                    new FilePickerFileType("Binary glTF 2.0") { Patterns = ["*.glb", "*.GLB"] },
                    new FilePickerFileType("XMODEL_EXPORT files") { Patterns = ["*.XMODEL_EXPORT", "*.xmodel_export"] }
                ]
            });
            IStorageFile? file = files.FirstOrDefault();
            if (file is null) return;

            XModelExportDocument? document;
            await using (Stream stream = await file.OpenReadAsync())
            {
                if (string.Equals(Path.GetExtension(file.Name), ".glb", StringComparison.OrdinalIgnoreCase))
                {
                    bool read = replaceModel
                        ? XModelGlbReader.TryReadRigidModel(
                            stream,
                            DecodeGlbImage,
                            out document,
                            out IReadOnlyList<string> blockers)
                        : XModelGlbReader.TryRead(
                            stream,
                            DecodeGlbImage,
                            out document,
                            out blockers);
                    if (!read || document is null)
                    {
                        viewModel.ReportXModelExportStatus(
                            $"GLB import blocked: {string.Join(" ", blockers.Take(3))}",
                            IW4.Studio.Documents.AssetValidationSeverity.Error);
                        return;
                    }
                }
                else
                {
                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                    string contents = await reader.ReadToEndAsync();
                    if (!XModelExportReader.TryRead(new StringReader(contents), out document, out IReadOnlyList<XModelExportParseIssue> issues) || document is null)
                    {
                        string detail = string.Join(" ", issues.Take(3).Select(issue => $"Line {issue.Line}: {issue.Message}"));
                        viewModel.ReportXModelExportStatus($"XMODEL_EXPORT import blocked: {detail}", IW4.Studio.Documents.AssetValidationSeverity.Error);
                        return;
                    }
                }
            }
            bool staged = replaceModel
                ? viewModel.TryStageReplacementModel(document, file.TryGetLocalPath() ?? file.Name, out string? error)
                : viewModel.TryStageImportedLod(document, file.TryGetLocalPath() ?? file.Name, replaceSelected, out error);
            if (!staged)
                viewModel.ReportXModelExportStatus($"XModel geometry import blocked: {error}", IW4.Studio.Documents.AssetValidationSeverity.Error);
            else
                viewModel.ReportXModelExportSuccess(replaceModel
                    ? $"Staged {file.Name} as a complete rigid model replacement; review rebuilt bounds, collision, materials, and validation before Apply."
                    : $"Staged {file.Name}; review material remapping and validation before Apply.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            viewModel.ReportXModelExportStatus($"XModel geometry import failed: {exception.Message}", IW4.Studio.Documents.AssetValidationSeverity.Error);
        }
        finally { _isImportInProgress = false; }
    }

    private static XModelImportImage DecodeGlbImage(
        string mimeType,
        ReadOnlyMemory<byte> encoded)
    {
        using SKData data = SKData.CreateCopy(encoded.ToArray());
        using SKCodec codec = SKCodec.Create(data) ??
            throw new InvalidDataException($"The embedded {mimeType} image could not be decoded.");
        if (codec.Info.Width is <= 0 or > ushort.MaxValue ||
            codec.Info.Height is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException("The embedded GLB image dimensions exceed IW4 limits.");
        }
        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"The embedded {mimeType} image could not be decoded ({result}).");
        byte[] rgba = new byte[checked(info.Width * info.Height * 4)];
        Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length);
        return new XModelImportImage(
            info.Width,
            info.Height,
            Array.AsReadOnly(rgba));
    }

    private void ExportXModelMenuItem_Click(
        object? sender,
        RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(
            async () => await ExportXModelAsync(),
            DispatcherPriority.Background);

    private async Task ExportXModelAsync()
    {
        if (_isExportInProgress)
            return;

        _isExportInProgress = true;
        try
        {
        if (DataContext is not XModelEditorViewModel viewModel)
            return;

        string modelName = viewModel.Name;
        int lodIndex = viewModel.SelectedLodIndex;
        if (!TrySerializeXModelExport(viewModel, out string? exported) || exported is null)
            return;

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

    private void ExportGlbMenuItem_Click(
        object? sender,
        RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(
            async () => await ExportGlbAsync(),
            DispatcherPriority.Background);

    private async Task ExportGlbAsync()
    {
        if (_isGlbExportInProgress ||
            DataContext is not XModelEditorViewModel viewModel)
        {
            return;
        }

        _isGlbExportInProgress = true;
        try
        {
            if (!viewModel.TryCreateGlb(
                    out byte[]? glb,
                    out int texturedMaterialCount,
                    out int materialCount,
                    out IReadOnlyList<string> blockers) ||
                glb is null)
            {
                string detail = blockers.Count == 0
                    ? "The selected LOD could not be represented as GLB."
                    : string.Join(" ", blockers);
                viewModel.ReportXModelExportStatus(
                    $"GLB export blocked: {detail}",
                    IW4.Studio.Documents.AssetValidationSeverity.Error);
                return;
            }

            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
            {
                viewModel.ReportXModelExportStatus(
                    "GLB export blocked: the desktop file picker is unavailable.",
                    IW4.Studio.Documents.AssetValidationSeverity.Error);
                return;
            }

            string modelName = viewModel.Name;
            int lodIndex = viewModel.SelectedLodIndex;
            IStorageFile? destination = await storageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export binary glTF 2.0",
                    SuggestedFileName = SuggestedGlbFileName(modelName, lodIndex),
                    DefaultExtension = "glb",
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Binary glTF files")
                        {
                            Patterns = ["*.glb"]
                        },
                        FilePickerFileTypes.All
                    ]
                });
            if (destination is null)
                return;

            string? localPath = destination.TryGetLocalPath();
            if (localPath is not null)
            {
                string directory = Path.GetDirectoryName(localPath) ??
                    throw new IOException("The selected destination has no parent directory.");
                string temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(localPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllBytesAsync(temporaryPath, glb);
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
                {
                    throw new NotSupportedException(
                        "The selected storage destination does not support a safe replacement write.");
                }
                stream.SetLength(0);
                stream.Position = 0;
                await stream.WriteAsync(glb);
                await stream.FlushAsync();
            }

            viewModel.ReportXModelExportSuccess(
                $"Exported GLB for {modelName} LOD {lodIndex} to {destination.Name}; " +
                $"embedded {texturedMaterialCount} of {materialCount} resolved base-color textures. " +
                "IW4 authored techniques remain available only in Studio.");
        }
        catch (Exception exception) when (exception is
                   IOException or
                   UnauthorizedAccessException or
                   NotSupportedException or
                   ArgumentException or
                   InvalidOperationException)
        {
            viewModel.ReportXModelExportStatus(
                $"GLB export failed: {exception.Message}",
                IW4.Studio.Documents.AssetValidationSeverity.Error);
        }
        finally
        {
            _isGlbExportInProgress = false;
        }
    }

    private static bool TrySerializeXModelExport(
        XModelEditorViewModel viewModel,
        out string? exported)
    {
        exported = null;
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
            return false;
        }

        try
        {
            using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            XModelExportWriter.Write(text, document);
            exported = text.ToString();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            viewModel.ReportXModelExportStatus(
                $"XMODEL_EXPORT blocked: {exception.Message}",
                IW4.Studio.Documents.AssetValidationSeverity.Error);
            return false;
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

    private static string SuggestedGlbFileName(string modelName, int lodIndex)
    {
        string source = SuggestedExportFileName(modelName, lodIndex);
        int extensionIndex = source.IndexOf(".XMODEL_EXPORT", StringComparison.Ordinal);
        string stem = extensionIndex >= 0 ? source[..extensionIndex] : source;
        return stem + ".glb";
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
