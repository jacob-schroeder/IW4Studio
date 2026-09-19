using System.ComponentModel;

namespace IW4.Studio.Desktop.Workbench.Selection;

public sealed class WorkbenchSelectionChangedEventArgs : EventArgs
{
    public WorkbenchSelectionChangedEventArgs(
        WorkbenchAssetSelection? previous,
        WorkbenchAssetSelection? current)
    {
        Previous = previous;
        Current = current;
    }

    public WorkbenchAssetSelection? Previous { get; }

    public WorkbenchAssetSelection? Current { get; }
}

/// <summary>
/// Shared selection seam for navigator tools, the Properties tool, and the
/// center editor host.
/// </summary>
public interface IWorkbenchSelectionContext : INotifyPropertyChanged
{
    WorkbenchAssetSelection? Current { get; }

    event EventHandler<WorkbenchSelectionChangedEventArgs>? SelectionChanged;

    void Select(WorkbenchAssetSelection selection);

    void Clear(WorkbenchAssetSelectionSource source);
}

/// <summary>
/// Window-local selection context. Clearing is source-aware so a stale
/// navigator cannot erase a newer selection published by another tool.
/// </summary>
public sealed class WorkbenchSelectionContext : IWorkbenchSelectionContext
{
    private WorkbenchAssetSelection? _current;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<WorkbenchSelectionChangedEventArgs>? SelectionChanged;

    public WorkbenchAssetSelection? Current => _current;

    public void Select(WorkbenchAssetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        SetCurrent(selection);
    }

    public void Clear(WorkbenchAssetSelectionSource source)
    {
        if (source == WorkbenchAssetSelectionSource.None)
            throw new ArgumentOutOfRangeException(nameof(source));
        if (_current?.Source != source)
            return;

        SetCurrent(null);
    }

    private void SetCurrent(WorkbenchAssetSelection? selection)
    {
        if (Equals(_current, selection))
            return;

        WorkbenchAssetSelection? previous = _current;
        _current = selection;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        SelectionChanged?.Invoke(
            this,
            new WorkbenchSelectionChangedEventArgs(previous, selection));
    }
}
