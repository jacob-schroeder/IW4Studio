using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Weapon;

public sealed partial class WeaponEditorView : UserControl
{
    private readonly XModelPreviewControl? _preview;
    private readonly XModelBoneTagOverlay? _boneTagOverlay;
    private AssetReferencePickerService? _assetReferencePicker;
    private bool _isAttached;

    public WeaponEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        _preview = this.FindControl<XModelPreviewControl>("Preview");
        _boneTagOverlay = this.FindControl<XModelBoneTagOverlay>("BoneTagOverlay");
        if (_preview is not null && this.FindControl<Border>("PreviewInputSurface") is { } input)
            _preview.AttachCameraInput(input);
    }

    internal WeaponEditorView(WeaponEditorViewModel viewModel, AssetReferencePickerService assetReferencePicker) : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _assetReferencePicker = assetReferencePicker ?? throw new ArgumentNullException(nameof(assetReferencePicker));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_isAttached) return;
        _isAttached = true;
        if (_preview is not null) { _preview.RendererStatusChanged += Preview_RendererStatusChanged; _boneTagOverlay?.Attach(_preview); }
        if (DataContext is WeaponEditorViewModel vm && _assetReferencePicker is not null) vm.AssetReferenceSelectionRequested += ViewModel_AssetReferenceSelectionRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_isAttached)
        {
            if (_preview is not null) _preview.RendererStatusChanged -= Preview_RendererStatusChanged;
            _boneTagOverlay?.Detach();
            if (DataContext is WeaponEditorViewModel vm) vm.AssetReferenceSelectionRequested -= ViewModel_AssetReferenceSelectionRequested;
            _isAttached = false;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private async void ViewModel_AssetReferenceSelectionRequested(object? sender, AssetReferenceSelectionRequestedEventArgs e)
    {
        if (_assetReferencePicker is not null && TopLevel.GetTopLevel(this) is Window owner)
            await _assetReferencePicker.ShowAsync(owner, e.Row);
    }

    private void FitButton_Click(object? sender, RoutedEventArgs e) => _preview?.Fit();
    private void RevertButton_Click(object? sender, RoutedEventArgs e) => (DataContext as WeaponEditorViewModel)?.RevertDraft();
    private void ApplyButton_Click(object? sender, RoutedEventArgs e) => (DataContext as WeaponEditorViewModel)?.ApplyDraft();

    private void Preview_RendererStatusChanged(object? sender, EventArgs e)
    {
        if (sender is not XModelPreviewControl preview) return;
        int lodIndex = preview.ReportedLodIndex;
        XModelViewerUploadResult? upload = preview.UploadResult;
        string? failure = preview.RendererFailure;
        long revision = preview.RendererStatusRevision;
        XModelRenderScene? scene = preview.Scene;
        void Apply()
        {
            if (!ReferenceEquals(preview.Scene, scene) || preview.RendererStatusRevision != revision || preview.ReportedLodIndex != lodIndex || DataContext is not WeaponEditorViewModel vm || !ReferenceEquals(vm.Scene, scene)) return;
            vm.UpdateRendererStatus(lodIndex, upload, failure);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
}
