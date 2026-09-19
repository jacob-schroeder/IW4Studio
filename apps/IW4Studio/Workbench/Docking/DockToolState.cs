using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// Bindable runtime state for a registered workbench tool.
/// </summary>
public sealed class DockToolState : ObservableObject
{
    private bool _isActive;
    private int _position;

    internal DockToolState(DockToolDescriptor descriptor, int position)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _position = position;
    }

    public DockToolDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string Title => Descriptor.Title;

    public string IconToken => Descriptor.IconToken;

    public bool IsImplemented => Descriptor.IsImplemented;

    public DockRegion AllowedRegion => Descriptor.AllowedRegion;

    public DockRailGroup RailGroup => Descriptor.RailGroup;

    public int Position
    {
        get => _position;
        private set => SetProperty(ref _position, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    internal void SetPosition(int position) => Position = position;

    internal void SetActive(bool isActive) => IsActive = isActive;
}
