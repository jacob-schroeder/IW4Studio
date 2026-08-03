using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Lifecycle;

/// <summary>
/// A destructive transition requested while an editing workspace is active.
/// The action names are deliberately independent of any particular Avalonia
/// event so menu, chrome, and application-lifetime routes share one policy.
/// </summary>
public enum DestructiveNavigationAction
{
    OpenAnother,
    CloseEditorTab,
    CloseEditorTabs,
    Exit,
    WindowClose,
    ApplicationShutdown
}

/// <summary>
/// Choices presented for one destructive action. Save is offered only when a
/// whole-document Save As capability check succeeded.
/// </summary>
public enum UnsavedChangesDecision
{
    Cancel = 0,
    DiscardChanges = 1,
    Save = 2
}

/// <summary>Ownership boundary for the changes described by a prompt.</summary>
public enum UnsavedChangesScope
{
    Workspace,
    EditorInput
}

/// <summary>
/// The observable result of a destructive-navigation request.
/// </summary>
public enum DestructiveNavigationResult
{
    Proceeded,
    Cancelled,
    Coalesced,
    Failed
}

/// <summary>
/// Application-level save outcome used by destructive-navigation policy.
/// This keeps the lifecycle boundary independent of the concrete fastfile or
/// compiled-map Save As pipeline that handled the request.
/// </summary>
public enum WorkspaceSaveStatus
{
    Succeeded,
    Cancelled,
    Failed
}

public readonly record struct WorkspaceSaveOutcome(
    WorkspaceSaveStatus Status)
{
    public bool Succeeded => Status == WorkspaceSaveStatus.Succeeded;
    public bool Cancelled => Status == WorkspaceSaveStatus.Cancelled;

    public static WorkspaceSaveOutcome Success { get; } =
        new(WorkspaceSaveStatus.Succeeded);

    public static WorkspaceSaveOutcome Cancellation { get; } =
        new(WorkspaceSaveStatus.Cancelled);

    public static WorkspaceSaveOutcome Failure { get; } =
        new(WorkspaceSaveStatus.Failed);

    public WorkspaceSaveOutcome Validate()
    {
        if (!Enum.IsDefined(Status))
        {
            throw new InvalidOperationException(
                $"A workspace save returned unknown status value {(int)Status}.");
        }

        return this;
    }
}

/// <summary>
/// Immutable contribution from an editor document whose changes are not held
/// by <see cref="FastFileEditingSession"/>.
/// </summary>
public readonly record struct SupplementalUnsavedChanges(
    bool IsDirty,
    int ChangedItemCount)
{
    public static SupplementalUnsavedChanges Clean { get; } = new(
        IsDirty: false,
        ChangedItemCount: 0);

    public SupplementalUnsavedChanges Validate()
    {
        if (ChangedItemCount < 0)
        {
            throw new InvalidOperationException(
                "An unsaved-change source reported a negative item count.");
        }

        if (!IsDirty && ChangedItemCount != 0)
        {
            throw new InvalidOperationException(
                "A clean unsaved-change source reported changed items.");
        }

        if (IsDirty && ChangedItemCount == 0)
        {
            throw new InvalidOperationException(
                "A dirty unsaved-change source reported no changed items.");
        }

        return this;
    }
}

/// <summary>
/// Immutable prompt data for either workspace changes or transient editor
/// input. It deliberately contains no runtime asset or mutable draft data.
/// </summary>
public sealed record UnsavedChangesPrompt(
    DestructiveNavigationAction Action,
    string FastFilePath,
    int ChangedItemCount,
    bool CanSave,
    UnsavedChangesScope Scope)
{
    public string FastFileName => Path.GetFileName(FastFilePath);
}

/// <summary>
/// UI boundary for the unsaved-change decision.
/// </summary>
public interface IUnsavedChangesDialog
{
    Task<UnsavedChangesDecision> ShowAsync(UnsavedChangesPrompt prompt);
}

/// <summary>
/// Serializes destructive workspace transitions. A single active request owns
/// the prompt and transition; overlapping requests are coalesced, so they
/// neither display a second dialog nor execute a second disposal/replacement.
/// </summary>
public sealed class DestructiveNavigationCoordinator
{
    private readonly object _gate = new();
    private object? _activeNavigation;

