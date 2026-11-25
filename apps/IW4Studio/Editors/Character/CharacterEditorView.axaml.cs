using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IW4.Render;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Character;

public sealed partial class CharacterEditorView : UserControl, IEditorTextNavigator
{
    private const int AssemblyTab = 0;
    private const int ReferencesTab = 1;
    private const int SourceTab = 2;

    private static readonly FilePickerFileType GlbFileType = new("Binary glTF")
    {
        Patterns = ["*.glb"]
    };

    private CharacterEditorViewModel? _viewModel;
    private ReferenceKind _selectedReference = ReferenceKind.Head;
    private bool _showHands;
    private bool _refreshQueued;
    private bool _updatingBodyRail;
    private bool _committingReference;
    private double _graphZoom = 1;
    private Vector _graphOffset;
    private IPointer? _graphPanPointer;
    private Point _graphPanPosition;

    public CharacterEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnEditorDataContextChanged;
        AssemblyPreview.RendererStatusChanged += OnRendererStatusChanged;
        ReferencePreview.RendererStatusChanged += OnRendererStatusChanged;
        SourcePreview.RendererStatusChanged += OnRendererStatusChanged;
        foreach (AutoCompleteBox field in new[] { BodyField, HeadField, HandsField, ReferenceBodyField, ReferenceHeadField, ReferenceHandsField })
            field.AddHandler(KeyDownEvent, ReferenceValue_KeyDown, RoutingStrategies.Tunnel);

