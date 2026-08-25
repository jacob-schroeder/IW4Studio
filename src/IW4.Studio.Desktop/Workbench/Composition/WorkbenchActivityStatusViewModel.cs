using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Composition;

/// <summary>Shared presentation state for the workbench activity indicator.</summary>
public sealed class WorkbenchActivityStatusViewModel : ObservableObject
{
    private string _label = string.Empty;
    private bool _isActive;

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    internal void Begin(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
        IsActive = true;
    }

    internal void Clear()
    {
        IsActive = false;
        Label = string.Empty;
    }
}
