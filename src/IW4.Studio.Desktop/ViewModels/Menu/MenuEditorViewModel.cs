using System.ComponentModel;
using Avalonia.Threading;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Desktop host for one top-level Menu occurrence. Editable target rows route
/// through the document coordinator so duplicate top-level and inline
/// definitions always share one logical authority.
/// </summary>
public sealed class MenuEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorInspectorSource,
      IAssetEditorDiagnostics,
      IAssetEditorStagingState,
      IDisposable
{
    private readonly AssetEditorSession _session;
    private readonly MenuEditingCoordinator _coordinator;
    private readonly TargetZoneRowIdentity? _rowIdentity;
    private MenuAuthorityResolutionSnapshot? _resolution;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private string _statusMessage = string.Empty;
    private bool _pendingCoordinatorRefresh;
    private int _coordinatorMutationDepth;
    private bool _disposed;

    public MenuEditorViewModel(
        AssetEditorSession session,
        MenuEditingCoordinator coordinator,
        IMenuPreviewMaterialResolver materialResolver,
        IMenuTextResourceResolver textResourceResolver,
        bool canSelectAssetReferences = false,
        Func<XAssetType, string?, bool>? isAssetReferenceResolved = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _coordinator = coordinator ??
            throw new ArgumentNullException(nameof(coordinator));
        ArgumentNullException.ThrowIfNull(materialResolver);
        ArgumentNullException.ThrowIfNull(textResourceResolver);
        if (session.Entry.AssetType != XAssetType.Menu)
        {
            throw new InvalidDataException(
                "The Menu view model can host only Menu editor sessions.");
        }
        if (coordinator.DocumentId != session.Workspace.Document.TargetSource.DocumentId)
        {
            throw new InvalidOperationException(
                "The Menu coordinator belongs to another editing document.");
        }

        _rowIdentity = session.RowIdentity;
        MenuEditorSnapshot? snapshot = InitializeSnapshot();
        Designer = new MenuDesignerViewModel(
            snapshot,
            Mode == AssetEditorMode.Editable ? ApplyEdit : null,
            Mode == AssetEditorMode.Editable && canSelectAssetReferences
                ? RequestAssetReferenceSelection
                : null,
            materialResolver,
            textResourceResolver,
            () => IsEditable,
            isAssetReferenceResolved);
        Designer.PropertyChanged += Designer_PropertyChanged;
        _coordinator.Changed += Coordinator_Changed;

        RevertCommand = new ViewModelCommand(RevertDraft, CanRevert);
    }

    public AssetEditorMode Mode => _session.Mode;

    public bool IsEditable =>
        Mode == AssetEditorMode.Editable && _resolution?.CanEdit == true;

    public string Name =>
        Designer.Snapshot?.Name
        ?? _session.Entry.OriginalName
        ?? string.Empty;

    public int ItemCount => Designer.ItemCount;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public MenuDesignerViewModel Designer { get; }

    public ViewModelCommand RevertCommand { get; }

    public IReadOnlyList<AssetValidationIssue> Diagnostics
    {
        get => _diagnostics;
        private set
        {
            _diagnostics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiagnostics));
        }
    }

    public bool HasDiagnostics => Diagnostics.Count != 0;

    public string PropertySectionName => "Menu";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Items", ItemCount.ToString("N0")),
        new("Authority", AuthorityText(_resolution, IsEditable)),
        new("Selection", Designer.SelectedNode?.KindText ?? "None")
    ];

    public InspectorSelectionViewModel? InspectorSelection =>
        Designer.InspectorSelection;

    public bool HasUnappliedChanges => Designer.HasStagedInput;

    public event EventHandler<AssetReferenceSelectionRequestedEventArgs>?
        AssetReferenceSelectionRequested;

    public void RevertDraft()
    {
        if (!CanRevert() || _rowIdentity is not { } rowIdentity)
            return;

        MenuAuthorityResolutionSnapshot expectedResolution = _resolution
            ?? throw new InvalidOperationException(
                "This Menu has no authority resolution to revert.");
        MenuAuthorityEditResult result = RunCoordinator(() =>
            _coordinator.RevertTopLevelMenu(
                rowIdentity,
                expectedResolution));
        if (_disposed)
            return;
        _resolution = result.Resolution;
        Designer.ReplaceDocument(result.Resolution.Menu);
        RefreshValidation();
        StatusMessage = result.Changed
            ? "Reverted the logical Menu authority to its authored baseline."
            : "The logical Menu authority already matched its baseline.";
        NotifyEditorStateChanged();
    }

    public void ChangeSelectedItemType(ItemDefType type) =>
        Designer.ChangeSelectedItemType(type);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _coordinator.Changed -= Coordinator_Changed;
        Designer.PropertyChanged -= Designer_PropertyChanged;
        Designer.Dispose();
    }

    private MenuEditorSnapshot? InitializeSnapshot()
    {
        if (_rowIdentity is { } rowIdentity)
        {
            if (Mode == AssetEditorMode.Editable)
                _ = _session.OpenDraft<MenuDraft>();
            _resolution = _coordinator.ResolveTopLevelMenu(rowIdentity);
            RefreshValidation();
            StatusMessage = ResolutionStatus(_resolution, IsEditable);
            return _resolution.Menu;
        }

        if (Mode == AssetEditorMode.ReadOnly)
        {
            try
            {
                MenuEditorSnapshot snapshot = MenuReadOnlySnapshot
                    .CaptureResolvedProvider(_session)
                    .Menu;
                StatusMessage =
                    "Detached read-only copy of the catalog-resolved Menu provider.";
                return snapshot;
            }
            catch (InvalidDataException exception)
            {
                StatusMessage = exception.Message;
                Diagnostics = ProviderDiagnostic(exception);
                return null;
            }
        }

        StatusMessage =
            "Menu content is unavailable because this reference has no resolved provider.";
        return null;
    }

    private MenuEditorSnapshot ApplyEdit(MenuEdit edit)
    {
        if (_rowIdentity is not { } rowIdentity || !IsEditable)
            throw new InvalidOperationException("This Menu authority is read-only.");

        MenuAuthorityResolutionSnapshot expectedResolution = _resolution
            ?? throw new InvalidOperationException(
                "This Menu has no editable authority resolution.");
        MenuAuthorityEditResult result = RunCoordinator(() =>
            _coordinator.ApplyTopLevelMenuEdit(
                rowIdentity,
                expectedResolution,
                edit));
        _resolution = result.Resolution;
        RefreshValidation();
        StatusMessage = result.Changed
            ? "Applied the Menu change to its logical authority."
            : "The Menu authority already contained that value.";
        NotifyEditorStateChanged();
        return result.Resolution.Menu
            ?? throw new InvalidDataException(
                "The edited Menu authority returned no snapshot.");
    }

    private void RequestAssetReferenceSelection(
        InspectorAssetReferencePropertyRowViewModel row) =>
        AssetReferenceSelectionRequested?.Invoke(
            this,
            new AssetReferenceSelectionRequestedEventArgs(row));

    private bool CanRevert() =>
        Mode == AssetEditorMode.Editable &&
        !Designer.HasStagedInput &&
        _rowIdentity is { } rowIdentity &&
        _resolution is { } resolution &&
        resolution.Occurrences.Count(occurrence =>
            occurrence.MaterializesDefinition) == 1 &&
        resolution.Owner is
        {
            Kind: MenuAuthorityOccurrenceKind.TopLevelDefinition
        } owner &&
        owner.RowIdentity == rowIdentity;

    private void Coordinator_Changed(
        object? sender,
        MenuEditingCoordinatorChangedEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Coordinator_Changed(sender, args));
            return;
        }
        // Authority resolutions carry the document-wide editing revision, so
        // even an edit to another logical Menu invalidates this snapshot.
        if (
            _disposed ||
            _rowIdentity is null ||
            _coordinatorMutationDepth != 0)
            return;
        if (Designer.HasStagedInput)
        {
            _pendingCoordinatorRefresh = true;
            StatusMessage =
                "The document changed in another editor; reset the staged " +
                "Properties value to refresh before applying it again.";
            return;
        }

        RefreshFromCoordinator();
    }

    private void RefreshFromCoordinator()
    {
        if (_rowIdentity is not { } rowIdentity)
            return;
        try
        {
            _resolution = _coordinator.ResolveTopLevelMenu(rowIdentity);
            Designer.ReplaceDocument(_resolution.Menu);
            RefreshValidation();
            StatusMessage = ResolutionStatus(_resolution, IsEditable);
            _pendingCoordinatorRefresh = false;
            NotifyEditorStateChanged();
        }
        catch (Exception exception) when (exception is
                   KeyNotFoundException or
                   InvalidOperationException or
                   InvalidDataException)
        {
            Designer.ReplaceDocument(null);
            StatusMessage = exception.Message;
        }
    }

    private void Designer_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MenuDesignerViewModel.InspectorSelection))
            OnPropertyChanged(nameof(InspectorSelection));
        if (args.PropertyName == nameof(MenuDesignerViewModel.HasStagedInput))
        {
            OnPropertyChanged(nameof(HasUnappliedChanges));
            NotifyCommandsChanged();
            if (!Designer.HasStagedInput && _pendingCoordinatorRefresh)
                RefreshFromCoordinator();
        }
        if (args.PropertyName is nameof(MenuDesignerViewModel.SelectedNode) or
            nameof(MenuDesignerViewModel.Snapshot))
        {
            NotifyCommandsChanged();
            if (args.PropertyName == nameof(MenuDesignerViewModel.Snapshot))
            {
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(ItemCount));
            }
            OnPropertyChanged(nameof(EditorProperties));
        }
    }

    private void RefreshValidation()
    {
        var issues = new List<AssetValidationIssue>();
        if (_resolution is { } resolution)
        {
            issues.AddRange(resolution.OwnerValidationIssues);
            issues.AddRange(resolution.Issues.Select(issue =>
                new AssetValidationIssue(
                    "menu.authority",
                    issue.Message,
                    AssetValidationSeverity.Error)));
        }
        Diagnostics = DistinctIssues(issues);
    }

    private T RunCoordinator<T>(Func<T> action)
    {
        _coordinatorMutationDepth++;
        try
        {
            return action();
        }
        finally
        {
            _coordinatorMutationDepth--;
        }
    }

    private void NotifyEditorStateChanged()
    {
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(InspectorSelection));
        NotifyCommandsChanged();
    }

    private void NotifyCommandsChanged()
    {
        RevertCommand.RaiseCanExecuteChanged();
    }

    private static string ResolutionStatus(
        MenuAuthorityResolutionSnapshot resolution,
        bool canEditHere) => (resolution.Kind, canEditHere) switch
    {
        (MenuAuthorityResolutionKind.Editable, false) when
            resolution.Owner is { } owner =>
            $"This occurrence is read-only; its logical authority is owned by row {owner.RowIdentity.SerializedIndex:N0}.",
        (MenuAuthorityResolutionKind.Editable, true) when resolution.Owner is
            { Kind: MenuAuthorityOccurrenceKind.MenuFileInlineDefinition } owner =>
            $"Editing the first inline authority in MenuFile row {owner.RowIdentity.SerializedIndex:N0}. Recursive behavior is preserved read-only.",
        (MenuAuthorityResolutionKind.Editable, true) =>
            "Editing the logical Menu authority. Recursive behavior is preserved read-only.",
        (MenuAuthorityResolutionKind.ReadOnlyProvider, _) =>
            "This Menu is supplied by a read-only dependency provider.",
        (MenuAuthorityResolutionKind.Conflict, _) =>
            "Conflicting complete definitions exist for this Menu; editing and Save are blocked until authority is unambiguous.",
        _ => "No complete Menu definition is available."
    };

    private static string AuthorityText(
        MenuAuthorityResolutionSnapshot? resolution,
        bool canEditHere) => (resolution?.Kind, canEditHere) switch
    {
        (MenuAuthorityResolutionKind.Editable, true) => "Editable",
        (MenuAuthorityResolutionKind.Editable, false) => "Owner elsewhere",
        (MenuAuthorityResolutionKind.ReadOnlyProvider, _) => "Dependency",
        (MenuAuthorityResolutionKind.Conflict, _) => "Conflict",
        (MenuAuthorityResolutionKind.Unavailable, _) => "Unavailable",
        _ => "—"
    };

    private static IReadOnlyList<AssetValidationIssue> ProviderDiagnostic(
        InvalidDataException exception) =>
    [
        new("provider", exception.Message, AssetValidationSeverity.Error)
    ];

    private static IReadOnlyList<AssetValidationIssue> DistinctIssues(
        IEnumerable<AssetValidationIssue> issues) =>
        Array.AsReadOnly(issues
            .GroupBy(
                issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray());
}
