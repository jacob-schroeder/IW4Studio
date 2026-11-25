using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Formats.SourceFormat.Fx;
using IW4.Game.Assets.Fx;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;
using IW4.Studio.Fx;

namespace IW4.Studio.Desktop.Editors.Fx;

public sealed partial class FxEditorView : UserControl
{
    private FxEditorViewModel? _viewModel;
    private FxEffectDefAsset? _loadedEffect;
    private int? _loadedLayer;
    private AssetEditorSession? _editorSession;

    public FxEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not FxEditorViewModel vm) return;
            bool hasWorkspace = vm.Workspace is { IsBlank: false };
            EffectTab.IsVisible = hasWorkspace;
            PreviewTabs.SelectedItem = !hasWorkspace ? MotionTab :
                vm.Effect.ElemDefs.Count > 0 && vm.Effect.ElemDefs.All(e => e.ElemType == FxElemType.Sound)
                    ? VisualTab : EffectTab;
        };
        PreviewTabs.SelectionChanged += (_, args) =>
        {
            if (args.Source == PreviewTabs) UpdatePreviewMode();
        };
        SelectedVisualPreview.RendererStatusChanged += SelectedVisualPreview_RendererStatusChanged;
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
        BindViewModel();
        DataContextChanged += View_DataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DataContextChanged -= View_DataContextChanged;
        UnbindViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void View_DataContextChanged(object? sender, EventArgs e) => BindViewModel();

    private void BindViewModel()
    {
        UnbindViewModel();
        _viewModel = DataContext as FxEditorViewModel;
        if (_viewModel is null) return;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdatePreviewMode();
    }

    private void UnbindViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.PausePlayback();
        _viewModel.SetVisualInspectorActive(false);
        _viewModel = null;
        _loadedEffect = null;
        _loadedLayer = null;
        EffectPreview.Clear();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FxEditorViewModel.Effect)
            or nameof(FxEditorViewModel.SelectedElementIndex) or nameof(FxEditorViewModel.ShowSelectedOnly))
            UpdateEffectPreview();
    }

    private void UpdatePreviewMode()
    {
        if (_viewModel is null) return;
        var mode = PreviewTabs.SelectedItem;
        SoloLayer.IsVisible = mode == EffectTab || mode == MotionTab;
        if (mode != MotionTab) _viewModel.PausePlayback();
        _viewModel.SetVisualInspectorActive(mode == VisualTab);
        UpdateEffectPreview();
    }

    private void UpdateEffectPreview()
    {
        if (PreviewTabs.SelectedItem != EffectTab ||
            _viewModel?.Workspace is not { IsBlank: false } workspace)
            return;
        int? layer = _viewModel.ShowSelectedOnly && _viewModel.HasSelectedElement
            ? _viewModel.SelectedElementIndex : null;
        if (ReferenceEquals(_loadedEffect, _viewModel.Effect) && _loadedLayer == layer) return;
        _loadedEffect = _viewModel.Effect;
        _loadedLayer = layer;
        _ = EffectPreview.SetEffectAsync(workspace, _loadedEffect, repeatOneShots: true, selectedElementIndex: layer);
    }

    private void SelectedVisualPreview_RendererStatusChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null || !ReferenceEquals(SelectedVisualPreview.Scene, _viewModel.SelectedVisualScene))
            return;
        string? diagnostic = SelectedVisualPreview.RendererFailure;
        if (diagnostic is null && SelectedVisualPreview.UploadResult is { ExecutableGroupCount: 0 } result)
            diagnostic = result.Diagnostics.FirstOrDefault() ?? "No material pass could be drawn.";
        _viewModel.ReportSelectedVisualRendererStatus(diagnostic);
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

    private void FitButton_Click(object? sender, RoutedEventArgs e) => Preview.Fit();

    private void FitVisualButton_Click(object? sender, RoutedEventArgs e) => SelectedVisualPreview.Fit();
}
