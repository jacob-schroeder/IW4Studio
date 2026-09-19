using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Tools.GscUsages;

public sealed class GscUsageActivatedEventArgs : EventArgs
{
    public GscUsageActivatedEventArgs(GscUsagePresentationItem usage) =>
        Usage = usage ?? throw new ArgumentNullException(nameof(usage));

    public GscUsagePresentationItem Usage { get; }
}

/// <summary>Snapshot-based model for Rider-style GSC usage results.</summary>
public sealed class GscUsagesToolViewModel : ObservableObject
{
    private IReadOnlyList<GscUsagePresentationItem> _items = [];
    private string _symbolName = string.Empty;

    public event EventHandler<GscUsageActivatedEventArgs>? UsageActivated;

    public IReadOnlyList<GscUsagePresentationItem> Items
    {
        get => _items;
        private set
        {
            if (!SetProperty(ref _items, value))
                return;

            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ResultText));
        }
    }

    public string SymbolName
    {
        get => _symbolName;
        private set
        {
            if (!SetProperty(ref _symbolName, value))
                return;

            OnPropertyChanged(nameof(ResultText));
        }
    }

    public int Count => Items.Count;

    public bool HasItems => Count != 0;

    public string ResultText => SymbolName.Length == 0
        ? "No GSC reference search has run."
        : Count == 1
            ? $"1 reference to '{SymbolName}'"
            : $"{Count:N0} references to '{SymbolName}'";

    public void Replace(GscUsagePresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        SymbolName = presentation.SymbolName;
        Items = presentation.Items;
    }

    public void Clear()
    {
        SymbolName = string.Empty;
        Items = [];
    }

    public void Activate(GscUsagePresentationItem usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (!Items.Contains(usage))
            return;

        UsageActivated?.Invoke(this, new GscUsageActivatedEventArgs(usage));
    }
}

/// <summary>
/// Narrow service injected into editor surfaces that can publish usage results.
/// </summary>
public interface IGscUsagesPresenter
{
    void Present(GscUsagePresentation presentation);
}

/// <summary>
/// Connects editor result publication, the docked tool model, and source
/// navigation without exposing workbench composition to a RawFile editor.
/// </summary>
public sealed class GscUsagesPresenter : IGscUsagesPresenter, IDisposable
{
    private readonly GscUsagesToolViewModel _tool;
    private readonly IGscSourceNavigator _navigator;
    private bool _disposed;

    public GscUsagesPresenter(
        GscUsagesToolViewModel tool,
        IGscSourceNavigator navigator)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _tool.UsageActivated += Tool_UsageActivated;
    }

    public event EventHandler? PresentationRequested;

    public void Present(GscUsagePresentation presentation)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GscUsagesPresenter));

        _tool.Replace(presentation);
        PresentationRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _tool.UsageActivated -= Tool_UsageActivated;
        PresentationRequested = null;
    }

    private void Tool_UsageActivated(
        object? sender,
        GscUsageActivatedEventArgs args) =>
        _navigator.NavigateTo(args.Usage.Location);
}
