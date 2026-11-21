using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IW4.Render;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.XAnim;

public sealed partial class XAnimEditorView : UserControl
{
    private readonly XModelPreviewControl? _preview;
    private XAnimPreviewViewModel? _viewModel;
    private bool _startBonePlayback;

    public XAnimEditorView()
    {
        InitializeComponent();
        _preview = this.FindControl<XModelPreviewControl>("Preview");
        if (_preview is not null)
        {
            _preview.PropertyChanged += Preview_PropertyChanged;
            _preview.RendererStatusChanged += Preview_RendererStatusChanged;
            if (this.FindControl<Border>("PreviewInputSurface") is { } input)
                _preview.AttachCameraInput(input);
        }

        DataContextChanged += View_DataContextChanged;
        Loaded += (_, _) => StartBonePlaybackIfReady();
        View_DataContextChanged(this, EventArgs.Empty);
    }

    private void View_DataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.PausePlayback();
        }

        _viewModel = DataContext as XAnimPreviewViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _startBonePlayback = _viewModel?.ShowBoneTracks == true;
        StartBonePlaybackIfReady();
        UpdateUnavailableOverlay();
    }

    private void StartBonePlaybackIfReady()
    {
        if (!_startBonePlayback || !IsLoaded || _viewModel is not { CanPlay: true } vm)
            return;

        _startBonePlayback = false;
        if (!vm.IsPlaying)
            vm.TogglePlayback();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(XAnimPreviewViewModel.PreviewUnavailableReason) or
            nameof(XAnimPreviewViewModel.ShowModelView) or
            nameof(XAnimPreviewViewModel.ShowUnavailable))
        {
            UpdateUnavailableOverlay();
        }
    }

    private void Preview_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != XModelPreviewControl.SceneProperty ||
            sender is not XModelPreviewControl preview)
        {
            return;
        }

        XModelRenderScene? scene = preview.Scene;
        preview.SelectedLodIndex = scene?.DefaultLodIndex ?? -1;
        UpdateUnavailableOverlay();
    }

    private void Preview_RendererStatusChanged(object? sender, EventArgs e)
    {
        if (sender is not XModelPreviewControl preview)
            return;

        XModelRenderScene? scene = preview.Scene;
        long revision = preview.RendererStatusRevision;
        void Apply()
        {
            if (ReferenceEquals(preview.Scene, scene) &&
                preview.RendererStatusRevision == revision)
            {
                UpdateUnavailableOverlay();
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void UpdateUnavailableOverlay()
    {
        XAnimPreviewViewModel? vm = _viewModel;
        if (vm is null)
        {
            PreviewUnavailableOverlay.IsVisible = false;
            return;
        }

        string? failure = vm.ShowModelView && _preview is { Scene: not null } preview
            ? preview.RendererFailure
            : null;
        if (!string.IsNullOrWhiteSpace(failure))
        {
            PreviewUnavailableTitleText.Text = "OpenGL preview unavailable";
            PreviewUnavailableReasonText.Text = failure;
            PreviewUnavailableOverlay.IsVisible = true;
            return;
        }

        PreviewUnavailableTitleText.Text = vm.PreviewUnavailableTitle;
        PreviewUnavailableReasonText.Text = vm.PreviewUnavailableReason;
        PreviewUnavailableOverlay.IsVisible = vm.ShowUnavailable;
    }

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        _viewModel?.PausePlayback();
        base.OnDetachedFromVisualTree(e);
    }

    private void FitButton_Click(object? sender, RoutedEventArgs e) =>
        _preview?.Fit();

    private void PlayPauseButton_Click(object? sender, RoutedEventArgs e) =>
        _viewModel?.TogglePlayback();

    private void RestartButton_Click(object? sender, RoutedEventArgs e) =>
        _viewModel?.RestartPlayback();
}