    /// <summary>
    /// Requests permission and, only after a clean session or an explicit
    /// discard decision, invokes <paramref name="proceedAsync"/> exactly once.
    /// Cancel, an unsupported future decision, a prompt failure, or a
    /// transition failure never authorize another caller to continue.
    /// </summary>
    public Task<DestructiveNavigationResult> NavigateAsync(
        FastFileEditingSession session,
        DestructiveNavigationAction action,
        IUnsavedChangesDialog dialog,
        Func<Task> proceedAsync,
        Func<Task<WorkspaceSaveOutcome>>? saveAsync = null,
        Func<SupplementalUnsavedChanges>? supplementalChanges = null,
        Func<SupplementalUnsavedChanges>? stagedEditorChanges = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(proceedAsync);
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action));

        if (!TryBeginNavigation(out object navigation))
            return Task.FromResult(DestructiveNavigationResult.Coalesced);

        return NavigateCoreAsync(
            session,
            action,
            dialog,
            proceedAsync,
            saveAsync,
            supplementalChanges,
            stagedEditorChanges,
            navigation);
    }

    /// <summary>
    /// Requests permission to close one editor tab. Only input that has not
    /// been applied to its session draft is destructive at tab scope; applied
    /// document changes remain pending in the workspace after the view closes.
    /// </summary>
    public Task<DestructiveNavigationResult> CloseEditorTabAsync(
        FastFileEditingSession session,
        bool hasUnappliedChanges,
        IUnsavedChangesDialog dialog,
        Func<Task> proceedAsync)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(proceedAsync);

        if (!TryBeginNavigation(out object navigation))
            return Task.FromResult(DestructiveNavigationResult.Coalesced);

        return CloseEditorTabsCoreAsync(
            session,
            hasUnappliedChanges ? 1 : 0,
            DestructiveNavigationAction.CloseEditorTab,
            dialog,
            proceedAsync,
            navigation);
    }

    /// <summary>
    /// Requests permission to close several editor tabs as one operation.
    /// Discarding drops only unapplied view input and never reverts a
    /// session-owned asset draft.
    /// </summary>
    public Task<DestructiveNavigationResult> CloseEditorTabsAsync(
        FastFileEditingSession session,
        int unappliedTabCount,
        IUnsavedChangesDialog dialog,
        Func<Task> proceedAsync)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(unappliedTabCount);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(proceedAsync);

        if (!TryBeginNavigation(out object navigation))
            return Task.FromResult(DestructiveNavigationResult.Coalesced);

        return CloseEditorTabsCoreAsync(
            session,
            unappliedTabCount,
            DestructiveNavigationAction.CloseEditorTabs,
            dialog,
            proceedAsync,
            navigation);
    }

    private async Task<DestructiveNavigationResult> NavigateCoreAsync(
        FastFileEditingSession session,
        DestructiveNavigationAction action,
        IUnsavedChangesDialog dialog,
        Func<Task> proceedAsync,
        Func<Task<WorkspaceSaveOutcome>>? saveAsync,
        Func<SupplementalUnsavedChanges>? supplementalChanges,
        Func<SupplementalUnsavedChanges>? stagedEditorChanges,
        object navigation)
    {
        try
        {
            SupplementalUnsavedChanges stagedChanges =
                CaptureSupplementalChanges(stagedEditorChanges);
            if (stagedChanges.IsDirty)
            {
                var prompt = new UnsavedChangesPrompt(
                    action,
                    session.Workspace.Document.Request.Path,
                    stagedChanges.ChangedItemCount,
                    CanSave: false,
                    UnsavedChangesScope.EditorInput);
                UnsavedChangesDecision decision = await dialog.ShowAsync(prompt);
                if (decision != UnsavedChangesDecision.DiscardChanges)
                    return DestructiveNavigationResult.Cancelled;
            }

            WorkspaceUnsavedChanges beforeDecision =
                CaptureWorkspaceChanges(
                    session,
                    supplementalChanges);
            if (beforeDecision.IsDirty)
            {
                var prompt = new UnsavedChangesPrompt(
                    action,
                    session.Workspace.Document.Request.Path,
                    beforeDecision.ChangedItemCount,
                    CanSave: saveAsync is not null,
                    UnsavedChangesScope.Workspace);
                UnsavedChangesDecision decision = await dialog.ShowAsync(prompt);
                switch (decision)
                {
                    case UnsavedChangesDecision.DiscardChanges:
                        break;
                    case UnsavedChangesDecision.Save when saveAsync is not null:
                    {
                        WorkspaceSaveOutcome save =
                            (await saveAsync()).Validate();
                        if (save.Cancelled)
                            return DestructiveNavigationResult.Cancelled;
                        if (!save.Succeeded)
                            return DestructiveNavigationResult.Failed;

                        WorkspaceUnsavedChanges afterSave =
                            CaptureWorkspaceChanges(
                                session,
                                supplementalChanges);
                        if (afterSave.IsDirty)
                        {
                            return DestructiveNavigationResult.Failed;
                        }
                        break;
                    }
                    default:
                        return DestructiveNavigationResult.Cancelled;
                }
            }

            await proceedAsync();
            return DestructiveNavigationResult.Proceeded;
        }
        catch (OperationCanceledException)
        {
            return DestructiveNavigationResult.Cancelled;
        }
        catch
        {
            // The only safe result when confirmation or transition state is
            // unavailable is to leave the current workspace in place.
            return DestructiveNavigationResult.Failed;
        }
        finally
        {
            EndNavigation(navigation);
        }
    }

    private async Task<DestructiveNavigationResult> CloseEditorTabsCoreAsync(
        FastFileEditingSession session,
        int unappliedTabCount,
        DestructiveNavigationAction action,
        IUnsavedChangesDialog dialog,
        Func<Task> proceedAsync,
        object navigation)
    {
        try
        {
            if (unappliedTabCount != 0)
            {
                var prompt = new UnsavedChangesPrompt(
                    action,
                    session.Workspace.Document.Request.Path,
                    ChangedItemCount: unappliedTabCount,
                    CanSave: false,
                    UnsavedChangesScope.EditorInput);
                UnsavedChangesDecision decision = await dialog.ShowAsync(prompt);
                if (decision != UnsavedChangesDecision.DiscardChanges)
                {
                    return DestructiveNavigationResult.Cancelled;
                }
            }

            await proceedAsync();
            return DestructiveNavigationResult.Proceeded;
        }
        catch (OperationCanceledException)
        {
            return DestructiveNavigationResult.Cancelled;
        }
        catch
        {
            return DestructiveNavigationResult.Failed;
        }
        finally
        {
            EndNavigation(navigation);
        }
    }

    private bool TryBeginNavigation(out object navigation)
    {
        navigation = new object();
        lock (_gate)
        {
            if (_activeNavigation is not null)
                return false;

            _activeNavigation = navigation;
            return true;
        }
    }

    private void EndNavigation(object navigation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeNavigation, navigation))
                _activeNavigation = null;
        }
    }

    private static SupplementalUnsavedChanges CaptureSupplementalChanges(
        Func<SupplementalUnsavedChanges>? source) =>
        (source?.Invoke() ?? SupplementalUnsavedChanges.Clean).Validate();

    private static WorkspaceUnsavedChanges CaptureWorkspaceChanges(
        FastFileEditingSession session,
        Func<SupplementalUnsavedChanges>? supplementalChanges)
    {
        AssetChangeSet sessionChanges = session.ChangeSet;
        SupplementalUnsavedChanges supplemental =
            CaptureSupplementalChanges(supplementalChanges);
        int changedItemCount = checked(
            sessionChanges.ChangedRowCount +
            supplemental.ChangedItemCount);
        bool isDirty =
            !sessionChanges.IsEmpty ||
            supplemental.IsDirty;
        if (isDirty != (changedItemCount != 0))
        {
            throw new InvalidOperationException(
                "Workspace dirty state and changed-item count are inconsistent.");
        }

        return new WorkspaceUnsavedChanges(
            isDirty,
            changedItemCount);
    }

    private readonly record struct WorkspaceUnsavedChanges(
        bool IsDirty,
        int ChangedItemCount);
}
