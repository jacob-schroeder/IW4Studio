using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Fx;

public sealed partial class FxEditorView : UserControl
{
    private readonly XModelPreviewControl? _selectedVisualPreview;
    private bool _isAttached;

    public FxEditorView()
    {
        InitializeComponent();
        _selectedVisualPreview =
            this.FindControl<XModelPreviewControl>("SelectedVisualPreview");
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_isAttached)
            return;

        _isAttached = true;
        if (_selectedVisualPreview is not null)
        {
            _selectedVisualPreview.RendererStatusChanged +=
                SelectedVisualPreview_RendererStatusChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        if (_isAttached && _selectedVisualPreview is not null)
        {
            _selectedVisualPreview.RendererStatusChanged -=
                SelectedVisualPreview_RendererStatusChanged;
        }
        _isAttached = false;

        if (DataContext is FxEditorViewModel viewModel)
        {
            viewModel.PausePlayback();
            viewModel.SelectedSoundPreview?.PausePlayback();
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void SelectedVisualPreview_RendererStatusChanged(
        object? sender,
        EventArgs e)
    {
        if (DataContext is not FxEditorViewModel viewModel ||
            _selectedVisualPreview is null)
        {
            return;
        }

        string? message = _selectedVisualPreview.RendererFailure is
            { } failure
                ? $"Selected visual renderer failed: {failure}"
                : _selectedVisualPreview.UploadResult is
                    { ExecutableGroupCount: 0 } upload
                    ? "The selected visual produced no executable authored " +
                      $"material pass: {upload.Diagnostics.FirstOrDefault() ?? "no renderer diagnostic was produced"}"
                    : null;
        viewModel.ReportSelectedVisualRendererStatus(message);
    }

    private void PlayPauseButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as FxEditorViewModel)?.TogglePlayback();

    private void RestartButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as FxEditorViewModel)?.RestartPlayback();

    private void SoundPlayPauseButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        ((sender as Control)?.DataContext as SoundPreviewViewModel)?
            .TogglePlayback();

    private void FitButton_Click(object? sender, RoutedEventArgs e)
    {
        Preview.Fit();
        _selectedVisualPreview?.Fit();
    }
}
