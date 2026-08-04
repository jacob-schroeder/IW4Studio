using System.ComponentModel;
using Avalonia.Threading;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.Editors.Menu;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Preview;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Shared Desktop selection and outline state for Menu and MenuFile editors.
/// Studio owns the immutable snapshot and applies every typed aggregate edit;
/// this view model owns only local selection and staged inspector input.
/// </summary>
public sealed class MenuDesignerViewModel : ObservableObject, IDisposable
{
    private const float DuplicateItemOffset = 8f;

    private readonly Func<MenuEdit, MenuEditorSnapshot>? _applyEdit;
    private readonly Func<bool>? _isEditAllowed;
    private readonly Action<InspectorAssetReferencePropertyRowViewModel>?
        _requestAssetReferenceSelection;
    private readonly Action<MenuItemBehaviorEditRequestedEventArgs>?
        _requestItemBehaviorEdit;
    private readonly IMenuPreviewMaterialResolver? _materialResolver;
    private readonly IMenuTextResourceResolver? _textResourceResolver;
    private readonly MenuPreviewDebugViewModel _previewDebug;
    private readonly Func<XAssetType, string?, bool>?
        _isAssetReferenceResolved;
    private readonly List<InspectorPropertyRowViewModel> _observedRows = [];
    private readonly Dictionary<string, MenuPreviewMaterialStatus>
        _materialPreviewStatuses = new(StringComparer.Ordinal);
    private readonly Dictionary<MenuNodeId, MenuPreviewTextStatus>
        _textPreviewStatuses = [];
    private PreviewMaterialResourceKey[] _previewMaterialResources = [];
    private PreviewTextResourceKey[] _previewTextResources = [];
    private string? _directManipulationError;
    private MenuEditorSnapshot? _snapshot;
    private IReadOnlyList<MenuOutlineNodeViewModel> _outlineRoots = [];
    private MenuOutlineNodeViewModel? _selectedNode;
    private InspectorSelectionViewModel? _inspectorSelection;
    private bool _hasStagedInput;
    private bool _disposed;

    public MenuDesignerViewModel(
        MenuEditorSnapshot? snapshot,
        Func<MenuEdit, MenuEditorSnapshot>? applyEdit = null,
        Action<InspectorAssetReferencePropertyRowViewModel>?
            requestAssetReferenceSelection = null,
        IMenuPreviewMaterialResolver? materialResolver = null,
        IMenuTextResourceResolver? textResourceResolver = null,
        Func<bool>? isEditAllowed = null,
        Func<XAssetType, string?, bool>? isAssetReferenceResolved = null,
        Action<MenuItemBehaviorEditRequestedEventArgs>?
            requestItemBehaviorEdit = null)
    {
        _applyEdit = applyEdit;
        _isEditAllowed = isEditAllowed;
        _requestAssetReferenceSelection = requestAssetReferenceSelection;
        _requestItemBehaviorEdit = requestItemBehaviorEdit;
        _materialResolver = materialResolver;
        _textResourceResolver = textResourceResolver;
        _previewDebug = new MenuPreviewDebugViewModel(textResourceResolver);
        _previewDebug.PreviewChanged += PreviewDebug_PreviewChanged;
        if (_textResourceResolver is not null)
            _textResourceResolver.Changed += TextResourceResolver_Changed;
        _isAssetReferenceResolved = isAssetReferenceResolved;
        AddItemCommand = new ViewModelCommand(
            AddItem,
            () => IsEditable && !HasStagedInput);
        DuplicateItemCommand = new ViewModelCommand(
            DuplicateSelectedItem,
            CanDuplicateSelectedItem);
        RemoveItemCommand = new ViewModelCommand(
            RemoveSelectedItem,
            CanEditSelectedItem);
        MoveItemUpCommand = new ViewModelCommand(
            MoveSelectedItemUp,
            CanMoveSelectedItemUp);
        MoveItemDownCommand = new ViewModelCommand(
            MoveSelectedItemDown,
            CanMoveSelectedItemDown);
        ReplaceDocument(snapshot);
    }

    public MenuEditorSnapshot? Snapshot => _snapshot;

    public bool HasDocument => _snapshot is { IsComplete: true };

    public bool HasNoDocument => !HasDocument;

    public bool IsComplete => _snapshot?.IsComplete == true;

    public bool IsEditable =>
        HasDocument &&
        _applyEdit is not null &&
        (_isEditAllowed?.Invoke() ?? true);

    public string DocumentName =>
        _snapshot is null
            ? "Unavailable Menu"
            : MenuPresentationText.MenuTitle(_snapshot.Name);

