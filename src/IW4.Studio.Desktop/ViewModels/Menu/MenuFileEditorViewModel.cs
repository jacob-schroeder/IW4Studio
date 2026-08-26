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
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Desktop host for one ordered MenuFile registration list. The selected
/// logical Menu is resolved through the same document authority as a
/// top-level Menu tab, regardless of whether this registration is inline or
/// packed.
/// </summary>
public sealed class MenuFileEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorPropertiesRevealSource,
      IAssetEditorInspectorSource,
      IAssetEditorDiagnostics,
      IAssetEditorStagingState,
      IDisposable
{
    private readonly AssetEditorSession _session;
    private readonly MenuEditingCoordinator _coordinator;
    private readonly IMenuPreviewMaterialResolver _materialResolver;
    private readonly IMenuTextResourceResolver _textResourceResolver;
    private readonly TargetZoneRowIdentity? _rowIdentity;
    private readonly bool _canSelectAssetReferences;
    private readonly Func<XAssetType, string?, bool>?
        _isAssetReferenceResolved;
    private MenuFileEditorSnapshot? _snapshot;
    private MenuAuthorityResolutionSnapshot? _selectedResolution;
    private IReadOnlyList<MenuFileRegistrationViewModel> _registrations = [];
    private MenuFileRegistrationViewModel? _selectedRegistration;
    private MenuDesignerViewModel _designer;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private string _statusMessage = string.Empty;
    private bool _canRevertWithoutSplittingAuthority;
    private bool _pendingCoordinatorRefresh;
    private int _coordinatorMutationDepth;
    private bool _disposed;

    public MenuFileEditorViewModel(
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
        _materialResolver = materialResolver ??
            throw new ArgumentNullException(nameof(materialResolver));
        _textResourceResolver = textResourceResolver ??
            throw new ArgumentNullException(nameof(textResourceResolver));
        if (session.Entry.AssetType != XAssetType.MenuFile)
        {
            throw new InvalidDataException(
                "The MenuFile view model can host only MenuFile editor sessions.");
        }
        if (coordinator.DocumentId != session.Workspace.Document.DocumentId)
        {
            throw new InvalidOperationException(
                "The Menu coordinator belongs to another editing document.");
        }

        _rowIdentity = session.RowIdentity;
        _canSelectAssetReferences = canSelectAssetReferences;
        _isAssetReferenceResolved = isAssetReferenceResolved;
        InitializeSnapshot();
        _designer = new MenuDesignerViewModel(
            snapshot: null,
            materialResolver: _materialResolver,
            textResourceResolver: _textResourceResolver,
            isAssetReferenceResolved: _isAssetReferenceResolved);
        _designer.PropertyChanged += Designer_PropertyChanged;
        _designer.PropertiesRevealRequested += Designer_PropertiesRevealRequested;
        if (Mode == WorkspaceAssetAccess.Editable)
            _coordinator.Changed += Coordinator_Changed;

        RevertCommand = new ViewModelCommand(RevertDraft, CanRevert);
        RemoveRegistrationCommand = new ViewModelCommand(
            RemoveSelectedRegistration,
            CanEditSelectedRegistration);
        MoveRegistrationUpCommand = new ViewModelCommand(
            MoveSelectedRegistrationUp,
            CanMoveSelectedRegistrationUp);
        MoveRegistrationDownCommand = new ViewModelCommand(
            MoveSelectedRegistrationDown,
            CanMoveSelectedRegistrationDown);
        RebuildRegistrations(selectedId: null);
    }

    public WorkspaceAssetAccess Mode => _session.Mode;
    public bool IsEditable => Mode == WorkspaceAssetAccess.Editable;
    public bool CanAddRegistration => IsEditable && !Designer.HasStagedInput;
    public bool CanRetargetRegistration =>
        CanAddRegistration && SelectedRegistration is not null;
    public bool CanDuplicateRegistration =>
        CanAddRegistration &&
        SelectedRegistration is { IsEditableDefinition: true };

    public string Name =>
        _snapshot?.Name
        ?? _session.Entry.OriginalName
        ?? string.Empty;

    public int MenuCount => Registrations.Count;

    public IReadOnlyList<MenuFileRegistrationViewModel> Registrations
    {
        get => _registrations;
        private set => SetProperty(ref _registrations, value);
    }

    public MenuFileRegistrationViewModel? SelectedRegistration
    {
        get => _selectedRegistration;
        set
        {
            if (ReferenceEquals(value, _selectedRegistration) ||
                Designer.HasStagedInput)
            {
                if (Designer.HasStagedInput)
                    OnPropertyChanged();
                return;
            }
            MenuFileRegistrationViewModel? previous = _selectedRegistration;
            if (!SetProperty(ref _selectedRegistration, value))
                return;

            AttachDesigner(
                value,
                preserveSelection: previous?.Id == value?.Id);
            OnPropertyChanged(nameof(HasSelectedRegistration));
            OnPropertyChanged(nameof(CanRetargetRegistration));
            OnPropertyChanged(nameof(CanDuplicateRegistration));
            OnPropertyChanged(nameof(EditorProperties));
            NotifyCommandsChanged();
        }
    }

    public bool HasSelectedRegistration => SelectedRegistration is not null;

    public MenuDesignerViewModel Designer
    {
        get => _designer;
        private set => SetProperty(ref _designer, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ViewModelCommand RevertCommand { get; }
    public ViewModelCommand RemoveRegistrationCommand { get; }
    public ViewModelCommand MoveRegistrationUpCommand { get; }
    public ViewModelCommand MoveRegistrationDownCommand { get; }

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
    public string PropertySectionName => "MenuFile";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Registrations", MenuCount.ToString("N0")),
        new("Selected", SelectedRegistration?.Name ?? "None"),
        new("Authority", AuthorityText(
            _selectedResolution,
            Designer.IsEditable))
    ];

    public InspectorSelectionViewModel? InspectorSelection =>
        Designer.InspectorSelection;
    public bool HasUnappliedChanges => Designer.HasStagedInput;

    public event EventHandler<AssetReferenceSelectionRequestedEventArgs>?
        AssetReferenceSelectionRequested;

    public event EventHandler<MenuItemBehaviorEditRequestedEventArgs>?
        ItemBehaviorEditRequested;

    public event EventHandler<MenuDefinitionBehaviorEditRequestedEventArgs>?
        MenuBehaviorEditRequested;

    public event EventHandler? PropertiesRevealRequested;

    internal void RequestPropertiesReveal() =>
        PropertiesRevealRequested?.Invoke(this, EventArgs.Empty);

    public void AddExistingMenu(string menuName, int? insertIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuName);
        if (!IsEditable || _rowIdentity is not { } rowIdentity)
            throw new InvalidOperationException("This MenuFile is read-only.");

        HashSet<MenuRegistrationId> previous = (_snapshot?.Registrations ?? [])
            .Select(value => value.Id)
            .ToHashSet();
        int? effectiveIndex = insertIndex ??
            (SelectedRegistration is { } selected ? selected.Index + 1 : null);
        MenuFileEditResult result = RunCoordinator(() =>
            _coordinator.ApplyMenuFileEdit(
                rowIdentity,
                new AddExistingMenuRegistrationEdit(menuName, effectiveIndex)));
        _snapshot = result.MenuFile;
        MenuRegistrationId? added = _snapshot.Registrations
            .Where(value => !previous.Contains(value.Id))
            .Select(value => (MenuRegistrationId?)value.Id)
            .FirstOrDefault();
        RefreshValidation();
        RebuildRegistrations(added);
        StatusMessage = result.Changed
            ? $"Added Menu registration '{menuName}'."
            : "The MenuFile already contained that registration state.";
        NotifyEditorStateChanged();
    }

    public void RetargetSelectedRegistration(string menuName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuName);
        if (SelectedRegistration is not { } selected)
            throw new InvalidOperationException("No MenuFile registration is selected.");
        if (SameLogicalName(selected.MenuName, menuName))
        {
            StatusMessage =
                "The selected registration already targets that logical Menu.";
            return;
        }
        ApplyStructuralEdit(new RetargetMenuFileRegistrationEdit(
            selected.Id,
            menuName));
    }

    public bool WouldDiscardInlineDefinitionOnRetarget(string menuName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuName);
        return SelectedRegistration is
            {
                IsEditableDefinition: true,
                MenuName: { } currentName
            } && !SameLogicalName(currentName, menuName);
    }

    public string? ValidateNewMenuName(string menuName) =>
        _coordinator.ValidateNewMenuName(menuName);

    public void DuplicateSelectedRegistration(string menuName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuName);
        if (SelectedRegistration is not
            {
                IsEditableDefinition: true
            } selected)
        {
            throw new InvalidOperationException(
                "Select an inline Menu definition to duplicate.");
        }

        ApplyStructuralEdit(
            new DuplicateMenuFileRegistrationEdit(
                selected.Id,
                menuName,
                selected.Index + 1),
            selectNewRegistration: true);
        StatusMessage =
            $"Duplicated Menu '{selected.Name}' as '{menuName}'.";
    }

    public void RevertDraft()
    {
        if (!CanRevert() || _rowIdentity is not { } rowIdentity)
            return;

        MenuRegistrationId? selectedId = SelectedRegistration?.Id;
        MenuFileRevertResult result = RunCoordinator(() =>
            _coordinator.RevertMenuFile(rowIdentity));
        if (_disposed)
            return;
        _snapshot = result.MenuFile;
        RefreshValidation();
        RebuildRegistrations(selectedId);
        StatusMessage = result.Changed
            ? "Reverted the complete MenuFile row to its authored baseline."
            : "The MenuFile already matched its baseline.";
        NotifyEditorStateChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _coordinator.Changed -= Coordinator_Changed;
        Designer.PropertyChanged -= Designer_PropertyChanged;
        Designer.PropertiesRevealRequested -= Designer_PropertiesRevealRequested;
        Designer.Dispose();
    }

    private void InitializeSnapshot()
    {
        if (Mode == WorkspaceAssetAccess.Editable && _rowIdentity is { } rowIdentity)
        {
            _ = _session.OpenDraft<MenuFileDraft>();
            _snapshot = _coordinator.ReadMenuFile(rowIdentity);
            _canRevertWithoutSplittingAuthority =
                _coordinator.CanRevertMenuFile(rowIdentity);
            RefreshValidation();
            StatusMessage =
                "Editing the ordered MenuFile registration list through document Menu authority.";
            return;
        }

        if (Mode == WorkspaceAssetAccess.ReadOnly)
        {
            try
            {
                _snapshot = MenuFileReadOnlySnapshot
                    .CaptureResolvedProvider(_session)
                    .MenuFile;
                StatusMessage =
                    "Detached read-only copy of the catalog-resolved MenuFile provider.";
            }
            catch (InvalidDataException exception)
            {
                StatusMessage = exception.Message;
                Diagnostics = ProviderDiagnostic(exception);
            }
            return;
        }

        StatusMessage =
            "MenuFile content is unavailable because this reference has no resolved provider.";
    }

    private MenuEditorSnapshot ApplyRegistrationMenuEdit(
        MenuRegistrationId registrationId,
        MenuEdit edit)
    {
        if (_rowIdentity is not { } rowIdentity)
            throw new InvalidOperationException("This MenuFile is read-only.");

        MenuAuthorityResolutionSnapshot expectedResolution =
            _selectedResolution
            ?? throw new InvalidOperationException(
                "The selected Menu has no editable authority resolution.");
        MenuAuthorityEditResult result = RunCoordinator(() =>
            _coordinator.ApplyMenuFileRegistrationEdit(
                rowIdentity,
                registrationId,
                expectedResolution,
                edit));
        _selectedResolution = result.Resolution;
        _snapshot = _coordinator.ReadMenuFile(rowIdentity);
        RefreshValidation();
        StatusMessage = result.Changed
            ? "Applied the selected Menu change to its logical authority."
            : "The selected Menu authority already contained that value.";
        NotifyEditorStateChanged();
        return result.Resolution.Menu
            ?? throw new InvalidDataException(
                "The selected Menu authority returned no snapshot.");
    }

    private void ApplyStructuralEdit(
        MenuFileEdit edit,
        bool selectNewRegistration = false)
    {
        if (!IsEditable || _rowIdentity is not { } rowIdentity)
            throw new InvalidOperationException("This MenuFile is read-only.");
        HashSet<MenuRegistrationId>? previousIds = selectNewRegistration
            ? (_snapshot?.Registrations ?? [])
                .Select(value => value.Id)
                .ToHashSet()
            : null;
        MenuRegistrationId? selectedId = SelectedRegistration?.Id;
        MenuFileEditResult result = RunCoordinator(() =>
            _coordinator.ApplyMenuFileEdit(rowIdentity, edit));
        _snapshot = result.MenuFile;
        if (previousIds is not null)
        {
            selectedId = _snapshot.Registrations
                .Where(value => !previousIds.Contains(value.Id))
                .Select(value => (MenuRegistrationId?)value.Id)
                .FirstOrDefault() ?? selectedId;
        }
        RefreshValidation();
        RebuildRegistrations(selectedId);
        StatusMessage = result.Changed
            ? "Applied the MenuFile registration change."
            : "The MenuFile already contained that registration state.";
        NotifyEditorStateChanged();
    }

    private void RemoveSelectedRegistration()
    {
        if (SelectedRegistration is not { } selected)
            return;
        ApplyStructuralEdit(new RemoveMenuFileRegistrationEdit(selected.Id));
    }

    private void MoveSelectedRegistrationUp()
    {
        if (SelectedRegistration is not { Index: > 0 } selected)
            return;
        ApplyStructuralEdit(new MoveMenuFileRegistrationEdit(
            selected.Id,
            selected.Index - 1));
    }

    private void MoveSelectedRegistrationDown()
    {
        if (SelectedRegistration is not { } selected ||
            selected.Index >= MenuCount - 1)
        {
            return;
        }
        ApplyStructuralEdit(new MoveMenuFileRegistrationEdit(
            selected.Id,
            selected.Index + 1));
    }

    private bool CanRevert() =>
        IsEditable &&
        _session.HasUnsavedChanges &&
        !Designer.HasStagedInput &&
        _rowIdentity is not null &&
        _canRevertWithoutSplittingAuthority;
    private bool CanEditSelectedRegistration() =>
        IsEditable && !Designer.HasStagedInput && SelectedRegistration is not null;
    private bool CanMoveSelectedRegistrationUp() =>
        CanEditSelectedRegistration() && SelectedRegistration is { Index: > 0 };
    private bool CanMoveSelectedRegistrationDown() =>
        CanEditSelectedRegistration() &&
        SelectedRegistration is { } selected &&
        selected.Index < MenuCount - 1;

    private void RebuildRegistrations(MenuRegistrationId? selectedId)
    {
        Registrations = Array.AsReadOnly(
            (_snapshot?.Registrations ?? [])
            .Select(value => new MenuFileRegistrationViewModel(value))
            .ToArray());
        SelectedRegistration = selectedId is { } id
            ? Registrations.FirstOrDefault(value => value.Id == id)
                ?? Registrations.FirstOrDefault()
            : Registrations.FirstOrDefault();
        OnPropertyChanged(nameof(MenuCount));
        OnPropertyChanged(nameof(CanAddRegistration));
        OnPropertyChanged(nameof(CanRetargetRegistration));
        OnPropertyChanged(nameof(CanDuplicateRegistration));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void AttachDesigner(
        MenuFileRegistrationViewModel? registration,
        bool preserveSelection)
    {
        MenuNodeId? selectedNodeId = preserveSelection
            ? Designer.SelectedNode?.NodeId
            : null;
        MenuOutlineNodeKind? selectedKind = preserveSelection
            ? Designer.SelectedNode?.Kind
            : null;
        int? selectedItemIndex = preserveSelection
            ? Designer.SelectedNode?.ItemIndex
            : null;
        Designer.PropertyChanged -= Designer_PropertyChanged;
        Designer.PropertiesRevealRequested -= Designer_PropertiesRevealRequested;
        Designer.Dispose();
        _selectedResolution = null;
        MenuEditorSnapshot? menu = null;
        Func<MenuEdit, MenuEditorSnapshot>? apply = null;
        if (registration is not null &&
            !string.IsNullOrWhiteSpace(registration.Snapshot.Name))
        {
            _selectedResolution = _coordinator.ResolveMenu(
                registration.Snapshot.Name!);
            menu = _selectedResolution.Menu ?? registration.Snapshot.Menu;
            if (IsEditable && _selectedResolution.CanEdit)
            {
                apply = edit => ApplyRegistrationMenuEdit(
                    registration.Id,
                    edit);
            }
        }
        else
        {
            menu = registration?.Snapshot.Menu;
        }

        Designer = new MenuDesignerViewModel(
            menu,
            apply,
            _canSelectAssetReferences && apply is not null
                ? RequestAssetReferenceSelection
                : null,
            _materialResolver,
            _textResourceResolver,
            apply is null
                ? null
                : () => _selectedResolution?.CanEdit == true,
            _isAssetReferenceResolved,
            RequestItemBehaviorEdit,
            RequestMenuBehaviorEdit);
        Designer.RestoreSelection(
            selectedNodeId,
            selectedKind,
            selectedItemIndex);
        Designer.PropertyChanged += Designer_PropertyChanged;
        Designer.PropertiesRevealRequested += Designer_PropertiesRevealRequested;
        RefreshValidation();
        OnPropertyChanged(nameof(InspectorSelection));
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void RequestAssetReferenceSelection(
        InspectorAssetReferencePropertyRowViewModel row) =>
        AssetReferenceSelectionRequested?.Invoke(
            this,
            new AssetReferenceSelectionRequestedEventArgs(row));

    private void RequestItemBehaviorEdit(
        MenuItemBehaviorEditRequestedEventArgs args) =>
        ItemBehaviorEditRequested?.Invoke(this, args);

    private void RequestMenuBehaviorEdit(
        MenuDefinitionBehaviorEditRequestedEventArgs args) =>
        MenuBehaviorEditRequested?.Invoke(this, args);

    private void Designer_PropertiesRevealRequested(
        object? sender,
        EventArgs e) =>
        RequestPropertiesReveal();

    private void Coordinator_Changed(
        object? sender,
        MenuEditingCoordinatorChangedEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Coordinator_Changed(sender, args));
            return;
        }
        // Both the MenuFile and selected Menu authority carry the document-
        // wide editing revision; every coordinator mutation rebases them.
        if (
            _disposed ||
            Mode != WorkspaceAssetAccess.Editable ||
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
            MenuRegistrationId? selectedId = SelectedRegistration?.Id;
            _snapshot = _coordinator.ReadMenuFile(rowIdentity);
            RebuildRegistrations(selectedId);
            RefreshValidation();
            StatusMessage =
                "Refreshed MenuFile registrations and logical Menu authority.";
            _pendingCoordinatorRefresh = false;
            NotifyEditorStateChanged();
        }
        catch (Exception exception) when (exception is
                   KeyNotFoundException or
                   InvalidOperationException or
                   InvalidDataException)
        {
            _snapshot = null;
            _canRevertWithoutSplittingAuthority = false;
            RebuildRegistrations(null);
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
            OnPropertyChanged(nameof(CanAddRegistration));
            OnPropertyChanged(nameof(CanRetargetRegistration));
            OnPropertyChanged(nameof(CanDuplicateRegistration));
            NotifyCommandsChanged();
            if (!Designer.HasStagedInput && _pendingCoordinatorRefresh)
                RefreshFromCoordinator();
        }
    }

    private void RefreshValidation()
    {
        var issues = new List<AssetValidationIssue>();
        if (Mode == WorkspaceAssetAccess.Editable && _session.IsDraftOpen)
            issues.AddRange(_session.RefreshValidation().Issues);
        if (_selectedResolution is { } resolution)
        {
            issues.AddRange(resolution.OwnerValidationIssues);
            issues.AddRange(resolution.Issues.Select(issue =>
                new AssetValidationIssue(
                    "menu.authority",
                    issue.Message,
                    AssetValidationSeverity.Error)));
        }
        Diagnostics = Array.AsReadOnly(issues
            .GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray());
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
        if (IsEditable && _rowIdentity is { } rowIdentity)
        {
            _canRevertWithoutSplittingAuthority =
                _coordinator.CanRevertMenuFile(rowIdentity);
        }
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(MenuCount));
        OnPropertyChanged(nameof(CanAddRegistration));
        OnPropertyChanged(nameof(CanRetargetRegistration));
        OnPropertyChanged(nameof(CanDuplicateRegistration));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(InspectorSelection));
        NotifyCommandsChanged();
    }

    private void NotifyCommandsChanged()
    {
        RevertCommand.RaiseCanExecuteChanged();
        RemoveRegistrationCommand.RaiseCanExecuteChanged();
        MoveRegistrationUpCommand.RaiseCanExecuteChanged();
        MoveRegistrationDownCommand.RaiseCanExecuteChanged();
    }

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

    private static bool SameLogicalName(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(
            XAssetStableIdentity.NormalizeLookupName(left),
            XAssetStableIdentity.NormalizeLookupName(right),
            StringComparison.Ordinal);
}
