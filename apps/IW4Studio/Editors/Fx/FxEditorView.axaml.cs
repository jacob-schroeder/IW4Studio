using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;
using IW4.Studio.Fx;
using IW4.Formats.SourceFormat.Fx;

namespace IW4.Studio.Desktop.Editors.Fx;

public sealed partial class FxEditorView : UserControl
{
    private readonly XModelPreviewControl? _selectedVisualPreview;
    private AssetEditorSession? _editorSession;
    private bool _isAttached;

    public FxEditorView()
    {
        InitializeComponent();
        _selectedVisualPreview =
            this.FindControl<XModelPreviewControl>("SelectedVisualPreview");
    }

    public void SetEditorSession(AssetEditorSession? session)
    {
        _editorSession = session;
        EditLayersButton.IsVisible = session?.CanEdit == true;
    }

    private async void EditLayersButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_editorSession is not { CanEdit: true } session ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;
        var editor = new FxLayerEditorWindow(session);
        bool applied = await editor.ShowDialog<bool>(owner);
        if (!applied || DataContext is not FxEditorViewModel viewModel) return;
        FxDraft draft = session.OpenDraft<FxDraft>();
        viewModel.Reload(new FxExchange().LinkJson(draft.Json, draft.AssetName));
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
