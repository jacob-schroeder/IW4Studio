using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.XModel;

public sealed partial class XModelEditorView : UserControl
{
    private readonly XModelPreviewControl? _preview;
    private readonly XModelBoneTagOverlay? _boneTagOverlay;
    private bool _isAttached;

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
