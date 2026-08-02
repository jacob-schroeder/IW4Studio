using System.Collections.ObjectModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// Observable state for one fixed workbench region.
/// </summary>
public sealed class DockRegionState : ObservableObject
{
    private readonly ObservableCollection<DockToolState> _tools;
    private DockToolState? _activeTool;
    private double _size;

    internal DockRegionState(
        DockRegion region,
        DockRegionSizeLimits sizeLimits,
        IEnumerable<DockToolState> tools)
    {
        Region = region;
        SizeLimits = sizeLimits ?? throw new ArgumentNullException(nameof(sizeLimits));
        _size = sizeLimits.Initial;
        _tools = new ObservableCollection<DockToolState>(
            tools ?? throw new ArgumentNullException(nameof(tools)));
        Tools = new ReadOnlyObservableCollection<DockToolState>(_tools);
    }

    public DockRegion Region { get; }

    public DockRegionSizeLimits SizeLimits { get; }

    public ReadOnlyObservableCollection<DockToolState> Tools { get; }

    public DockToolState? ActiveTool
    {
        get => _activeTool;
        private set => SetProperty(ref _activeTool, value);
    }

    public string? ActiveToolId => ActiveTool?.Id;

    public bool IsOpen => ActiveTool is not null;

    /// <summary>
    /// The last requested open size. Collapsing a region does not discard it.
    /// </summary>
    public double Size
    {
        get => _size;
        private set => SetProperty(ref _size, value);
    }

    internal void SetActiveTool(DockToolState? tool)
    {
        if (tool is not null && !_tools.Contains(tool))
            throw new ArgumentException("The active tool must belong to this region.", nameof(tool));

        if (ReferenceEquals(ActiveTool, tool))
            return;

        ActiveTool?.SetActive(false);
        ActiveTool = tool;
        ActiveTool?.SetActive(true);
        OnPropertyChanged(nameof(ActiveToolId));
        OnPropertyChanged(nameof(IsOpen));
    }

    internal double Resize(double requestedSize)
    {
        Size = SizeLimits.Clamp(requestedSize);
        return Size;
    }

    internal void Move(int oldIndex, int newIndex)
    {
        _tools.Move(oldIndex, newIndex);

        int start = Math.Min(oldIndex, newIndex);
        int end = Math.Max(oldIndex, newIndex);
        for (int index = start; index <= end; index++)
            _tools[index].SetPosition(index);
    }
}
