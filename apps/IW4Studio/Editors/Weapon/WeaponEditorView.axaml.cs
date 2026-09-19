using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IW4.AssetExchange.XModel;
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
    private const long MaximumCamoImageFileBytes = 128L * 1024 * 1024;
    private readonly XModelPreviewControl? _preview;
    private readonly XModelBoneTagOverlay? _boneTagOverlay;
    private AssetReferencePickerService? _assetReferencePicker;
    private bool _isAttached;
    private bool _isCamoImageImportInProgress;
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
        if (this.FindControl<Slider>("CamoLoopSlider") is { } camoLoopSlider)
        {
            camoLoopSlider.AddHandler(
                InputElement.PointerReleasedEvent,
                CamoLoopSlider_PointerReleased,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
            camoLoopSlider.AddHandler(
                InputElement.KeyUpEvent,
                CamoLoopSlider_KeyUp,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
            camoLoopSlider.LostFocus += CamoLoopSlider_LostFocus;
        }
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
            if (DataContext is WeaponEditorViewModel vm)
            {
                vm.AssetReferenceSelectionRequested -= ViewModel_AssetReferenceSelectionRequested;
            }
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

    private void PreviewAnimationButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is Control
            {
                DataContext: InspectorAssetReferencePropertyRowViewModel row
            } &&
            DataContext is WeaponEditorViewModel viewModel &&
            TopLevel.GetTopLevel(this) is Window owner)
        {
            XAnimPreviewViewModel? preview =
                viewModel.CreateAnimationPreview(
                    row,
                    out XModelRenderScene? scene,
                    out int selectedLodIndex,
                    out bool isMaterialAnimationEnabled);
            if (preview is null)
                return;

            var window = new WeaponAnimationPreviewWindow(
                preview,
                scene,
                selectedLodIndex,
                scene?.Name ?? "Weapon assembly unavailable",
                $"View model / camo · {viewModel.SelectedCamo?.ToString() ?? "Default / index 00"}",
                isMaterialAnimationEnabled);
            window.Show(owner);
        }
    }

    private void ToggleCamoEditorButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is WeaponEditorViewModel viewModel)
            viewModel.ToggleCamoEditor();
    }

    private void CloseCamoEditorButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is WeaponEditorViewModel viewModel)
            viewModel.CloseCamoEditor();
    }

    private async void ChooseCamoImageButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not WeaponEditorViewModel viewModel ||
            !viewModel.CanChooseCamoImage ||
            _isCamoImageImportInProgress ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        string? targetIdentity = viewModel.CaptureCamoTargetIdentity();
        Bitmap? preview = null;
        _isCamoImageImportInProgress = true;
        try
        {
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Choose weapon camo image",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("PNG or JPEG images")
                        {
                            Patterns = ["*.png", "*.PNG", "*.jpg", "*.JPG", "*.jpeg", "*.JPEG"]
                        },
                        new FilePickerFileType("PNG images")
                        {
                            Patterns = ["*.png", "*.PNG"]
                        },
                        new FilePickerFileType("JPEG images")
                        {
                            Patterns = ["*.jpg", "*.JPG", "*.jpeg", "*.JPEG"]
                        }
                    ]
                });
            if (files.Count != 1)
                return;

            IStorageFile file = files[0];
            await using Stream source = await file.OpenReadAsync();
            if (source.CanSeek && source.Length > MaximumCamoImageFileBytes)
            {
                throw new InvalidDataException(
                    "The camo image file is larger than the 128 MiB import limit.");
            }

            using var encoded = new MemoryStream();
            await source.CopyToAsync(encoded);
            if (encoded.Length > MaximumCamoImageFileBytes)
            {
                throw new InvalidDataException(
                    "The camo image file is larger than the 128 MiB import limit.");
            }

            byte[] bytes = encoded.ToArray();
            XModelImportImage image = await Task.Run(() =>
                XModelImportImageDecoder.DecodeWeaponCamo(file.Name, bytes));
            if (!viewModel.IsCurrentCamoTarget(targetIdentity))
                return;
            using var previewStream = new MemoryStream(bytes, writable: false);
            preview = new Bitmap(previewStream);
            viewModel.SetCamoImage(file.Name, image, preview);
            preview = null;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or OverflowException or ArgumentException or
            InvalidOperationException)
        {
            viewModel.ReportCamoImageFailure(
                $"The camo image could not be loaded: {exception.Message}");
        }
        finally
        {
            preview?.Dispose();
            _isCamoImageImportInProgress = false;
        }
    }

    private void ResetCamoAnimationButton_Click(
        object? sender,
        RoutedEventArgs e) => _preview?.ResetMaterialAnimation();

    private void CamoLoopSlider_PointerReleased(
        object? sender,
        PointerReleasedEventArgs e) => CommitCamoLoopSeconds();

    private void CamoLoopSlider_KeyUp(
        object? sender,
        KeyEventArgs e) => CommitCamoLoopSeconds();

    private void CamoLoopSlider_LostFocus(
        object? sender,
        RoutedEventArgs e) => CommitCamoLoopSeconds();

    private void CommitCamoLoopSeconds()
    {
        if (DataContext is WeaponEditorViewModel viewModel)
            viewModel.CommitCamoLoopSeconds();
    }

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
