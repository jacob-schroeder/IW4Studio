using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Weapon;

public sealed partial class WeaponEditorView : UserControl
{
    private const double CompactPropertyWorkspaceWidth = 1040;
    private readonly XModelPreviewControl? _preview;
    private readonly XModelBoneTagOverlay? _boneTagOverlay;
    private AssetReferencePickerService? _assetReferencePicker;
    private bool _isAttached;
    private bool _usesCompactPropertyWorkspace;

    public WeaponEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        UpdatePropertyWorkspaceLayout(
            Bounds.Width < CompactPropertyWorkspaceWidth);
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

    private void Editor_SizeChanged(object? sender, SizeChangedEventArgs e)
        => UpdatePropertyWorkspaceLayout(
            e.NewSize.Width < CompactPropertyWorkspaceWidth);

    private void UpdatePropertyWorkspaceLayout(bool useCompactLayout)
    {
        if (PropertyWorkspaceGrid is null ||
            PropertyPrimaryPane is null ||
            PropertySidebarPane is null)
        {
            return;
        }
        if (_usesCompactPropertyWorkspace == useCompactLayout) return;

        _usesCompactPropertyWorkspace = useCompactLayout;
        PropertyWorkspaceGrid.ColumnDefinitions = new ColumnDefinitions(
            useCompactLayout ? "*" : "2*,12,*");
        PropertyWorkspaceGrid.RowDefinitions = new RowDefinitions(
            useCompactLayout ? "Auto,12,Auto" : "Auto");
        Grid.SetColumn(PropertyPrimaryPane, 0);
        Grid.SetRow(PropertyPrimaryPane, 0);
        Grid.SetColumn(PropertySidebarPane, useCompactLayout ? 0 : 2);
        Grid.SetRow(PropertySidebarPane, useCompactLayout ? 2 : 0);
    }

    private void AssignDetectedTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: string tagName } &&
            DataContext is WeaponEditorViewModel viewModel)
        {
            viewModel.AssignDetectedModelTag(tagName);
        }
    }

    private void StagedInput_LostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Control
            {
                DataContext: IInspectorStagedPropertyRow row
            })
        {
            _ = row.CommitInput();
        }
    }

    private void StagedInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control
            {
                DataContext: IInspectorStagedPropertyRow row
            })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = row.CommitInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.ResetInput();
            e.Handled = true;
        }
    }

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