        ShowSelectedTab();
        UpdateReferenceSelection();
        UpdatePreviewMode();
    }

    public void NavigateTo(EditorTextLocation location)
    {
        CharacterTabs.SelectedIndex = SourceTab;
        SourceEditor.NavigateTo(location);
    }

    private void OnEditorDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnEditorPropertyChanged;

        _viewModel = DataContext as CharacterEditorViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnEditorPropertyChanged;

        UpdatePreview();
        UpdateReferenceSelection();
        RefreshBodyRail();
        RefreshChanges();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CharacterEditorViewModel.BodyOptions) or nameof(CharacterEditorViewModel.BodyModel))
            RefreshBodyRail();

        if (e.PropertyName is nameof(CharacterEditorViewModel.BaselineSource)
            or nameof(CharacterEditorViewModel.PayloadInput)
            or nameof(CharacterEditorViewModel.ThirdPersonScene)
            or nameof(CharacterEditorViewModel.BodyScene)
            or nameof(CharacterEditorViewModel.HeadScene)
            or nameof(CharacterEditorViewModel.HandsScene)
            or nameof(CharacterEditorViewModel.BodyModel)
            or nameof(CharacterEditorViewModel.HeadAlias)
            or nameof(CharacterEditorViewModel.ViewHands)
            or nameof(CharacterEditorViewModel.CanEditBody)
            or nameof(CharacterEditorViewModel.CanEditHead)
            or nameof(CharacterEditorViewModel.CanEditHands)
            or nameof(CharacterEditorViewModel.PreviewStatus) or null)
        {
            if (_refreshQueued)
                return;

            _refreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _refreshQueued = false;
                UpdatePreview();
                UpdateReferenceSelection();
                if (CharacterTabs.SelectedIndex == SourceTab)
                    RefreshChanges();
            });
        }
    }

    private void Tabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.Source) && SourcePage is not null)
            ShowSelectedTab();
    }

    private void ShowSelectedTab()
    {
        AssemblyPage.IsVisible = CharacterTabs.SelectedIndex == AssemblyTab;
        ReferencesPage.IsVisible = CharacterTabs.SelectedIndex == ReferencesTab;
        SourcePage.IsVisible = CharacterTabs.SelectedIndex == SourceTab;
        UpdatePreview();

        if (SourcePage.IsVisible)
            RefreshChanges();
    }

    private async void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is { CanApply: true } viewModel)
            await viewModel.ImportPayloadAsync();
    }

    private void Revert_Click(object? sender, RoutedEventArgs e) => _viewModel?.RevertDraft();

    private void OverrideCharacter_Click(object? sender, RoutedEventArgs e) => _viewModel?.OverrideCharacter();

    private void OpenSource_Click(object? sender, RoutedEventArgs e) =>
        CharacterTabs.SelectedIndex = SourceTab;

    private void OpenBody_Click(object? sender, RoutedEventArgs e) => _viewModel?.OpenBody();

    private async void ImportBody_Click(object? sender, RoutedEventArgs e) =>
        await ImportGlbAsync(viewHands: false);

    private async void ImportHands_Click(object? sender, RoutedEventArgs e) =>
        await ImportGlbAsync(viewHands: true);

    private async Task ImportGlbAsync(bool viewHands)
    {
        if (_viewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } window)
            return;

        try
        {
            IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = viewHands ? "Import view hands GLB" : "Import character body GLB",
                    AllowMultiple = false,
                    FileTypeFilter = [GlbFileType]
                });
            if (!ReferenceEquals(viewModel, _viewModel) || files.FirstOrDefault() is not { } file)
                return;

            using (file)
            {
                if (file.TryGetLocalPath() is { } path)
                    await viewModel.ImportGlbAsync(path, viewHands,
                        reasons => XModelImportWarningDialog.ConfirmAsync(this, viewModel, file.Name, reasons));
                else
                    viewModel.ReportReplacementFailure("Choose a GLB on the local filesystem.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            viewModel.ReportReplacementFailure(exception.Message);
        }
    }

    private void BodyModels_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingBodyRail && _viewModel is { CanEditBody: true } viewModel &&
            BodyModels.SelectedItem is string body &&
            body != viewModel.BodyModel)
        {
            viewModel.BodyModel = body;
        }
    }

    private void RefreshBodyRail()
    {
        string? current = _viewModel?.BodyModel;
        string[] bodies = _viewModel?.BodyOptions
            .Where(name => name.Contains("_body_", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_body", StringComparison.OrdinalIgnoreCase)
                || name.Equals(current, StringComparison.OrdinalIgnoreCase))
            .Prepend(current ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        _updatingBodyRail = true;
        try
        {
            if (BodyModels.ItemsSource is not string[] previous || !previous.SequenceEqual(bodies))
                BodyModels.ItemsSource = bodies;
            BodyModels.SelectedItem = current;
        }
        finally
        {
            _updatingBodyRail = false;
        }
    }

    private void ReferenceValue_DropDownClosed(object? sender, EventArgs e)
    {
        if (sender is AutoCompleteBox field && field.SelectedItem is string value)
            CommitReference(field, value);
    }

    private void ReferenceValue_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is AutoCompleteBox field && !field.IsKeyboardFocusWithin && !field.IsDropDownOpen)
            CommitReference(field, field.Text);
    }

    private void ReferenceValue_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is AutoCompleteBox field &&
            (!field.IsDropDownOpen || field.SelectedItem is null))
        {
            CommitReference(field, field.Text);
            field.IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private void CommitReference(AutoCompleteBox field, string? value)
    {
        if (_committingReference || _viewModel is not { } viewModel || !field.IsVisible || !field.IsEnabled)
            return;

        string reference = value?.Trim() ?? string.Empty;
        _committingReference = true;
        try
        {
            if (ReferenceEquals(field, BodyField) || ReferenceEquals(field, ReferenceBodyField))
            {
                if (reference != viewModel.BodyModel)
                    viewModel.BodyModel = reference;
                field.SetCurrentValue(AutoCompleteBox.TextProperty, viewModel.BodyModel);
            }
            else if (ReferenceEquals(field, HeadField) || ReferenceEquals(field, ReferenceHeadField))
            {
                if (reference != viewModel.HeadAlias)
                    viewModel.HeadAlias = reference;
                field.SetCurrentValue(AutoCompleteBox.TextProperty, viewModel.HeadAlias);
            }
            else
            {
                if (reference != viewModel.ViewHands)
                    viewModel.ViewHands = reference;
                field.SetCurrentValue(AutoCompleteBox.TextProperty, viewModel.ViewHands);
            }
        }
        finally
        {
            _committingReference = false;
        }
    }

    private void SelectBody_Click(object? sender, RoutedEventArgs e) => SelectReference(ReferenceKind.Body);
    private void SelectHead_Click(object? sender, RoutedEventArgs e) => SelectReference(ReferenceKind.Head);
    private void SelectHands_Click(object? sender, RoutedEventArgs e) => SelectReference(ReferenceKind.Hands);

    private void SelectReference(ReferenceKind kind)
    {
        if (_selectedReference == kind)
            return;

        _selectedReference = kind;
        UpdateReferenceSelection();
        UpdatePreview();
        ReferencePreview.Fit();
    }

    private AutoCompleteBox SelectedReferenceField => _selectedReference switch
    {
        ReferenceKind.Body => ReferenceBodyField,
        ReferenceKind.Head => ReferenceHeadField,
        _ => ReferenceHandsField
    };

    private void UpdateReferenceSelection()
    {
        bool body = _selectedReference == ReferenceKind.Body;
        bool head = _selectedReference == ReferenceKind.Head;
        bool hands = _selectedReference == ReferenceKind.Hands;

        BodyNode.Classes.Set("current", body);
        HeadNode.Classes.Set("current", head);
        HandsNode.Classes.Set("current", hands);
        BodyEdge.Classes.Set("current", body);
        HeadEdge.Classes.Set("current", head);
        HandsEdge.Classes.Set("current", hands);
        bool canReplace = _viewModel is { } viewModel && _selectedReference switch
        {
            ReferenceKind.Body => viewModel.CanEditBody,
            ReferenceKind.Head => viewModel.CanEditHead,
            _ => viewModel.CanEditHands
        };
        ReferenceBodyField.IsVisible = body && canReplace;
        ReferenceHeadField.IsVisible = head && canReplace;
        ReferenceHandsField.IsVisible = hands && canReplace;
        ReferenceValue.IsVisible = !canReplace;
        ReferenceValue.Text = _selectedReference switch
        {
            ReferenceKind.Body => _viewModel?.BodyModel,
            ReferenceKind.Head => _viewModel?.HeadAlias,
            _ => _viewModel?.ViewHands
        };
        AliasScopePanel.IsVisible = head;
        if (head && _viewModel is { CanEditHead: false })
            SharedAliasScope.IsChecked = true;
        ReplaceReference.IsEnabled = canReplace;
        ReferenceTitle.Text = body ? "Body model" : head ? "Head alias" : "View hands";
        UpdateReferenceAction();
    }

    private void ReplaceReference_Click(object? sender, RoutedEventArgs e)
    {
        AutoCompleteBox field = SelectedReferenceField;
        field.ApplyTemplate();
        if (field.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } input)
        {
            input.Focus();
            input.SelectAll();
        }
        else
            field.Focus();

        field.IsDropDownOpen = true;
    }

    private void AliasScope_Changed(object? sender, RoutedEventArgs e)
    {
        if (OpenReference is not null)
            UpdateReferenceAction();
    }

    private void UpdateReferenceAction()
    {
        bool shared = SharedAliasScope.IsChecked == true;
        OpenReference.Content = _selectedReference == ReferenceKind.Head
            ? shared ? "Open shared alias source" : "Create and open local alias"
            : "Open XModel";
        OpenReference.IsEnabled = _viewModel is { } viewModel && _selectedReference switch
        {
            ReferenceKind.Body => viewModel.BodyModel.Length > 0,
            ReferenceKind.Head => viewModel.HeadAlias.Length > 0 && (shared || viewModel.CanEditHead),
            _ => viewModel.ViewHands.Length > 0
        };
    }

    private void OpenReference_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } viewModel)
            return;

        switch (_selectedReference)
        {
            case ReferenceKind.Body:
                viewModel.OpenBody();
                break;
            case ReferenceKind.Head:
                viewModel.OpenHeadAlias(SharedAliasScope.IsChecked == true);
                break;
            case ReferenceKind.Hands:
                viewModel.OpenHands();
                break;
        }
    }

    private void FitGraph_Click(object? sender, RoutedEventArgs e)
    {
        _graphOffset = default;
        SetGraphZoom(1);
    }

    private void ZoomGraphOut_Click(object? sender, RoutedEventArgs e) => SetGraphZoom(_graphZoom / 1.2);
    private void ZoomGraphIn_Click(object? sender, RoutedEventArgs e) => SetGraphZoom(_graphZoom * 1.2);

    private void SetGraphZoom(double zoom)
    {
        _graphZoom = Math.Clamp(zoom, 0.65, 1.4);
        UpdateGraphTransform();
    }

    private void UpdateGraphTransform() =>
        ReferenceGraph.RenderTransform = new TransformGroup
        {
            Children = [new ScaleTransform(_graphZoom, _graphZoom), new TranslateTransform(_graphOffset.X, _graphOffset.Y)]
        };

    private void Graph_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(ReferenceGraphSurface);
        if (!point.Properties.IsRightButtonPressed && !point.Properties.IsMiddleButtonPressed)
            return;

        _graphPanPointer = e.Pointer;
        _graphPanPosition = point.Position;
        e.Pointer.Capture(ReferenceGraphSurface);
        e.Handled = true;
    }

    private void Graph_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _graphPanPointer))
            return;

        Point position = e.GetPosition(ReferenceGraphSurface);
        _graphOffset += position - _graphPanPosition;
        _graphPanPosition = position;
        UpdateGraphTransform();
        e.Handled = true;
    }

    private void Graph_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _graphPanPointer))
            return;

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void Graph_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        _graphPanPointer = null;

    private void ThirdPerson_Click(object? sender, RoutedEventArgs e) => SetPreviewMode(false);
    private void ViewHands_Click(object? sender, RoutedEventArgs e) => SetPreviewMode(true);

    private void SetPreviewMode(bool showHands)
    {
        if (_showHands == showHands)
            return;

        _showHands = showHands;
        UpdatePreviewMode();
        UpdatePreview();
        AssemblyPreview.Fit();
        SourcePreview.Fit();
    }

    private void UpdatePreviewMode()
    {
        AssemblyThirdPerson.Classes.Set("current", !_showHands);
        SourceThirdPerson.Classes.Set("current", !_showHands);
        AssemblyHands.Classes.Set("current", _showHands);
        SourceHands.Classes.Set("current", _showHands);
    }

    private void FitPreview_Click(object? sender, RoutedEventArgs e) =>
        (CharacterTabs.SelectedIndex == SourceTab ? SourcePreview : AssemblyPreview).Fit();

    private void Shading_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AssemblyPreviewMessage is not null && ShadingMode is not null)
            UpdatePreviewAppearance(AssemblyPreview, AssemblyPreviewMessage);
    }

    private void UpdatePreview()
    {
        XModelRenderScene? assembled = _showHands ? _viewModel?.HandsScene : _viewModel?.ThirdPersonScene;
        XModelRenderScene? selected = _selectedReference switch
        {
            ReferenceKind.Body => _viewModel?.BodyScene,
            ReferenceKind.Head => _viewModel?.HeadScene,
            _ => _viewModel?.HandsScene
        };
        UpdatePreviewScene(AssemblyPreview, AssemblyPreviewMessage, AssemblyPage.IsVisible ? assembled : null);
        UpdatePreviewScene(SourcePreview, SourcePreviewMessage, SourcePage.IsVisible ? assembled : null);
        UpdatePreviewScene(ReferencePreview, ReferencePreviewMessage, ReferencesPage.IsVisible ? selected : null);
    }

    private void UpdatePreviewScene(XModelPreviewControl preview, TextBlock message, XModelRenderScene? scene)
    {
        preview.SelectedLodIndex = scene?.DefaultLodIndex ?? -1;
        preview.Scene = scene;
        preview.IsVisible = scene is not null;
        UpdatePreviewAppearance(preview, message);
    }

    private void OnRendererStatusChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnRendererStatusChanged(sender, e));
            return;
        }

        if (ReferenceEquals(sender, AssemblyPreview))
            UpdatePreviewAppearance(AssemblyPreview, AssemblyPreviewMessage);
        else if (ReferenceEquals(sender, SourcePreview))
            UpdatePreviewAppearance(SourcePreview, SourcePreviewMessage);
        else
            UpdatePreviewAppearance(ReferencePreview, ReferencePreviewMessage);
    }

    private void UpdatePreviewAppearance(XModelPreviewControl preview, TextBlock message)
    {
        bool hasScene = preview.Scene is not null;
        bool materialsUnavailable = hasScene && preview.UploadResult is { ExecutableGroupCount: 0 };
        preview.ShowWireframe = hasScene &&
            (materialsUnavailable || (preview == AssemblyPreview && ShadingMode.SelectedIndex == 1));
        string? status = _viewModel?.PreviewStatus;
        message.Text = hasScene
            ? preview.RendererFailure
            : string.IsNullOrEmpty(status) ? "No model preview available" : status;
        message.IsVisible = !string.IsNullOrEmpty(message.Text);

        if (preview == AssemblyPreview)
        {
            string label = _showHands ? "First-person view hands" : "Third-person character";
            if (hasScene && !_showHands && _viewModel is { HeadAlias.Length: > 0 } viewModel &&
                ReferenceEquals(preview.Scene, viewModel.BodyScene))
            {
                label += " · head preview unavailable";
            }
            if (materialsUnavailable)
                label += " · authored materials unavailable";

            AssemblyPreviewHint.Text = label;
        }
    }

    private void RefreshChanges()
    {
        IReadOnlyList<CharacterSourceChange> changes = _viewModel is { } viewModel
            ? CharacterSourceChange.Between(viewModel.BaselineSource, viewModel.PayloadInput)
            : [];
        SourceChanges.ItemsSource = changes;
        ChangeSummary.Text = changes.Count == 0 ? "No draft changes" : FormatChangeCount(changes);
    }

    private static string FormatChangeCount(IReadOnlyList<CharacterSourceChange> changes)
    {
        int added = changes.Count(change => change.IsAddition);
        return $"+{added}  /  −{changes.Count - added}";
    }

    private enum ReferenceKind
    {
        Body,
        Head,
        Hands
    }
}