    public int ItemCount => _snapshot?.Items.Count ?? 0;

    public MenuPreviewScene? PreviewScene => PreviewDebug.Scene;

    public IMenuPreviewMaterialResolver? MaterialResolver => _materialResolver;

    public IMenuTextResourceResolver? TextResourceResolver =>
        _textResourceResolver;

    public MenuPreviewDebugViewModel PreviewDebug => _previewDebug;

    internal bool IsDisposed => _disposed;

    internal event EventHandler? Disposed;

    public IReadOnlyList<MenuOutlineNodeViewModel> OutlineRoots
    {
        get => _outlineRoots;
        private set => SetProperty(ref _outlineRoots, value);
    }

    public MenuOutlineNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(value, _selectedNode))
                return;

            // Invalid or otherwise staged text belongs to the current entity.
            // Do not silently discard it because another outline row was
            // clicked; Enter, focus loss, or Escape resolves the staging.
            if (HasStagedInput)
            {
                OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref _selectedNode, value))
                return;

            SetDirectManipulationError(null);
            SetInspectorSelection(BuildInspectorSelection(value));
            OnPropertyChanged(nameof(SelectionBreadcrumb));
            OnPropertyChanged(nameof(SelectedPreviewNodeId));
            OnPropertyChanged(nameof(CanChangeSelectedItemType));
            OnPropertyChanged(nameof(CanDirectManipulateSelectedItem));
            OnPropertyChanged(nameof(SelectedItemType));
            PreviewDebug.SelectNode(value?.Kind, value?.NodeId);
            NotifyItemCommandsChanged();
        }
    }

    public MenuNodeId? SelectedPreviewNodeId => SelectedNode switch
    {
        { Kind: MenuOutlineNodeKind.Menu } when _snapshot is { } snapshot =>
            snapshot.Window.Id,
        { Kind: MenuOutlineNodeKind.Items } => null,
        { NodeId: { } nodeId } => nodeId,
        _ => null
    };

    public bool CanChangeSelectedItemType =>
        IsEditable &&
        !HasStagedInput &&
        SelectedNode is { Kind: MenuOutlineNodeKind.Item } &&
        SelectedItem() is { IsResolved: true };

    public bool CanDirectManipulateSelectedItem =>
        IsEditable &&
        !HasStagedInput &&
        PreviewDebug.IsAuthored &&
        _snapshot is { } snapshot &&
        SelectedItem() is { IsResolved: true } item &&
        SupportsDirectManipulation(
            snapshot.Window.Value.Rect,
            item.Value.Window.RectClient);

    public ItemDefType? SelectedItemType =>
        SelectedItem()?.Value.Type;

    public ViewModelCommand AddItemCommand { get; }
    public ViewModelCommand DuplicateItemCommand { get; }
    public ViewModelCommand RemoveItemCommand { get; }
    public ViewModelCommand MoveItemUpCommand { get; }
    public ViewModelCommand MoveItemDownCommand { get; }

    /// <summary>
    /// Raised by a view only after the user deliberately selects a Menu node.
    /// The hosting editor forwards this intent to the workbench.
    /// </summary>
    internal event EventHandler? PropertiesRevealRequested;

    public InspectorSelectionViewModel? InspectorSelection
    {
        get => _inspectorSelection;
        private set => SetProperty(ref _inspectorSelection, value);
    }

    public bool HasStagedInput
    {
        get => _hasStagedInput;
        private set => SetProperty(ref _hasStagedInput, value);
    }

    public string SelectionBreadcrumb => SelectedNode is null
        ? DocumentName
        : $"{DocumentName}  /  {SelectedNode.Title}";

    public string PreviewHeading => "Editor Preview";

    public string PreviewStatus
    {
        get
        {
            if (_directManipulationError is not null)
                return _directManipulationError;
            if (!HasDocument)
                return "No complete Menu definition is available to preview.";

            int fidelityIssueCount = PreviewScene!.FidelityIssues.Count(issue =>
                    issue.Severity == MenuPreviewFidelitySeverity.Warning) +
                _materialPreviewStatuses.Values.Sum(status =>
                    status.FidelityIssueCount) +
                _textPreviewStatuses.Values.Sum(status =>
                    status.FidelityIssueCount);
            int unavailableCount = _materialPreviewStatuses.Values.Count(
                status => !status.IsResolved);
            int fallbackTextCount = _textPreviewStatuses.Values.Count(
                status => !status.UsesGameGlyphs);
            var parts = new List<string>
            {
                PreviewDebug.IsSimulating
                    ? $"Simulation at {PreviewDebug.Simulation.Milliseconds:N0} ms"
                    : "Static authored preview"
            };
            if (PreviewDebug.IsSimulating && PreviewDebug.DiagnosticCount > 0)
                parts.Add(PreviewDebug.EvaluationSummary);
            if (unavailableCount > 0)
            {
                parts.Add(
                    $"{unavailableCount:N0} " +
                    $"material{(unavailableCount == 1 ? string.Empty : "s")} " +
                    "unavailable");
            }
            if (fallbackTextCount > 0)
            {
                parts.Add(
                    $"{fallbackTextCount:N0} text " +
                    $"run{(fallbackTextCount == 1 ? string.Empty : "s")} " +
                    "using fallback metrics");
            }
            if (fidelityIssueCount > 0)
            {
                parts.Add(
                    $"{fidelityIssueCount:N0} fidelity " +
                    $"issue{(fidelityIssueCount == 1 ? string.Empty : "s")}");
            }
            return string.Join(" · ", parts);
        }
    }

    public string PreviewDetails
    {
        get
        {
            if (_directManipulationError is not null)
                return _directManipulationError;
            if (!HasDocument)
                return PreviewStatus;

            string[] details = PreviewScene!.FidelityIssues
                .Where(issue =>
                    issue.Severity == MenuPreviewFidelitySeverity.Warning)
                .Select(issue => $"{issue.Path}: {issue.Message}")
                .Concat(_materialPreviewStatuses.Values
                    .OrderBy(status => status.MaterialName, StringComparer.Ordinal)
                    .Select(status => status.Detail))
                .Concat(_textPreviewStatuses.Values
                    .Where(status =>
                        !status.UsesGameGlyphs ||
                        status.Diagnostics.Count > 0)
                    .OrderBy(status => status.NodeId.ToString(), StringComparer.Ordinal)
                    .Select(status => status.Detail))
                .Concat(PreviewDebug.IsSimulating
                    ? PreviewDebug.DiagnosticLines
                    : [])
                .ToArray();
            return details.Length == 0
                ? PreviewStatus
                : string.Join(Environment.NewLine, details);
        }
    }

    internal Action<InspectorAssetReferencePropertyRowViewModel>?
        RequestAssetReferenceSelection => _requestAssetReferenceSelection;

    internal void RequestItemBehaviorEdit(MenuNodeId itemId)
    {
        if (!IsEditable || HasStagedInput ||
            _requestItemBehaviorEdit is null ||
            _snapshot is not { } snapshot)
        {
            return;
        }

        MenuItemSnapshot item = RequireItem(snapshot, itemId);
        if (!item.IsResolved)
            return;

        _requestItemBehaviorEdit(new MenuItemBehaviorEditRequestedEventArgs(
            item.Id,
            MenuPresentationText.ItemTitle(item.Value),
            item.Behavior,
            snapshot.ExpressionSupport,
            item.Value.Type == ItemDefType.ListBox,
            value => ApplyStructuralEdit(
                new ReplaceItemBehaviorEdit(item.Id, value))));
    }

    internal bool IsAssetReferenceMissing(
        XAssetType assetType,
        string? assetName) =>
        !string.IsNullOrWhiteSpace(assetName) &&
        _isAssetReferenceResolved is not null &&
        !_isAssetReferenceResolved(assetType, assetName);

    /// <summary>
    /// Replaces the immutable source after Revert, a structural edit, or
    /// external authority coordination. Any local staged input is cleared.
    /// </summary>
    public void ReplaceDocument(MenuEditorSnapshot? snapshot)
    {
        SetDirectManipulationError(null);
        MenuOutlineNodeKind? selectedKind = _selectedNode?.Kind;
        int? selectedItemIndex = _selectedNode?.ItemIndex;
        _snapshot = snapshot;
        PreviewDebug.ReplaceDocument(snapshot);
        string? selectedKey = _selectedNode?.Key;
        OutlineRoots = BuildOutline(snapshot);
        _selectedNode = FindNode(selectedKey) ??
            Flatten(OutlineRoots).FirstOrDefault(node =>
                node.Kind == selectedKind &&
                node.ItemIndex == selectedItemIndex) ??
            OutlineRoots.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedNode));
        SetInspectorSelection(BuildInspectorSelection(_selectedNode));
        PreviewDebug.SelectNode(_selectedNode?.Kind, _selectedNode?.NodeId);
        NotifyDocumentChanged();
    }

    public void SelectPreviewNode(MenuNodeId nodeId)
    {
        MenuOutlineNodeViewModel? node = Flatten(OutlineRoots)
            .FirstOrDefault(candidate => candidate.NodeId == nodeId);
        if (node is not null)
            SelectedNode = node;
    }

    internal void RequestPropertiesReveal() =>
        PropertiesRevealRequested?.Invoke(this, EventArgs.Empty);

    internal void ReportMaterialPreviewStatus(
        MenuPreviewMaterialStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        string key = XAssetStableIdentity.NormalizeLookupName(
            status.MaterialName);
        if (_materialPreviewStatuses.TryGetValue(
                key,
                out MenuPreviewMaterialStatus? current) &&
            current == status)
        {
            return;
        }

        _materialPreviewStatuses[key] = status;
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
    }

    internal void ReportTextPreviewStatus(MenuPreviewTextStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (_textPreviewStatuses.TryGetValue(
                status.NodeId,
                out MenuPreviewTextStatus? current) &&
            current.AuthoredText == status.AuthoredText &&
            current.UsesGameGlyphs == status.UsesGameGlyphs &&
            current.Diagnostics.SequenceEqual(
                status.Diagnostics,
                StringComparer.Ordinal))
        {
            return;
        }

        _textPreviewStatuses[status.NodeId] = status;
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
    }

    internal void RestoreSelection(
        MenuNodeId? nodeId,
        MenuOutlineNodeKind? kind,
        int? itemIndex)
    {
        if (kind is null)
            return;

        MenuOutlineNodeViewModel? node = Flatten(OutlineRoots)
            .FirstOrDefault(candidate =>
                nodeId is not null &&
                candidate.NodeId == nodeId) ??
            Flatten(OutlineRoots)
            .FirstOrDefault(candidate =>
                candidate.Kind == kind &&
                (kind != MenuOutlineNodeKind.Item ||
                 candidate.ItemIndex == itemIndex));
        if (node is not null)
            SelectedNode = node;
    }

    /// <summary>
    /// Applies one atomic union-arm change. Common Item/Window fields survive;
    /// type-specific payload fields are reset to valid defaults.
    /// </summary>
    public void ChangeSelectedItemType(ItemDefType type)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (!CanChangeSelectedItemType)
        {
            throw new InvalidOperationException(
                "Item Type cannot be changed until the selected item is " +
                "editable and any staged Properties value is resolved.");
        }
        MenuItemSnapshot item = SelectedItem() is { IsResolved: true } selected
            ? selected
            : throw new InvalidOperationException(
                "No resolved Menu Item is selected.");
        ApplyStructuralEdit(new ChangeMenuItemTypeEdit(item.Id, type));
    }

    private void AddItem()
    {
        int? insertionIndex = SelectedItemWithIndex() is { } selected
            ? selected.Index + 1
            : null;
        ApplyStructuralEdit(new AddMenuItemEdit(
            ItemDefType.Text,
            insertionIndex));
    }

    private void DuplicateSelectedItem()
    {
        if (SelectedItemWithIndex() is not { Item.IsResolved: true } selected)
            return;

        int insertionIndex = selected.Index + 1;
        MenuEditorSnapshot next = ApplyCore(new DuplicateMenuItemEdit(
            selected.Item.Id,
            insertionIndex,
            DuplicateItemOffset,
            DuplicateItemOffset));
        MenuNodeId duplicateId = next.Items[insertionIndex].Id;
        ReplaceDocument(next);
        SelectPreviewNode(duplicateId);
    }

    private void RemoveSelectedItem()
    {
        if (SelectedItemWithIndex() is not { } selected)
            return;
        ApplyStructuralEdit(new RemoveMenuItemEdit(selected.Item.Id));
    }

    private void MoveSelectedItemUp()
    {
        if (SelectedItemWithIndex() is not { Index: > 0 } selected)
            return;
        ApplyStructuralEdit(new MoveMenuItemEdit(
            selected.Item.Id,
            selected.Index - 1));
    }

    private void MoveSelectedItemDown()
    {
        if (SelectedItemWithIndex() is not { } selected ||
            selected.Index >= ItemCount - 1)
        {
            return;
        }
        ApplyStructuralEdit(new MoveMenuItemEdit(
            selected.Item.Id,
            selected.Index + 1));
    }

    private bool CanEditSelectedItem() =>
        IsEditable &&
        !HasStagedInput &&
        SelectedItemWithIndex() is not null;

    private bool CanDuplicateSelectedItem() =>
        CanEditSelectedItem() &&
        SelectedItem() is { IsResolved: true };

    private bool CanMoveSelectedItemUp() =>
        CanEditSelectedItem() &&
        SelectedItemWithIndex() is { Index: > 0 };

    private bool CanMoveSelectedItemDown() =>
        CanEditSelectedItem() &&
        SelectedItemWithIndex() is { } selected &&
        selected.Index < ItemCount - 1;

    public void UpdateSettings(
        Func<MenuSettingsValue, MenuSettingsValue> update) =>
        ApplyValue(
            snapshot => new ReplaceMenuSettingsEdit(update(snapshot.Settings)));

    public void UpdateRootWindow(
        Func<MenuWindowValue, MenuWindowValue> update) =>
        ApplyValue(
            snapshot => new ReplaceRootWindowEdit(update(snapshot.Window.Value)));

    public void UpdateItem(
        MenuNodeId itemId,
        Func<MenuItemValue, MenuItemValue> update) =>
        ApplyValue(snapshot =>
        {
            MenuItemSnapshot item = RequireItem(snapshot, itemId);
            return new ReplaceItemEdit(itemId, update(item.Value));
        });

    public void UpdateItemPayload(
        MenuNodeId itemId,
        Func<MenuItemValue, MenuItemValue> update) =>
        ApplyValue(snapshot =>
        {
            MenuItemSnapshot item = RequireItem(snapshot, itemId);
            return new ReplaceItemPayloadEdit(itemId, update(item.Value));
        });

    public void UpdateItemWindow(
        MenuNodeId itemId,
        Func<MenuWindowValue, MenuWindowValue> update) =>
        ApplyValue(snapshot =>
        {
            MenuItemSnapshot item = RequireItem(snapshot, itemId);
            return new ReplaceItemWindowEdit(itemId, update(item.Value.Window));
        });

    /// <summary>
    /// Commits one completed preview gesture through the same typed Menu
    /// authority used by Properties. Pointer-move candidates remain visual;
    /// only this gesture boundary creates a semantic document revision.
    /// </summary>
    public bool CommitPreviewItemGeometry(
        MenuNodeId itemId,
        MenuPreviewRect originalBounds,
        MenuPreviewRect candidateBounds)
    {
        if (!CanDirectManipulateSelectedItem ||
            SelectedItem() is not { IsResolved: true } item ||
            item.Id != itemId ||
            PreviewScene is not { } scene ||
            !IsFinite(originalBounds) ||
            !IsFinite(candidateBounds))
        {
            return false;
        }

        MenuWindowValue rootWindow = _snapshot!.Window.Value;
        float rootInset = rootWindow.Border == WindowBorder.WINDOW_BORDER_NONE
            ? 0
            : rootWindow.BorderSize;
        float itemInset = item.Value.Window.Border ==
            WindowBorder.WINDOW_BORDER_NONE
                ? 0
                : item.Value.Window.BorderSize;
        MenuRectangleValue screenRectangle = MenuRectTransform.ComposeItem(
            rootWindow.Rect,
            rootInset,
            itemInset,
            item.Value.Window.RectClient);
        MenuPreviewRect currentBounds = MenuRectTransform.Resolve(
            screenRectangle,
            scene.Settings);
        if (currentBounds != originalBounds ||
            candidateBounds == originalBounds)
        {
            return false;
        }

        MenuPreviewRect candidateVirtual = MenuRectTransform.Unresolve(
            candidateBounds,
            screenRectangle.HorizontalAlignment,
            screenRectangle.VerticalAlignment,
            scene.Settings);
        candidateVirtual = candidateVirtual with
        {
            Width = screenRectangle.Width < 0
                ? -candidateVirtual.Width
                : candidateVirtual.Width,
            Height = screenRectangle.Height < 0
                ? -candidateVirtual.Height
                : candidateVirtual.Height
        };
        MenuRectangleValue current = item.Value.Window.RectClient;
        var replacement = current with
        {
            X = current.X + candidateVirtual.X - screenRectangle.X,
            Y = current.Y + candidateVirtual.Y - screenRectangle.Y,
            Width = current.Width +
                candidateVirtual.Width - screenRectangle.Width,
            Height = current.Height +
                candidateVirtual.Height - screenRectangle.Height
        };
        if (!IsFinite(replacement) || replacement == current)
            return false;

        MenuEditorSnapshot next;
        try
        {
            next = ApplyCore(new ReplaceItemWindowEdit(
                itemId,
                item.Value.Window with { RectClient = replacement }));
        }
        catch (Exception exception) when (exception is
                   ArgumentException or
                   InvalidOperationException or
                   InvalidDataException or
                   OverflowException)
        {
            SetDirectManipulationError(
                $"Geometry change was not applied: {exception.Message}");
            return false;
        }

        // Unlike a Properties-originated scalar edit, direct manipulation
        // must rebuild the active rows so their RectClient inputs immediately
        // reflect the committed geometry.
        ReplaceDocument(next);
        return true;
    }

    public void ApplyStructuralEdit(MenuEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        MenuEditorSnapshot next = ApplyCore(edit);
        ReplaceDocument(next);
    }

    public bool ApplyStagedInput()
    {
        IInspectorStagedPropertyRow[] stagedRows = _observedRows
            .OfType<IInspectorStagedPropertyRow>()
            .Where(row => row.HasStagedValue)
            .ToArray();
        return stagedRows.Length == 1 && stagedRows[0].CommitInput();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_textResourceResolver is not null)
            _textResourceResolver.Changed -= TextResourceResolver_Changed;
        PreviewDebug.PreviewChanged -= PreviewDebug_PreviewChanged;
        StopObservingRows();
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyValue(Func<MenuEditorSnapshot, MenuEdit> createEdit)
    {
        ArgumentNullException.ThrowIfNull(createEdit);
        MenuEditorSnapshot snapshot = RequireEditableSnapshot();
        RefreshValueDocument(ApplyCore(createEdit(snapshot)));
    }

    /// <summary>
    /// Refreshes immutable document and preview state after a scalar edit
    /// without replacing the active inspector rows during their own input
    /// event. Structural edits still rebuild the complete selection model.
    /// </summary>
    private void RefreshValueDocument(MenuEditorSnapshot snapshot)
    {
        MenuNodeId? selectedNodeId = _selectedNode?.NodeId;
        MenuOutlineNodeKind? selectedKind = _selectedNode?.Kind;
        int? selectedItemIndex = _selectedNode?.ItemIndex;
        _snapshot = snapshot;
        PreviewDebug.ReplaceDocument(snapshot);
        OutlineRoots = BuildOutline(snapshot);
        _selectedNode = Flatten(OutlineRoots).FirstOrDefault(candidate =>
                selectedNodeId is not null &&
                candidate.NodeId == selectedNodeId) ??
            Flatten(OutlineRoots).FirstOrDefault(candidate =>
                candidate.Kind == selectedKind &&
                (candidate.Kind != MenuOutlineNodeKind.Item ||
                 candidate.ItemIndex == selectedItemIndex)) ??
            OutlineRoots.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(SelectedPreviewNodeId));
        OnPropertyChanged(nameof(SelectedItemType));
        OnPropertyChanged(nameof(SelectionBreadcrumb));
        PreviewDebug.SelectNode(_selectedNode?.Kind, _selectedNode?.NodeId);
        NotifyDocumentChanged();
    }

    private MenuEditorSnapshot ApplyCore(MenuEdit edit)
    {
        if (!IsEditable || _applyEdit is null)
            throw new InvalidOperationException("This Menu definition is read-only.");

        return _applyEdit(edit)
            ?? throw new InvalidDataException(
                "The Menu edit owner returned no updated snapshot.");
    }

    private MenuEditorSnapshot RequireEditableSnapshot() =>
        _snapshot is { IsComplete: true } snapshot && IsEditable
            ? snapshot
            : throw new InvalidOperationException(
                "This Menu definition is read-only or incomplete.");

    private static MenuItemSnapshot RequireItem(
        MenuEditorSnapshot snapshot,
        MenuNodeId itemId) =>
        snapshot.Items.SingleOrDefault(item => item.Id == itemId)
        ?? throw new InvalidOperationException(
            $"Menu item '{itemId}' is no longer present in the current draft.");

    private void NotifyDocumentChanged()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(HasNoDocument));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(DocumentName));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(PreviewScene));
        OnPropertyChanged(nameof(SelectedPreviewNodeId));
        OnPropertyChanged(nameof(CanChangeSelectedItemType));
        OnPropertyChanged(nameof(CanDirectManipulateSelectedItem));
        OnPropertyChanged(nameof(SelectedItemType));
        OnPropertyChanged(nameof(SelectionBreadcrumb));
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
        NotifyItemCommandsChanged();
    }

    private void PreviewDebug_PreviewChanged(object? sender, EventArgs args)
    {
        RefreshPreviewResourceIdentity();
        OnPropertyChanged(nameof(PreviewScene));
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
        OnPropertyChanged(nameof(CanDirectManipulateSelectedItem));
    }

    private void RefreshPreviewResourceIdentity()
    {
        MenuPreviewScene? scene = PreviewScene;
        PreviewMaterialResourceKey[] materials = scene is null
            ? []
            : scene.Primitives
                .SelectMany(PreviewMaterialResources)
                .Distinct()
                .OrderBy(value => value.Kind)
                .ThenBy(value => value.Name, StringComparer.Ordinal)
                .ThenBy(value => value.Font)
                .ThenBy(value => value.Scale)
                .ToArray();
        PreviewTextResourceKey[] texts = scene is null
            ? []
            : scene.Primitives
                .OfType<MenuPreviewText>()
                .Select(value => new PreviewTextResourceKey(
                    value.NodeId,
                    value.Text,
                    value.Font,
                    value.Scale,
                    value.Alignment,
                    value.Style))
                .ToArray();

        if (!_previewMaterialResources.SequenceEqual(materials))
            _materialPreviewStatuses.Clear();
        if (!_previewTextResources.SequenceEqual(texts))
            _textPreviewStatuses.Clear();

        _previewMaterialResources = materials;
        _previewTextResources = texts;
    }

    private static IEnumerable<PreviewMaterialResourceKey>
        PreviewMaterialResources(MenuPreviewPrimitive primitive)
    {
        if (primitive is MenuPreviewMaterial material)
        {
            yield return new PreviewMaterialResourceKey(
                0,
                XAssetStableIdentity.NormalizeLookupName(
                    material.MaterialName),
                0,
                0);
        }
        else if (primitive is MenuPreviewText text)
        {
            yield return new PreviewMaterialResourceKey(
                1,
                string.Empty,
                text.Font,
                text.Scale);
        }
    }

    private readonly record struct PreviewMaterialResourceKey(
        int Kind,
        string Name,
        int Font,
        float Scale);

    private readonly record struct PreviewTextResourceKey(
        MenuNodeId NodeId,
        string Text,
        int Font,
        float Scale,
        int Alignment,
        int Style);

    private void TextResourceResolver_Changed(object? sender, EventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
                TextResourceResolver_Changed(sender, args));
            return;
        }

        if (!_disposed)
        {
            // A font provider revision can remap the same Font/scale request
            // to a different glyph-atlas material without changing the Menu
            // scene's authored text identity.
            _materialPreviewStatuses.Clear();
            _textPreviewStatuses.Clear();
            PreviewDebug.RefreshTextResources();
        }
    }

    private void SetInspectorSelection(InspectorSelectionViewModel? value)
    {
        StopObservingRows();
        InspectorSelection = value;
        if (value is null)
        {
            SetStagingState(false);
            return;
        }

        foreach (InspectorPropertyRowViewModel row in
                 value.Sections.SelectMany(section => section.Rows))
        {
            row.PropertyChanged += InspectorRow_PropertyChanged;
            _observedRows.Add(row);
        }

        RefreshStagingState();
    }

    private void StopObservingRows()
    {
        foreach (InspectorPropertyRowViewModel row in _observedRows)
            row.PropertyChanged -= InspectorRow_PropertyChanged;
        _observedRows.Clear();
    }

    private void InspectorRow_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IInspectorStagedPropertyRow.HasStagedValue))
        {
            RefreshStagingState();
        }
    }

    private void RefreshStagingState()
    {
        InspectorPropertyRowViewModel[] stagedRows = _observedRows
            .OfType<IInspectorStagedPropertyRow>()
            .Where(row => row.HasStagedValue)
            .Cast<InspectorPropertyRowViewModel>()
            .ToArray();
        foreach (InspectorPropertyRowViewModel row in _observedRows)
        {
            row.SetInteractionBlocked(
                stagedRows.Length != 0 && !stagedRows.Contains(row));
        }
        SetStagingState(stagedRows.Length != 0);
    }

    private void SetStagingState(bool value)
    {
        HasStagedInput = value;
        OnPropertyChanged(nameof(CanChangeSelectedItemType));
        OnPropertyChanged(nameof(CanDirectManipulateSelectedItem));
        NotifyItemCommandsChanged();
    }

    private InspectorSelectionViewModel? BuildInspectorSelection(
        MenuOutlineNodeViewModel? node)
    {
        if (_snapshot is null || node is null)
            return null;

        return MenuInspectorProjection.Create(this, _snapshot, node);
    }

    private static IReadOnlyList<MenuOutlineNodeViewModel> BuildOutline(
        MenuEditorSnapshot? snapshot)
    {
        if (snapshot is not { IsComplete: true })
            return [];

        MenuOutlineNodeViewModel[] items = snapshot.Items
            .Select((item, index) =>
            {
                return new MenuOutlineNodeViewModel(
                    $"item:{item.Id}",
                    item.IsResolved
                        ? MenuPresentationText.ItemTitle(item.Value, index)
                        : $"Unresolved Item {index + 1:N0}",
                    MenuOutlineNodeKind.Item,
                    item.Id,
                    index);
            })
            .ToArray();

        var root = new MenuOutlineNodeViewModel(
            $"menu:{snapshot.Id}",
            MenuPresentationText.MenuTitle(snapshot.Name),
            MenuOutlineNodeKind.Menu,
            snapshot.Id,
            children:
            [
                new MenuOutlineNodeViewModel(
                    $"window:{snapshot.Window.Id}",
                    "Window",
                    MenuOutlineNodeKind.Window,
                    snapshot.Window.Id),
                new MenuOutlineNodeViewModel(
                    "items",
                    $"Items ({items.Length:N0})",
                    MenuOutlineNodeKind.Items,
                    children: items)
            ]);
        return Array.AsReadOnly([root]);
    }

    private MenuOutlineNodeViewModel? FindNode(string? key)
    {
        if (key is null)
            return null;

        return Flatten(OutlineRoots).FirstOrDefault(node =>
            string.Equals(node.Key, key, StringComparison.Ordinal));
    }

    private static IEnumerable<MenuOutlineNodeViewModel> Flatten(
        IEnumerable<MenuOutlineNodeViewModel> nodes)
    {
        foreach (MenuOutlineNodeViewModel node in nodes)
        {
            yield return node;
            foreach (MenuOutlineNodeViewModel child in Flatten(node.Children))
                yield return child;
        }
    }

    private MenuItemSnapshot? SelectedItem()
    {
        if (_snapshot is null ||
            SelectedNode is not
            {
                Kind: MenuOutlineNodeKind.Item,
                NodeId: { } nodeId
            })
        {
            return null;
        }

        return _snapshot.Items.FirstOrDefault(item => item.Id == nodeId);
    }

    private (MenuItemSnapshot Item, int Index)? SelectedItemWithIndex()
    {
        MenuItemSnapshot? selected = SelectedItem();
        if (selected is null || _snapshot is null)
            return null;

        int index = _snapshot.Items
            .Select((item, itemIndex) => (item, itemIndex))
            .Where(value => ReferenceEquals(value.item, selected))
            .Select(value => value.itemIndex)
            .DefaultIfEmpty(-1)
            .Single();
        return index < 0 ? null : (selected, index);
    }

    private void NotifyItemCommandsChanged()
    {
        AddItemCommand.RaiseCanExecuteChanged();
        DuplicateItemCommand.RaiseCanExecuteChanged();
        RemoveItemCommand.RaiseCanExecuteChanged();
        MoveItemUpCommand.RaiseCanExecuteChanged();
        MoveItemDownCommand.RaiseCanExecuteChanged();
    }

    private static bool SupportsDirectManipulation(HorizontalAlign value) =>
        (byte)value <=
        (byte)HorizontalAlign.HORIZONTAL_ALIGN_RIGHT_ADJUSTABLE;

    private static bool SupportsDirectManipulation(VerticalAlign value) =>
        (byte)value <=
        (byte)VerticalAlign.VERTICAL_ALIGN_BOTTOM_ADJUSTABLE;

    private static bool SupportsDirectManipulation(
        MenuRectangleValue root,
        MenuRectangleValue client)
    {
        bool inheritsRootAlignment =
            client.HorizontalAlignment ==
                HorizontalAlign.HORIZONTAL_ALIGN_SUBLEFT &&
            client.VerticalAlignment ==
                VerticalAlign.VERTICAL_ALIGN_SUBTOP;
        return SupportsDirectManipulation(inheritsRootAlignment
                ? root.HorizontalAlignment
                : client.HorizontalAlignment) &&
            SupportsDirectManipulation(inheritsRootAlignment
                ? root.VerticalAlignment
                : client.VerticalAlignment);
    }

    private static bool IsFinite(MenuPreviewRect value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Width) &&
        float.IsFinite(value.Height);

    private static bool IsFinite(MenuRectangleValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Width) &&
        float.IsFinite(value.Height);

    private void SetDirectManipulationError(string? value)
    {
        if (string.Equals(
                _directManipulationError,
                value,
                StringComparison.Ordinal))
        {
            return;
        }

        _directManipulationError = value;
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewDetails));
    }
}
